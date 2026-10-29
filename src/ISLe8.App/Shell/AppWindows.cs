using System.Runtime.InteropServices;

namespace ISLe8.Shell;

/// <summary>
/// Brings another app's window forward: "Oturuma git" brings the terminal or app an agent runs in
/// (AgentViews.swift, NSRunningApplication.activate). It is called as the island takes the click, so
/// Windows lets ISLe8 hand the foreground on.
/// </summary>
internal static unsafe partial class AppWindows
{
    private const int SwRestore = 9;

    /// <summary>The first visible, titled top-level window of <paramref name="processId"/>; false if it has none.</summary>
    public static bool Activate(int processId)
    {
        var box = new Box(processId);
        var handle = GCHandle.Alloc(box);
        try
        {
            EnumWindows(&Visit, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }
        if (box.Window == 0) return false;
        Activate(box.Window);
        return true;
    }

    public static void Activate(nint window)
    {
        if (IsIconic(window)) ShowWindow(window, SwRestore);
        SetForegroundWindow(window);
    }

    private sealed class Box(int processId)
    {
        public int ProcessId { get; } = processId;
        public nint Window { get; set; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static int Visit(nint window, nint state)
    {
        var box = (Box)GCHandle.FromIntPtr(state).Target!;
        if (!IsWindowVisible(window) || GetWindow(window, 4 /* GW_OWNER */) != 0 || GetWindowTextLengthW(window) == 0) return 1;
        if (GetWindowThreadProcessId(window, out var pid) == 0 || pid != box.ProcessId) return 1;
        box.Window = window;
        return 0;
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
    private static partial int GetWindowTextLengthW(nint window);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint window, out uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint window, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint window);
}
