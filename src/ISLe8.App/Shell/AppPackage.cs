using ISLe8.Interop;

namespace ISLe8.Shell;

/// <summary>
/// Whether ISLe8 runs from its MSIX package, with the identity the notification mirror and the
/// startup task need, or from the zip. A program another packaged app started (a terminal inside one)
/// carries that app's identity, so the family name is checked, not only that there is one.
/// </summary>
internal static unsafe class AppPackage
{
    /// <summary>The identity's name in Package.appxmanifest.</summary>
    public const string Name = "com.lrxe8.isle8";

    private const int ErrorInsufficientBuffer = 122;

    public static bool IsInstalled { get; } = FamilyName()?.StartsWith(Name + "_", StringComparison.OrdinalIgnoreCase) == true;

    private static string? FamilyName()
    {
        uint length = 0;
        if (Win32.GetCurrentPackageFamilyName(ref length, null) != ErrorInsufficientBuffer || length is 0 or > 256) return null;
        var buffer = stackalloc char[(int)length];
        return Win32.GetCurrentPackageFamilyName(ref length, buffer) == 0 ? new string(buffer, 0, (int)length - 1) : null;
    }
}
