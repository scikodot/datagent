using System.Text;
using System.Text.Json;
using DatagentMonitor.Collections;

namespace DatagentMonitor.FileSystem;

public enum FileSystemEntryType
{
    File = 0,
    Directory = 1
}

// TODO: consider switching to WatcherChangeTypes
public enum FileSystemEntryAction
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

        return JsonSerializer.Serialize<T>(props, options: _options);
    }

    public static T? Deserialize<T>(string? json)
    {
        if (json is null || json == "")
            return default;

        return JsonSerializer.Deserialize<T>(json, options: _options);
    }
}

public readonly record struct RenameProperties(string Name);

public readonly record struct ChangeProperties(DateTime LastWriteTime, long Length)
{
    public static bool operator ==(ChangeProperties? a, CustomFileInfo? b) => a.HasValue ? a.Value.Equals(b) : b is null;
    public static bool operator !=(ChangeProperties? a, CustomFileInfo? b) => !(a == b);

    public static bool operator ==(CustomFileInfo? a, ChangeProperties? b) => b == a;
    public static bool operator !=(CustomFileInfo? a, ChangeProperties? b) => !(b == a);

    private bool Equals(CustomFileInfo? info) => LastWriteTime == info?.LastWriteTime && Length == info.Length;
}

public record class EntryChange : IComparable<EntryChange>
{
    public string OldPath { get; init; }
    public string Path => RenameProperties.HasValue ? 
        System.IO.Path.Combine(
            OldPath[..(OldPath.LastIndexOf(System.IO.Path.DirectorySeparatorChar) + 1)], 
            RenameProperties.Value.Name) : OldPath;

    public string OldName { get; init; }
    public string Name => RenameProperties?.Name ?? OldName;

    public FileSystemEntryType Type { get; init; }
    public FileSystemEntryAction Action { get; init; }

    public DateTime Timestamp { get; init; } = DateTime.MinValue;
    public RenameProperties? RenameProperties { get; init; }
    public ChangeProperties? ChangeProperties { get; init; }

    public EntryChange(string path, FileSystemEntryType type, FileSystemEntryAction action)
    {
        if (type is FileSystemEntryType.Directory && action is FileSystemEntryAction.Change)
            throw new DirectoryChangeActionNotAllowedException();

        OldPath = path;
        OldName = System.IO.Path.GetFileName(OldPath);
        Type = type;
        Action = action;
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
    public static readonly string DateTimeFormat = "yyyyMMddHHmmssfff";

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

    public abstract FileSystemEntryType Type { get; }

    public CustomFileSystemInfo(string name)
    {
        _name = name;
    }

    public static CustomFileSystemInfo Parse(string entry)
    {
        var split = entry!.Split(new char[] { ':', ',' }, StringSplitOptions.TrimEntries);
        var name = split[0];
        return split.Length switch
        {
            1 => new CustomDirectoryInfo(name),
            _ => new CustomFileInfo(name)
            {
                LastWriteTime = DateTime.ParseExact(split[1], DateTimeFormat, null),
                Length = long.Parse(split[2]),
            }
        };
    }
}

public class CustomFileInfo : CustomFileSystemInfo
{
    public override FileSystemEntryType Type => FileSystemEntryType.File;
    public DateTime LastWriteTime { get; set; }
    public long Length { get; set; }

    public CustomFileInfo(string name) : base(name) { }

    public CustomFileInfo(FileInfo info) : base(Path.GetFileName(info.FullName))
    {
        if (!info.Exists)
            throw new FileNotFoundException(info.FullName);

        LastWriteTime = info.LastWriteTime;
        Length = info.Length;
    }

    public override string ToString() => $"{Name}: {LastWriteTime.ToString(DateTimeFormat)}, {Length}";
}

public class CustomDirectoryInfo : CustomFileSystemInfo
{
    public override FileSystemEntryType Type => FileSystemEntryType.Directory;

    public CustomFileSystemInfoCollection Entries { get; init; } = new();

    public CustomDirectoryInfo(string name) : base(name) { }

    public CustomDirectoryInfo(DirectoryInfo info, Func<FileSystemInfo, bool>? filter = null) : 
        base(Path.GetFileName(info.FullName))
    {
        if (!info.Exists)
            throw new DirectoryNotFoundException(info.FullName);

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

    public void Create(string path, CustomFileSystemInfo entry)
    {
        var parent = GetParent(path);
        parent.Entries.Add(entry);
    }

    public void Rename(string path, RenameProperties properties, out CustomFileSystemInfo entry)
    {
        var parent = GetParent(path);
        var name = Path.GetFileName(path);
        if (!parent.Entries.TryGetValue(name, out entry))
            throw new KeyNotFoundException(name);

        entry.Name = properties.Name;
    }

    public void Change(string path, ChangeProperties properties, out CustomFileInfo file)
    {
        var parent = GetParent(path);
        var name = Path.GetFileName(path);
        if (!parent.Entries.TryGetValue(name, out var entry))
            throw new KeyNotFoundException(name);

        file = (CustomFileInfo)entry;
        file.LastWriteTime = properties.LastWriteTime;
        file.Length = properties.Length;
    }

    public void Delete(string path, out CustomFileSystemInfo entry)
    {
        var parent = GetParent(path);
        var name = Path.GetFileName(path);
        if (!parent.Entries.Remove(name, out entry))
            throw new KeyNotFoundException(name);
    }

    private CustomDirectoryInfo GetParent(string path)
    {
        var parent = this;
        foreach (var name in path.Split(Path.DirectorySeparatorChar).SkipLast(1))
        {
            if (!parent.Entries.TryGetValue(name, out var entry) || entry is not CustomDirectoryInfo)
                throw new ArgumentException($"Directory '{name}' not found for path '{path}'");

            parent = (CustomDirectoryInfo)entry;
        }

        return parent;
    }

    public override string ToString() => Name;
}

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
        var root = new CustomDirectoryInfo("");
        var stack = new Stack<CustomDirectoryInfo>();
        stack.Push(root);
        while (reader.Peek() > 0)
        {
            var line = reader.ReadLine()!;
            int level = line.StartsWithCount('\t');
            int diff = stack.Count - (level + 1);
            if (diff < 0)
                throw new InvalidIndexFormatException();

            for (int i = 0; i < diff; i++)
                stack.Pop();

            var parent = stack.Peek();
            var entry = CustomFileSystemInfo.Parse(line);
            parent.Entries.Add(entry);
            if (entry is CustomDirectoryInfo directory)
                stack.Push(directory);
        }

        return root;
    }
}
