﻿using System.Text;
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

    public FileSystemEntryType Type { get; private init; }
    public FileSystemEntryAction Action { get; private init; }

    public RenameProperties? RenameProperties { get; private init; }
    public ChangeProperties? ChangeProperties { get; private init; }

    public EntryChange(
        DateTime? timestamp, string path, 
        FileSystemEntryType type, FileSystemEntryAction action, 
        RenameProperties? renameProps, ChangeProperties? changeProps)
    {
        var typeName = EnumExtensions.GetNameEx(type);
        var actionName = EnumExtensions.GetNameEx(action);

        string ExceptionMessage(string msg) => $"{actionName} {typeName}: {msg}";

        if (string.IsNullOrEmpty(path))
            throw new ArgumentException("Path was null or empty.");

        // Total: 24 bad cases
        switch (type, action, renameProps, changeProps)
        {
            // Directory change is not allowed; 4 cases
            case (FileSystemEntryType.Directory, FileSystemEntryAction.Change, _, _):
                throw new ArgumentException(ExceptionMessage("Not allowed for a directory."));

            // No properties must be present; 9 cases
            case (_, FileSystemEntryAction.Delete, not null, _):
            case (_, FileSystemEntryAction.Delete, _, not null):
            case (FileSystemEntryType.Directory, FileSystemEntryAction.Create, not null, _):
            case (FileSystemEntryType.Directory, FileSystemEntryAction.Create, _, not null):
                throw new ArgumentException(ExceptionMessage("No properties must be present."));

            // Only rename properties must be present; 6 cases
            case (_, FileSystemEntryAction.Rename, null, _):
            case (_, FileSystemEntryAction.Rename, _, not null):
                throw new ArgumentException(ExceptionMessage("Only rename properties must be present."));

            // Only change properties must be present; 3 cases
            case (FileSystemEntryType.File, FileSystemEntryAction.Create, not null, _):
            case (FileSystemEntryType.File, FileSystemEntryAction.Create, _, null):
                throw new ArgumentException(ExceptionMessage("Only change properties must be present."));

            // At least change properties must be present; 2 cases
            case (FileSystemEntryType.File, FileSystemEntryAction.Change, _, null):
                throw new ArgumentException(ExceptionMessage("At least change properties must be present."));
        }

        /* Total: 8 good cases
         * (Directory or File, Rename, not null, null)
         * (Directory or File, Delete, null, null)
         * (Directory, Create, null, null)
         * (File, Create, null, not null)
         * (File, Change, null, not null)
         * (File, Change, not null, not null)
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
                LastWriteTime = DateTimeExtensions.Parse(split[1]),
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

    public override string ToString() => $"{Name}: {LastWriteTime.Serialize()}, {Length}";
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
