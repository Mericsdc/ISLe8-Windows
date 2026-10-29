using System.Diagnostics;
using System.Runtime.InteropServices;
using ISLe8.Core.Diagnostics;

namespace ISLe8.Notifications;

/// <summary>
/// A click on a notification banner brings its app forward (NotificationViews.swift, AppInfo.open).
/// A window of it already open comes to the front; otherwise Windows starts the app by its
/// AppUserModelID, as the Start menu would. Starting a running browser again would open a new
/// window, which is why open ones are looked for first.
/// </summary>
internal static unsafe partial class SenderApp
{
    private const int SwRestore = 9;
    private const ushort VtLpwstr = 31;
    private static readonly Guid PropertyStore = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");

    public static void Open(string appId)
    {
        if (appId.Length == 0) return;
        if (FindWindow(appId) is var window and not 0)
        {
            if (IsIconic(window)) ShowWindow(window, SwRestore);
            SetForegroundWindow(window);
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $@"shell:AppsFolder\{appId}") { UseShellExecute = false });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Error(e, "open sender");
        }
    }

    /// <summary>The first top-level window that says it belongs to <paramref name="appId"/>, as the taskbar groups them.</summary>
    private static nint FindWindow(string appId)
    {
        nint found = 0;
        var handle = GCHandle.Alloc((appId, (Action<nint>)(window => found = window)));
        try
        {
            EnumWindows(&Visit, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }
        return found;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static int Visit(nint window, nint state)
    {
        if (!IsWindowVisible(window) || GetWindow(window, 4 /* GW_OWNER */) != 0) return 1;
        var (appId, found) = ((string, Action<nint>))GCHandle.FromIntPtr(state).Target!;
        if (!string.Equals(AppIdOf(window), appId, StringComparison.OrdinalIgnoreCase)) return 1;
        found(window);
        return 0;
    }

    /// <summary>System.AppUserModel.ID from the window's property store, when it has one.</summary>
    private static string? AppIdOf(nint window)
    {
        var iid = PropertyStore;
        if (SHGetPropertyStoreForWindow(window, &iid, out var store) < 0 || store == 0) return null;
        try
        {
            var key = new PropertyKey { FormatId = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), PropertyId = 5 };
            var value = default(PropVariant);
            var getValue = (delegate* unmanaged[Stdcall]<nint, PropertyKey*, PropVariant*, int>)(*(nint**)store)[5];
            if (getValue(store, &key, &value) < 0) return null;
            try
            {
                return value.Type == VtLpwstr && value.Pointer != 0 ? new string((char*)value.Pointer) : null;
            }
            finally
            {
                PropVariantClear(&value);
            }
        }
        finally
        {
            Marshal.Release(store);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort Type;
        public ushort Reserved1;
        public ushort Reserved2;
        public ushort Reserved3;
        public nint Pointer;
        public nint Rest;
    }

    [LibraryImport("user32.dll")]
    private static partial int EnumWindows(delegate* unmanaged[Stdcall]<nint, nint, int> callback, nint state);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(nint window);

    [LibraryImport("user32.dll")]
    private static partial nint GetWindow(nint window, uint command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint window, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint window);

    [LibraryImport("shell32.dll")]
    private static partial int SHGetPropertyStoreForWindow(nint window, Guid* iid, out nint store);

    [LibraryImport("ole32.dll")]
    private static partial int PropVariantClear(PropVariant* value);
}
