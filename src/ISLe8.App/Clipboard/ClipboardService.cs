using System.Diagnostics;
using System.Runtime.InteropServices;
using ISLe8.Core.Clipboard;
using ISLe8.Core.Diagnostics;
using ISLe8.Core.Settings;
using ISLe8.UI;
using Microsoft.UI.Dispatching;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using WinClipboard = Windows.ApplicationModel.DataTransfer.Clipboard;

namespace ISLe8.ClipboardTab;

/// <summary>
/// Records what you copy and puts it back on request (ClipboardService.swift). The Mac checks the
/// pasteboard's change counter twice a second; Windows says when the clipboard changes
/// (WM_CLIPBOARDUPDATE), so nothing polls. Content marked private (password managers, Windows'
/// own "don't keep" flags) and copies from excluded programs are never read into the history.
/// </summary>
internal sealed partial class ClipboardService
{
    private const uint WM_CLIPBOARDUPDATE = 0x031D;
    /// <summary>ISLe8's own write raises an update too; it is not recorded again.</summary>
    private static readonly TimeSpan OwnWriteQuiet = TimeSpan.FromSeconds(1);

    private readonly AppSettings _settings;
    private readonly nint _hwnd;
    private readonly DispatcherQueue _queue;
    private bool _listening;
    private DateTimeOffset _ownWriteAt = DateTimeOffset.MinValue;
    private DispatcherQueueTimer? _copiedReset;
    private string _query = "";

    public ClipboardService(AppSettings settings, ClipboardHistory history, nint hwnd, DispatcherQueue queue)
    {
        _settings = settings;
        History = history;
        _hwnd = hwnd;
        _queue = queue;
        Reload();
    }

    public ClipboardHistory History { get; }

    public IReadOnlyList<ClipboardEntry> Items { get; private set; } = [];

    /// <summary>Briefly set after an entry was copied, for the check mark.</summary>
    public long? LastCopiedId { get; private set; }

    public string Query
    {
        get => _query;
        set
        {
            _query = value;
            Reload();
        }
    }

    public event Action? Changed;

    /// <summary>The island's window hands its messages here.</summary>
    public nint? OnMessage(uint message, nint wParam, nint lParam)
    {
        if (message != WM_CLIPBOARDUPDATE) return null;
        _queue.Post(() => _ = ReadAsync());
        return 0;
    }

    /// <summary>Starts or stops listening as Settings › Clipboard says.</summary>
    public void ApplySettings()
    {
        var listen = _settings.RecordClipboard && History.IsAvailable;
        if (listen == _listening) return;
        if (listen)
        {
            _listening = AddClipboardFormatListener(_hwnd);
            if (!_listening) Log.Info("clipboard listener refused");
        }
        else
        {
            RemoveClipboardFormatListener(_hwnd);
            _listening = false;
        }
    }

    public void Reload()
    {
        Items = History.Items(_query, Math.Max(_settings.ClipboardLimit, 50) + 50);
        Changed?.Invoke();
    }

    // MARK: Recording

