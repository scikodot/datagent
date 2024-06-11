namespace DatagentMonitor.FileSystem;

public enum EntryType
{
    File = 0,
    Directory = 1
}

// TODO: consider switching to WatcherChangeTypes
public enum EntryAction
{
    None = 0,  // Used in place of null's
    Create = 2,
    Rename = 4,  // Rename has the highest priority
    Change = 3,
    Delete = 1,
}

public readonly record struct RenameProperties(string Name);

public readonly record struct ChangeProperties
{
    private readonly DateTime _lastWriteTime = default;
    public DateTime LastWriteTime
    {
        get => _lastWriteTime;
        init => _lastWriteTime = value.TrimMicroseconds();
    }

    private readonly long _length = default;
    public long Length
    {
        get => _length;
        init => _length = value;
    }

    public ChangeProperties(FileSystemInfo info)
    {
        switch (info)
        {
            case DirectoryInfo directory:
                LastWriteTime = directory.LastWriteTime;
                break;

            case FileInfo file:
                LastWriteTime = file.LastWriteTime;
                Length = file.Length;
                break;
        }
    }

    public static bool operator ==(ChangeProperties? a, FileSystemInfo? b) => a.HasValue ? a.Value.EqualsInfo(b) : b is null;
    public static bool operator !=(ChangeProperties? a, FileSystemInfo? b) => !(a == b);

    public static bool operator ==(FileSystemInfo? a, ChangeProperties? b) => b == a;
    public static bool operator !=(FileSystemInfo? a, ChangeProperties? b) => !(b == a);

    public static bool operator ==(ChangeProperties? a, CustomFileSystemInfo? b) => a.HasValue ? a.Value.EqualsCustomInfo(b) : b is null;
    public static bool operator !=(ChangeProperties? a, CustomFileSystemInfo? b) => !(a == b);

    public static bool operator ==(CustomFileSystemInfo? a, ChangeProperties? b) => b == a;
    public static bool operator !=(CustomFileSystemInfo? a, ChangeProperties? b) => !(b == a);

    private bool EqualsInfo(FileSystemInfo? info) => info switch
    {
        null => false,
        DirectoryInfo directory => LastWriteTime == directory?.LastWriteTime.TrimMicroseconds(),
        FileInfo file => LastWriteTime == file?.LastWriteTime.TrimMicroseconds() && Length == file?.Length
    };

    private bool EqualsCustomInfo(CustomFileSystemInfo? info) => info switch
    {
        null => false,
        CustomDirectoryInfo directory => LastWriteTime == directory?.LastWriteTime.TrimMicroseconds(),
        CustomFileInfo file => LastWriteTime == file?.LastWriteTime.TrimMicroseconds() && Length == file?.Length
    };
}

public record class EntryChange : IComparable<EntryChange>
{
    private DateTime? _timestamp;
    public DateTime? Timestamp
    {
        get => _timestamp;
        init
        {
            if (value > DateTime.Now)
                throw new ArgumentException("Cannot create a change with a future timestamp.");

            _timestamp = value;
        }
    }

    public string OldPath { get; private init; }
    public string Path => RenameProperties.HasValue ?
        System.IO.Path.Combine(
            OldPath[..(OldPath.LastIndexOf(System.IO.Path.DirectorySeparatorChar) + 1)],
            RenameProperties.Value.Name) : OldPath;

    public string OldName { get; private init; }
    public string Name => RenameProperties?.Name ?? OldName;

    public EntryType Type { get; private init; }
    public EntryAction Action { get; private init; }

    public RenameProperties? RenameProperties { get; private init; }
    public ChangeProperties? ChangeProperties { get; private init; }

    public EntryChange(
        DateTime? timestamp, string path,
        EntryType type, EntryAction action,
        RenameProperties? renameProps, ChangeProperties? changeProps)
    {
        var typeName = EnumExtensions.GetNameEx(type);
        var actionName = EnumExtensions.GetNameEx(action);

        string ExceptionMessage(string msg) => $"{typeName} {actionName}: {msg}";

        if (string.IsNullOrEmpty(path))
            throw new ArgumentException("Path was null or empty.");

        // Total: 11 bad cases
        switch (action, renameProps, changeProps)
        {
            // Only change properties must be present; 3 cases
            case (EntryAction.Create, _, null):
            case (EntryAction.Create, not null, _):
                throw new ArgumentException(ExceptionMessage("Only change properties must be present."));

            // Only rename properties must be present; 3 cases
            case (EntryAction.Rename, null, _):
            case (EntryAction.Rename, _, not null):
                throw new ArgumentException(ExceptionMessage("Only rename properties must be present."));

            // At least change properties must be present; 2 cases
            case (EntryAction.Change, _, null):
                throw new ArgumentException(ExceptionMessage("At least change properties must be present."));

            // No properties must be present; 3 cases
            case (EntryAction.Delete, not null, _):
            case (EntryAction.Delete, _, not null):
                throw new ArgumentException(ExceptionMessage("No properties must be present."));
        }

        /* Total: 5 good cases
         * (Create, null, not null)
         * (Rename, not null, null)
         * (Change, _, not null)
         * (Delete, null, null)
         */

        // Identity check
        var oldName = System.IO.Path.GetFileName(path);
        if (renameProps?.Name == oldName)
            throw new ArgumentException("Cannot create an identity rename.");

        Timestamp = timestamp;
        OldPath = path;
        OldName = oldName;
        Type = type;
        Action = action;
        RenameProperties = renameProps;
        ChangeProperties = changeProps;
    }

    public static bool operator <(EntryChange? a, EntryChange? b) => Compare(a, b) < 0;
    public static bool operator <=(EntryChange? a, EntryChange? b) => Compare(a, b) <= 0;
    public static bool operator >(EntryChange? a, EntryChange? b) => Compare(a, b) > 0;
    public static bool operator >=(EntryChange? a, EntryChange? b) => Compare(a, b) >= 0;

    public int CompareTo(EntryChange? other) => (int)Compare(this, other);

    private static long Compare(EntryChange? change1, EntryChange? change2) =>
        ((change1?.Timestamp ?? DateTime.MinValue) - (change2?.Timestamp ?? DateTime.MinValue)).Ticks;
}
