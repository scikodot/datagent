using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;

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

public readonly record struct ChangeProperties(DateTime LastWriteTime, long Length);

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
}

// TODO: consider adding HashSet<string> (or even Dictionary<string, ...>):
// 1. It would guarantee the uniqueness of all keys of both dictionaries (files + directories)
// 2. It would serve as a common interface for accessing both dictionaries, 
//    so that, instead of checking both dictionaries for a key, only one could be checked
// 
// Better yet, LookupLinkedList can be enhanced with clusters.
// For example, a 2-clustered linked list can be split split into two parts:
// (d_1 -> ... -> d_m) -> (f_1 -> ... f_n)
// 
// Then we can store only 1 node (d_m) that is the last node of the first cluster;
// in a general case, k-1 nodes for each of k-1 first clusters of a k-clustered list.
// 
// Meanwhile, the lookup dict itself stays the same and encompasses all the entries, 
// say, both files and directories, but they reside in their respective clusters.
// This consequently guarantees that all the names are unique throughout the whole list and across the clusters.
public class CustomDirectoryInfo : CustomFileSystemInfo
{
    public override FileSystemEntryType Type => FileSystemEntryType.Directory;

    public LookupLinkedList<string, CustomDirectoryInfo> Directories { get; init; } = new(d => d.Name);
    public LookupLinkedList<string, CustomFileInfo> Files { get; init; } = new(f => f.Name);

    public CustomDirectoryInfo(string name) : base(name) { }

    public CustomDirectoryInfo(DirectoryInfo info, Func<FileSystemInfo, bool>? filter = null) : 
        base(Path.GetFileName(info.FullName))
    {
        if (!info.Exists)
            throw new DirectoryNotFoundException(info.FullName);

        var directories = info.EnumerateDirectories();
        var files = info.EnumerateFiles();
        if (filter is not null)
        {
            // TODO: implementing a clustered collection described above 
            // would also remove file/directory specific handling and Select's, 
            // because all the entries would be added as FileSystemInfo's
            directories = directories.Where(filter).Select(d => (DirectoryInfo)d);
            files = files.Where(filter).Select(f => (FileInfo)f);
        }
        foreach (var directory in directories)
            Directories.Add(new CustomDirectoryInfo(directory, filter));

        foreach (var file in info.EnumerateFiles())
            Files.Add(new CustomFileInfo(file));
    }

    public void Create(string path, CustomFileSystemInfo entry)
    {
        var parent = GetParent(path);
        switch (entry)
        {
            case CustomDirectoryInfo directory:
                parent.Directories.Add(directory);
                break;

            case CustomFileInfo file:
                parent.Files.Add(file);
                break;
        }
    }

    // TODO: consider adding a LookupLinkedList method
    // that would handle lookup property change on its own;
    // currently removing and adding an object moves it to the end of the list, 
    // while it is better to preserve the initial order
    public void Rename(string path, RenameProperties properties, out CustomFileSystemInfo entry)
    {
        var parent = GetParent(path);
        var name = Path.GetFileName(path);
        if (parent.Directories.Remove(name, out var directory))
        {
            directory.Name = properties.Name;
            parent.Directories.Add(directory);
            entry = directory;
        }
        else if (parent.Files.Remove(name, out var file))
        {
            file.Name = properties.Name;
            parent.Files.Add(file);
            entry = file;
        }
        else
        {
            throw new KeyNotFoundException(name);
        }
    }

    public void Change(string path, ChangeProperties properties, out CustomFileInfo entry)
    {
        var parent = GetParent(path);
        var name = Path.GetFileName(path);
        if (parent.Directories.TryGetValue(name, out _))
        {
            throw new DirectoryChangeActionNotAllowedException();
        }
        else if (parent.Files.TryGetValue(name, out var file))
        {
            file.LastWriteTime = properties.LastWriteTime;
            file.Length = properties.Length;
            entry = file;
        }
        else
        {
            throw new KeyNotFoundException(name);
        }
    }

    public void Delete(string path, out CustomFileSystemInfo entry)
    {
        var parent = GetParent(path);
        var name = Path.GetFileName(path);
        if (parent.Directories.Remove(name, out var directory))
        {
            entry = directory;
        }
        else if (parent.Files.Remove(name, out var file))
        {
            entry = file;
        }
        else
        {
            throw new KeyNotFoundException(name);
        }
    }

    private CustomDirectoryInfo GetParent(string path)
    {
        var parent = this;
        foreach (var name in path.Split(Path.DirectorySeparatorChar).SkipLast(1))
        {
            if (!parent.Directories.TryGetValue(name, out parent))
                throw new ArgumentException($"Directory '{name}' not found for path '{path}'");
        }

        return parent;
    }
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
        foreach (var directory in root.Directories.OrderBy(d => d.Name))
        {
            builder.Append('\t', depth).Append(directory.Name).Append('\n');
            Serialize(directory, builder, depth + 1);
        }

        foreach (var file in root.Files.OrderBy(f => Path.GetFileNameWithoutExtension(f.Name)))
        {
            builder.Append('\t', depth).Append($"{file.Name}: {file.LastWriteTime.ToString(CustomFileInfo.DateTimeFormat)}, {file.Length}").Append('\n');
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
                    LastWriteTime = DateTime.ParseExact(split[1], CustomFileInfo.DateTimeFormat, null),
                    Length = long.Parse(split[2]),
                };
                parent.Files.Add(file);
            }
            else
            {
                // Directory
                var directory = new CustomDirectoryInfo(name);
                parent.Directories.Add(directory);
                stack.Push(directory);
            }
        }

        return rootInfo;
    }
}
