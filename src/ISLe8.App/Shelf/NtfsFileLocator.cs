using System.Runtime.InteropServices;
using ISLe8.Core.Shelf;

namespace ISLe8.ShelfTab;

/// <summary>
/// Finds a file again after it was renamed or moved on the same drive, by its NTFS file ID: the
/// Windows counterpart of the Mac's bookmarks. The identity is "C:\|&lt;128-bit id&gt;".
/// </summary>
internal sealed unsafe partial class NtfsFileLocator : IFileLocator
{
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    private const uint FILE_SHARE_ALL = 0x7;
    private const uint OPEN_EXISTING = 3;
    private const int FileIdInfo = 18;
    private static readonly nint Invalid = -1;

    public string? IdOf(string path)
    {
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root)) return null;
        var handle = CreateFile(path, 0, FILE_SHARE_ALL, 0, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, 0);
        if (handle == Invalid) return null;
        try
        {
            FileIdInformation info;
            if (!GetFileInformationByHandleEx(handle, FileIdInfo, &info, (uint)sizeof(FileIdInformation))) return null;
            return root + "|" + Convert.ToHexString(new ReadOnlySpan<byte>(info.FileId, 16));
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    public string? PathOf(string id)
    {
        var bar = id.IndexOf('|');
        if (bar <= 0) return null;
        byte[] fileId;
        try
        {
            fileId = Convert.FromHexString(id[(bar + 1)..]);
        }
        catch (FormatException)
        {
            return null;
        }
        if (fileId.Length != 16) return null;

        // Any handle on the drive tells OpenFileById which volume to look on.
        var volume = CreateFile(id[..bar], 0, FILE_SHARE_ALL, 0, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, 0);
        if (volume == Invalid) return null;
        try
        {
            var descriptor = new FileIdDescriptor { Size = (uint)sizeof(FileIdDescriptor), Type = 2 };
            fileId.CopyTo(new Span<byte>(descriptor.FileId, 16));
            var file = OpenFileById(volume, &descriptor, 0, FILE_SHARE_ALL, 0, FILE_FLAG_BACKUP_SEMANTICS);
            if (file == Invalid) return null;
            try
            {
                var buffer = stackalloc char[1024];
                var length = GetFinalPathNameByHandle(file, buffer, 1024, 0);
                if (length == 0 || length >= 1024) return null;
                var path = new string(buffer, 0, (int)length);
                // "\\?\C:\…" → "C:\…".
                return path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..] : path;
            }
            finally
            {
                CloseHandle(file);
            }
        }
        finally
        {
            CloseHandle(volume);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileIdInformation
    {
        public ulong VolumeSerialNumber;
        public fixed byte FileId[16];
    }

    /// <summary>FILE_ID_DESCRIPTOR with ExtendedFileIdType: size, type, then the 16-byte id.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct FileIdDescriptor
    {
        public uint Size;
        public int Type;
        public fixed byte FileId[16];
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateFile(string path, uint access, uint share, nint security, uint creation, uint flags, nint template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandleEx(nint file, int infoClass, void* info, uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint OpenFileById(nint volumeHint, FileIdDescriptor* id, uint access, uint share, nint security, uint flags);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true)]
    private static partial uint GetFinalPathNameByHandle(nint file, char* path, uint length, uint flags);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);
}