    private async Task ReadAsync()
    {
        if (!_listening || DateTimeOffset.Now - _ownWriteAt < OwnWriteQuiet) return;
        if (IsPrivate()) return;
        var app = ForegroundApp();
        if (app is not null && _settings.ClipboardExcludedApps.Contains(app, StringComparer.OrdinalIgnoreCase)) return;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (await Capture(WinClipboard.GetContent()) is not { } capture) return;
                History.Record(capture, app, DateTimeOffset.Now, _settings.ClipboardLimit, TimeSpan.FromDays(_settings.ClipboardMaxAgeDays));
                Reload();
                return;
            }
            catch (Exception e) when (e is COMException or UnauthorizedAccessException or InvalidOperationException)
            {
                // The program that copied often still holds the clipboard open for a moment; asked again
                // a little later, it has let go. Refused three times, its data went away: the next copy will do.
                if (attempt < 3)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(150 * attempt));
                    continue;
                }
                Log.Error(e, "clipboard read");
                return;
            }
        }
    }

    private static async Task<ClipboardCapture?> Capture(DataPackageView view)
    {
        // Explorer puts both the files and their names on the clipboard; the files win.
        if (view.Contains(StandardDataFormats.StorageItems))
        {
            var items = await view.GetStorageItemsAsync();
            var paths = items.Select(item => item.Path).Where(path => !string.IsNullOrEmpty(path)).ToList();
            if (paths.Count > 0) return ClipboardCapture.ForFiles(paths);
        }
        if (view.Contains(StandardDataFormats.Text))
        {
            var text = await view.GetTextAsync();
            if (string.IsNullOrWhiteSpace(text) || text.Length > ClipboardRules.MaxTextLength) return null;
            string? rtf = null;
            if (view.Contains(StandardDataFormats.Rtf))
            {
                var candidate = await view.GetRtfAsync();
                if (candidate.Length <= ClipboardRules.MaxRtfSize) rtf = candidate;
            }
            return ClipboardCapture.ForText(text, rtf);
        }
        if (view.Contains(StandardDataFormats.Bitmap))
        {
            var reference = await view.GetBitmapAsync();
            using var source = await reference.OpenReadAsync();
            if (source.Size > ClipboardRules.MaxImageSize) return null;
            var decoder = await BitmapDecoder.CreateAsync(source);
            using var png = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateForTranscodingAsync(png, decoder);
            if (decoder.DecoderInformation?.CodecId != BitmapDecoder.PngDecoderId)
            {
                encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, png);
                encoder.SetSoftwareBitmap(await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied));
            }
            await encoder.FlushAsync();
            if (png.Size > ClipboardRules.MaxImageSize) return null;
            var bytes = new byte[png.Size];
            using var reader = new DataReader(png.GetInputStreamAt(0));
            await reader.LoadAsync((uint)png.Size);
            reader.ReadBytes(bytes);
            return ClipboardCapture.ForImage(bytes);
        }
        return null;
    }

    /// <summary>Formats that say "don't record this", or Windows' history flags set to 0.</summary>
    private bool IsPrivate()
    {
        foreach (var name in ClipboardRules.Excluding)
        {
            if (IsClipboardFormatAvailable(RegisterClipboardFormat(name))) return true;
        }
        var flags = ClipboardRules.AllowFlags.Select(RegisterClipboardFormat).Where(IsClipboardFormatAvailable).ToList();
        if (flags.Count == 0 || !OpenClipboard(_hwnd)) return false;
        try
        {
            foreach (var format in flags)
            {
                var handle = GetClipboardData(format);
                if (handle == 0) continue;
                var data = GlobalLock(handle);
                if (data == 0) continue;
                try
                {
                    if (Marshal.ReadInt32(data) == 0) return true;
                }
                finally
                {
                    GlobalUnlock(handle);
                }
            }
            return false;
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>The program in front, as its .exe name, which is what Settings › Clipboard lists.</summary>
    private static string? ForegroundApp()
    {
        var window = GetForegroundWindow();
        if (window == 0 || GetWindowThreadProcessId(window, out var pid) == 0) return null;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName + ".exe";
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    // MARK: Using the history

    public string ImagePath(ClipboardEntry entry) => Path.Combine(History.ImagesFolder, entry.ImageName ?? "");

    /// <summary>Puts the entry back on the clipboard and moves it to the top of the history.</summary>
    public async Task Copy(ClipboardEntry entry)
    {
        var package = new DataPackage();
        try
        {
            switch (entry.Kind)
            {
                case ClipboardKind.RichText:
                    if (entry.Rtf is { } rtf) package.SetRtf(rtf);
                    package.SetText(entry.Text);
                    break;
                case ClipboardKind.Url:
                    if (Uri.TryCreate(entry.Text.Trim(), UriKind.Absolute, out var uri)) package.SetWebLink(uri);
                    package.SetText(entry.Text);
                    break;
                case ClipboardKind.Image:
                    var file = await StorageFile.GetFileFromPathAsync(ImagePath(entry));
                    package.SetBitmap(RandomAccessStreamReference.CreateFromFile(file));
                    break;
                case ClipboardKind.Files:
                    var items = new List<IStorageItem>();
                    foreach (var path in entry.Text.Split('\n'))
                    {
                        if (File.Exists(path)) items.Add(await StorageFile.GetFileFromPathAsync(path));
                        else if (Directory.Exists(path)) items.Add(await StorageFolder.GetFolderFromPathAsync(path));
                    }
                    package.SetStorageItems(items);
                    break;
                default:
                    package.SetText(entry.Text);
                    break;
            }
            _ownWriteAt = DateTimeOffset.Now;
            WinClipboard.SetContent(package);
            WinClipboard.Flush();
        }
        catch (Exception e) when (e is COMException or IOException or UnauthorizedAccessException or FileNotFoundException)
        {
            Log.Error(e, "clipboard copy");
            return;
        }
        History.Touch(entry.Id, DateTimeOffset.Now);
        LastCopiedId = entry.Id;
        _copiedReset ??= CreateCopiedReset();
        _copiedReset.Stop();
        _copiedReset.Start();
        Reload();
    }

    /// <summary>Copies, then presses Ctrl+V in the window that had the keyboard (the island never takes it).</summary>
    public async Task Paste(ClipboardEntry entry, Action closeIsland)
    {
        await Copy(entry);
        closeIsland();
        await Task.Delay(150);
        PressPaste();
    }

    public void TogglePin(ClipboardEntry entry)
    {
        History.SetPinned(entry.Id, !entry.IsPinned);
        Reload();
    }

    public void Delete(ClipboardEntry entry)
    {
        History.Delete(entry.Id);
        Reload();
    }

    public void Clear()
    {
        History.Clear();
        Reload();
    }

    private DispatcherQueueTimer CreateCopiedReset()
    {
        var timer = _queue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(1.2);
        timer.IsRepeating = false;
        timer.OnTick(() =>
        {
            LastCopiedId = null;
            Changed?.Invoke();
        });
        return timer;
    }

    private static void PressPaste()
    {
        const ushort control = 0x11, v = 0x56;
        const uint keyUp = 0x0002;
        Input[] inputs =
        [
            Key(control, 0), Key(v, 0), Key(v, keyUp), Key(control, keyUp),
        ];
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
    }

    private static Input Key(ushort key, uint flags) => new() { Type = 1, Keyboard = new KeyboardInput { VirtualKey = key, Flags = flags } };

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    /// <summary>INPUT with its union sized for MOUSEINPUT, the largest member, so SendInput accepts the size.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct Input
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public KeyboardInput Keyboard;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AddClipboardFormatListener(nint hwnd);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RemoveClipboardFormatListener(nint hwnd);

    [LibraryImport("user32.dll", EntryPoint = "RegisterClipboardFormatW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterClipboardFormat(string name);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsClipboardFormatAvailable(uint format);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();

    [LibraryImport("user32.dll")]
    private static partial nint GetClipboardData(uint format);

    [LibraryImport("kernel32.dll")]
    private static partial nint GlobalLock(nint handle);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(nint handle);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint count, [In] Input[] inputs, int size);
}

/// <summary>A system-wide shortcut on the island's window (RegisterHotKey), such as Ctrl+Alt+V.</summary>
internal sealed partial class HotKey
{
    private const uint WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_NOREPEAT = 0x4000;
    private readonly nint _hwnd;
    private readonly int _id;
    private readonly Action _pressed;

    /// <summary>Ctrl+Alt+<paramref name="key"/>; false when another program already has it.</summary>
    public HotKey(nint hwnd, int id, uint key, Action pressed)
    {
        _hwnd = hwnd;
        _id = id;
        _pressed = pressed;
        IsRegistered = RegisterHotKey(hwnd, id, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, key);
        if (!IsRegistered) Log.Info($"shortcut {id} is taken by another program");
    }

    public bool IsRegistered { get; }

    public nint? OnMessage(uint message, nint wParam, nint lParam)
    {
        if (message != WM_HOTKEY || wParam != _id) return null;
        _pressed();
        return 0;
    }

    public void Dispose()
    {
        if (IsRegistered) UnregisterHotKey(_hwnd, _id);
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(nint hwnd, int id);
}
