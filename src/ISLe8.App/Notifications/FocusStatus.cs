using ISLe8.Interop;

namespace ISLe8.Notifications;

/// <summary>
/// Whether Do not disturb is on, or a focus session that turned it on (the Mac: a Focus mode,
/// FocusStatus in NotificationDatabase.swift). Windows offers other apps no API for it; the shell
/// publishes the quiet-hours profile in force, which is 0 while notifications are unrestricted.
/// </summary>
internal static unsafe class FocusStatus
{
    /// <summary>WNF_SHEL_QUIETHOURS_ACTIVE_PROFILE_CHANGED.</summary>
    private const ulong QuietHoursProfile = 0x0D83063EA3BF1C75;

    public static bool IsActive()
    {
        var name = QuietHoursProfile;
        var profile = 0;
        var size = (uint)sizeof(int);
        try
        {
            return Win32.NtQueryWnfStateData(ref name, 0, 0, out _, &profile, ref size) == 0 && size >= sizeof(int) && profile != 0;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }
}
