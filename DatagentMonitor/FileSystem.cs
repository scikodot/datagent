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

// TODO: handle Directory + Change combination here instead of many other places
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

public abstract class CustomFileSystemInfo
{
    public static readonly string DateTimeFormat = "yyyyMMddHHmmssfff";

    public string Name { get; set; }
    public abstract FileSystemEntryType Type { get; }

    public CustomFileSystemInfo(string name)
    {
        Name = name;
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

    // TODO: consider adding a LookupLinkedList method
    // that would handle lookup property change on its own;
    // currently removing and adding an object moves it to the end of the list, 
    // while it is better to preserve the initial order
    public void Rename(string path, RenameProperties properties, out CustomFileSystemInfo entry)
    {
        var parent = GetParent(path);
        var name = Path.GetFileName(path);
        if (!parent.Entries.Remove(name, out entry))
            throw new KeyNotFoundException(name);

        entry.Name = properties.Name;
        parent.Entries.Add(entry);
    }

    public void Change(string path, ChangeProperties properties, out CustomFileInfo file)
    {
        var parent = GetParent(path);
        var name = Path.GetFileName(path);
        if (!parent.Entries.TryGetValue(name, out var entry))
            throw new KeyNotFoundException(name);

        if (entry is CustomDirectoryInfo)
            throw new DirectoryChangeActionNotAllowedException();

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
    // TODO: consider returning string; StringBuilder is mutable
    public static StringBuilder Serialize(CustomDirectoryInfo root)
    {
        var builder = new StringBuilder();
        Serialize(root, builder, depth: 0);
        return builder;
    }

    private static void Serialize(CustomDirectoryInfo root, StringBuilder builder, int depth)
    {
        foreach (var directory in root.Entries.Directories.OrderBy(d => d.Name))
        {
            builder.Append('\t', depth).Append(directory.ToString()).Append('\n');
            Serialize(directory, builder, depth + 1);
        }

        foreach (var file in root.Entries.Files.OrderBy(f => Path.GetFileNameWithoutExtension(f.Name)))
        {
            builder.Append('\t', depth).Append(file.ToString()).Append('\n');
        }
    }

    public static CustomDirectoryInfo Deserialize(TextReader reader)
    {
        var rootInfo = new CustomDirectoryInfo("");
        var stack = new Stack<CustomDirectoryInfo>();
        stack.Push(rootInfo);
        while (reader.Peek() > 0)
        {
            var entry = reader.ReadLine();
            int level = entry!.StartsWithCount('\t');
            int diff = stack.Count - (level + 1);
            if (diff < 0)
                throw new InvalidIndexFormatException();

            for (int i = 0; i < diff; i++)
                stack.Pop();

            var parent = stack.Peek();
            var split = entry!.Split(new char[] { ':', ',' }, StringSplitOptions.TrimEntries);
            var name = split[0];
            if (split.Length > 1)
            {
                // File
                var file = new CustomFileInfo(name)
                {
                    LastWriteTime = DateTime.ParseExact(split[1], CustomFileSystemInfo.DateTimeFormat, null),
                    Length = long.Parse(split[2]),
                };
                parent.Entries.Add(file);
            }
            else
            {
                // Directory
                var directory = new CustomDirectoryInfo(name);
                parent.Entries.Add(directory);
                stack.Push(directory);
            }
        }

        return rootInfo;
    }
}
