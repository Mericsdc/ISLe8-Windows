using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using ISLe8.Core.Diagnostics;
using ISLe8.Core.Shelf;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace ISLe8.ShelfTab;

/// <summary>What you can do with files on the shelf (ShelfActions.swift).</summary>
internal static partial class ShelfActions
{
    public static async void Open(string path)
    {
        try
        {
            if (Directory.Exists(path)) await Windows.System.Launcher.LaunchFolderPathAsync(path);
            else await Windows.System.Launcher.LaunchFileAsync(await StorageFile.GetFileFromPathAsync(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or COMException)
        {
            Log.Error(e, "shelf open");
        }
    }

    /// <summary>The Mac's "Finder'da göster": Explorer with the file selected.</summary>
    public static void Reveal(string path)
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Error(e, "shelf reveal");
        }
    }

    /// <summary>Zips a file or folder next to it (or into the shelf's folder if that isn't writable); the folder keeps its name inside.</summary>
    public static Task<string?> Zip(string path, ShelfStore store) => Task.Run<string?>(() =>
    {
        var name = Path.GetFileName(path.TrimEnd('\\', '/'));
        var destination = ShelfStore.UniquePath(store.WritableFolderNext(path), name, "zip");
        try
        {
            if (Directory.Exists(path))
            {
                ZipFile.CreateFromDirectory(path, destination, CompressionLevel.Optimal, includeBaseDirectory: true);
            }
            else
            {
                using var archive = ZipFile.Open(destination, ZipArchiveMode.Create);
                archive.CreateEntryFromFile(path, name, CompressionLevel.Optimal);
            }
            return destination;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Log.Error(e, "shelf zip");
            return null;
        }
    });

    /// <summary>Writes the image as PNG or JPEG next to the original.</summary>
    public static async Task<string?> Convert(string path, bool jpeg, ShelfStore store)
    {
        var destination = ShelfStore.UniquePath(store.WritableFolderNext(path), Path.GetFileNameWithoutExtension(path), jpeg ? "jpg" : "png");
        try
        {
            var source = await StorageFile.GetFileFromPathAsync(path);
            using var input = await source.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(input);
            using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                new BitmapTransform(), ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb);
            var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(destination)!);
            var file = await folder.CreateFileAsync(Path.GetFileName(destination), CreationCollisionOption.GenerateUniqueName);
            using var output = await file.OpenAsync(FileAccessMode.ReadWrite);
            var encoder = await BitmapEncoder.CreateAsync(jpeg ? BitmapEncoder.JpegEncoderId : BitmapEncoder.PngEncoderId, output);
            encoder.SetSoftwareBitmap(bitmap);
            await encoder.FlushAsync();
            return file.Path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or COMException or ArgumentException)
        {
            Log.Error(e, "shelf convert");
            return null;
        }
    }

    /// <summary>
    /// Windows' share window for the files, with Nearby Share among its targets: the counterpart of
    /// the Mac's AirDrop picker. A Win32 window gets it through IDataTransferManagerInterop.
    /// </summary>
    public static async void Share(IReadOnlyList<string> paths, nint window, string title)
    {
        if (paths.Count == 0) return;
        var items = new List<IStorageItem>();
        foreach (var path in paths)
        {
            try
            {
                items.Add(Directory.Exists(path) ? await StorageFolder.GetFolderFromPathAsync(path) : await StorageFile.GetFileFromPathAsync(path));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or COMException)
            {
                Log.Error(e, "shelf share item");
            }
        }
        if (items.Count == 0) return;
        try
        {
            var manager = ForWindow(window);
            void Requested(DataTransferManager sender, DataRequestedEventArgs args)
            {
                sender.DataRequested -= Requested;
                args.Request.Data.Properties.Title = title;
                args.Request.Data.SetStorageItems(items);
            }
            manager.DataRequested += Requested;
            ShowFor(window);
        }
        catch (Exception e) when (e is COMException or InvalidOperationException)
        {
            Log.Error(e, "share");
        }
    }

    // MARK: IDataTransferManagerInterop

    private static readonly Guid InteropId = new("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8");
    private static readonly Guid ManagerId = new("A5CAEE9B-8708-49D1-8D36-67D25A8DA00C");

    private static unsafe nint Interop()
    {
        const string name = "Windows.ApplicationModel.DataTransfer.DataTransferManager";
        Marshal.ThrowExceptionForHR(WindowsCreateString(name, name.Length, out var hstring));
        try
        {
            var iid = InteropId;
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(hstring, &iid, out var factory));
            return factory;
        }
        finally
        {
            WindowsDeleteString(hstring);
        }
    }

    private static unsafe DataTransferManager ForWindow(nint window)
    {
        var interop = Interop();
        try
        {
            var vtable = *(nint**)interop;
            var getForWindow = (delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int>)vtable[3];
            var iid = ManagerId;
            nint manager;
            Marshal.ThrowExceptionForHR(getForWindow(interop, window, &iid, &manager));
            try
            {
                return DataTransferManager.FromAbi(manager);
            }
            finally
            {
                Marshal.Release(manager);
            }
        }
        finally
        {
            Marshal.Release(interop);
        }
    }

    private static unsafe void ShowFor(nint window)
    {
        var interop = Interop();
        try
        {
            var vtable = *(nint**)interop;
            var show = (delegate* unmanaged[Stdcall]<nint, nint, int>)vtable[4];
            Marshal.ThrowExceptionForHR(show(interop, window));
        }
        finally
        {
            Marshal.Release(interop);
        }
    }

    [LibraryImport("combase.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int WindowsCreateString(string text, int length, out nint hstring);

    [LibraryImport("combase.dll")]
    private static partial int WindowsDeleteString(nint hstring);

    [LibraryImport("combase.dll")]
    private static unsafe partial int RoGetActivationFactory(nint classId, Guid* iid, out nint factory);
}
