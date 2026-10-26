using System.Runtime.CompilerServices;
using ISLe8.Interop;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace ISLe8.Shell;

/// <summary>
/// Lets the desktop show through every pixel the island does not draw. WinUI paints a window's
/// background through its system backdrop; this one is a fully transparent brush. Together with
/// DWM blur-behind on an empty region (see <see cref="IslandWindow"/>) the window gets per-pixel alpha.
/// </summary>
internal sealed partial class TransparentBackdrop : SystemBackdrop
{
    private static Windows.UI.Composition.Compositor? s_compositor;
    private static nint s_dispatcherController;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        EnsureSystemDispatcherQueue();
        s_compositor ??= new Windows.UI.Composition.Compositor();
        connectedTarget.SystemBackdrop = s_compositor.CreateColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);
        disconnectedTarget.SystemBackdrop = null;
    }

    /// <summary>
    /// The system compositor (Windows.UI.Composition) needs a Windows.System dispatcher queue on
    /// this thread; WinUI only creates the Microsoft.UI one.
    /// </summary>
    private static void EnsureSystemDispatcherQueue()
    {
        if (Windows.System.DispatcherQueue.GetForCurrentThread() is not null || s_dispatcherController != 0) return;
        var options = new DispatcherQueueOptions
        {
            dwSize = Unsafe.SizeOf<DispatcherQueueOptions>(),
            threadType = 2, // DQTYPE_THREAD_CURRENT
            apartmentType = 2, // DQTAT_COM_STA
        };
        Win32.CreateDispatcherQueueController(options, out s_dispatcherController);
    }
}
