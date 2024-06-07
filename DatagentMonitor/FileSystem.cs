using System.Text;
using System.Text.Json;
using DatagentMonitor.Collections;

namespace DatagentMonitor.FileSystem;

public enum EntryType
{
    File = 0,
    Directory = 1
}

// TODO: consider switching to WatcherChangeTypes
public enum EntryAction
{
    Create,
    Rename,
    Change,
    Delete,
}

public class ActionSerializer
{
    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        IgnoreReadOnlyFields = true
    };

    public static string? Serialize<T>(T? props)
    {
        if (props is null)
            return default;

        return JsonSerializer.Serialize(props, options: _options);
    }

    public static T? Deserialize<T>(string? json)
    {
        if (json is null || json == "")
            return default;

        return JsonSerializer.Deserialize<T>(json, options: _options);
    }
}

public readonly record struct RenameProperties(string Name);

public readonly record struct ChangeProperties
{
    private readonly DateTime _lastWriteTime;
    public DateTime LastWriteTime
    {
        get => _lastWriteTime;
        init => _lastWriteTime = value.TrimMicroseconds();
    }

    private readonly long _length;
    public long Length
    {
        get => _length;
        init => _length = value;
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

public class CustomFileSystemInfoCollection : GroupedLookupLinkedList<string, CustomFileSystemInfo>
{
    protected override string GetKey(CustomFileSystemInfo info) => info.Name;

    public override void Add(CustomFileSystemInfo node)
    {
        base.Add(node);

        node.NamePropertyChanged += OnRenamed;
    }

    protected override void Remove(LinkedListNode<CustomFileSystemInfo> node)
    {
        base.Remove(node);

        node.Value.NamePropertyChanged -= OnRenamed;
    }

    // Rename callback that only moves the element to a new key in the lookup; 
    // element's positions in both list and group remain the same
    private void OnRenamed(object? sender, CustomRenameEventArgs e)
    {
        if (!_lookup.Remove(e.OldName, out var node))
            throw new KeyNotFoundException(e.OldName);

        _lookup.Add(e.Name, node);
    }

    public IEnumerable<CustomDirectoryInfo> Directories
    {
        get
        {                                            
            if (!TryGetGroup(typeof(CustomDirectoryInfo), out var directories))
                yield break;

            foreach (var directory in directories.Select(e => (CustomDirectoryInfo)e))
                yield return directory;
        }
    }
    public IEnumerable<CustomFileInfo> Files
    {
        get
        {
            if (!TryGetGroup(typeof(CustomFileInfo), out var files))
                yield break;

            foreach (var file in files.Select(e => (CustomFileInfo)e))
                yield return file;
        }
    }
}

public delegate void CustomRenameEventHandler(object sender, CustomRenameEventArgs e);

public record class CustomRenameEventArgs(string OldName, string Name);

public abstract class CustomFileSystemInfo
{
    public abstract EntryType Type { get; }

    public event CustomRenameEventHandler? NamePropertyChanged;

    protected string _name;
    public string Name
    {
        get => _name;
        set
        {
            if (value != _name)
            {
                NamePropertyChanged?.Invoke(this, new CustomRenameEventArgs(_name, value));
                _name = value;
            }
        }
    }

    private DateTime _lastWriteTime;
    public DateTime LastWriteTime
    {
        get => _lastWriteTime;
        set => _lastWriteTime = value;
    }

    protected CustomFileSystemInfo(string name, DateTime lastWriteTime)
    {
        _name = name;
        _lastWriteTime = lastWriteTime;
    }

    protected CustomFileSystemInfo(FileSystemInfo info)
    {
        if (!info.Exists)
            throw info switch
            {
                DirectoryInfo => new DirectoryNotFoundException(info.FullName),
                FileInfo => new FileNotFoundException(info.FullName)
            };

        _name = info.Name;
        _lastWriteTime = info.LastWriteTime;
    }

    public static CustomFileSystemInfo Parse(string entry)
    {
        var split = entry!.Split(new char[] { ':', ',' }, StringSplitOptions.TrimEntries);
        var name = split[0];
        return split.Length switch
        {
            1 => throw new ArgumentException($"Could not parse the entry: '{entry}'"),
            2 => new CustomDirectoryInfo(name, DateTimeExtensions.Parse(split[1])),
            _ => new CustomFileInfo(name, DateTimeExtensions.Parse(split[1]), long.Parse(split[2]))
        };
    }
}

public class CustomFileInfo : CustomFileSystemInfo
{
    public override EntryType Type => EntryType.File;

    private long _length;
    public long Length
    {
        get => _length;
        set => _length = value;
    }

    public CustomFileInfo(string name, DateTime lastWriteTime, long length) : base(name, lastWriteTime)
    {
        _length = length;
    }

    public CustomFileInfo(FileInfo info) : base(info)
    {
        _length = info.Length;
    }

    public override string ToString() => $"{Name}: {LastWriteTime.Serialize()}, {Length}";
}

public class CustomDirectoryInfo : CustomFileSystemInfo
{
    public override EntryType Type => EntryType.Directory;

    public CustomFileSystemInfoCollection Entries { get; init; } = new();

    public CustomDirectoryInfo(string name, DateTime lastWriteTime) : base(name, lastWriteTime) { }

    public CustomDirectoryInfo(DirectoryInfo info, Func<FileSystemInfo, bool>? filter = null) : base(info)
    {
        var entries = info.EnumerateFileSystemInfos();
        if (filter is not null)
            entries = entries.Where(filter);

        foreach (var entry in entries)
            Entries.Add(entry switch
            {
                DirectoryInfo directory => new CustomDirectoryInfo(directory, filter),
                FileInfo file => new CustomFileInfo(file)
            });
    }

    public void Create(DateTime timestamp, string path, CustomFileSystemInfo entry)
    {
        var parents = GetParents(path);
        if (parents[^1].Entries.Remove(entry.Name, out var existing) && entry.Type == existing.Type)
            throw new ArgumentException($"Attempted to replace an existing change of the same type: {entry}");

        parents[^1].Entries.Add(entry);

        UpdateLastWriteTimes(parents, timestamp);
    }

    public void Rename(DateTime timestamp, string path, RenameProperties properties, out CustomFileSystemInfo entry)
    {
        var parents = GetParents(path);
        var name = Path.GetFileName(path);
        if (!parents[^1].Entries.TryGetValue(name, out entry))
            throw new KeyNotFoundException(name);

        entry.Name = properties.Name;

        UpdateLastWriteTimes(parents, timestamp);
    }

    public void Change(DateTime timestamp, string path, ChangeProperties properties, out CustomFileSystemInfo entry)
    {
        var parents = GetParents(path);
        var name = Path.GetFileName(path);
        if (!parents[^1].Entries.TryGetValue(name, out entry))
            throw new KeyNotFoundException(name);

        switch (entry)
        {
            case CustomDirectoryInfo directory:
                directory.LastWriteTime = properties.LastWriteTime;
                break;

            case CustomFileInfo file:
                file.LastWriteTime = properties.LastWriteTime;
                file.Length = properties.Length;
                break;
        }

        UpdateLastWriteTimes(parents, timestamp);
    }

    public void Delete(DateTime timestamp, string path, out CustomFileSystemInfo entry)
    {
        var parents = GetParents(path);
        var name = Path.GetFileName(path);
        if (!parents[^1].Entries.Remove(name, out entry))
            throw new KeyNotFoundException(name);

        UpdateLastWriteTimes(parents, timestamp);
    }

    private List<CustomDirectoryInfo> GetParents(string path)
    {
        var parents = new List<CustomDirectoryInfo> { this };
        var parent = this;
        foreach (var name in path.Split(Path.DirectorySeparatorChar).SkipLast(1))
        {
            if (!parent.Entries.TryGetValue(name, out var entry) || entry is not CustomDirectoryInfo)
                throw new ArgumentException($"Directory '{name}' not found for path '{path}'");

            parents.Add(parent = (CustomDirectoryInfo)entry);
        }

        return parents;
    }

    private static void UpdateLastWriteTimes(IEnumerable<CustomDirectoryInfo> parents, DateTime timestamp)
    {
        foreach (var parent in parents.Reverse())
        {
            if (parent.LastWriteTime < timestamp)
                parent.LastWriteTime = timestamp;
        }
    }

    public override string ToString() => $"{Name}: {LastWriteTime.Serialize()}";
}

// TODO: disallow future timestamps for LastWriteTime's of both files and directories
public class CustomDirectoryInfoSerializer
{
    public static string Serialize(CustomDirectoryInfo root)
    {
        var builder = new StringBuilder();
        var stack = new Stack<(CustomFileSystemInfo, int)>();
        stack.Push((root, -1));
        while (stack.TryPop(out var pair))
        {
            var (entry, level) = pair;

            if (level >= 0)
                builder.Append('\t', level).Append(entry.ToString()).Append('\n');

            if (entry is CustomDirectoryInfo directory)
            {
                foreach (var file in directory.Entries.Files.OrderByDescending(f => Path.GetFileNameWithoutExtension(f.Name)))
                    stack.Push((file, level + 1));

                foreach (var subdir in directory.Entries.Directories.OrderByDescending(d => d.Name))
                    stack.Push((subdir, level + 1));
            }
        }

        return builder.ToString();
    }

    public static CustomDirectoryInfo Deserialize(TextReader reader)
    {
        var root = new CustomDirectoryInfo("", DateTime.MinValue);
        var stack = new Stack<CustomDirectoryInfo>();
        stack.Push(root);
        int count = 1;
        while (reader.Peek() > 0)
        {
            var line = reader.ReadLine()!;
            int level = line.StartsWithCount('\t');
            int diff = stack.Count - (level + 1);
            if (diff < 0)
                throw new InvalidIndexFormatException(count, "Inconsistent tabulation.");

            for (int i = 0; i < diff; i++)
                stack.Pop();

            var parent = stack.Peek();
            var entry = CustomFileSystemInfo.Parse(line);
            if (entry.LastWriteTime > parent.LastWriteTime)
            {
                if (stack.Count == 1)
                    parent.LastWriteTime = entry.LastWriteTime;
                else
                    throw new InvalidIndexFormatException(count, 
                        "Timestamp should be lower than the one of the containing folder.");
            }

            parent.Entries.Add(entry);
            if (entry is CustomDirectoryInfo directory)
                stack.Push(directory);
        }

        return root;
    }
}
