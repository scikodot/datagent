using DatagentShared;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DatagentMonitor.FileSystem;

// TODO: consider removing
public class FileSystemEntryChangeProperties
{
    public RenameProperties? RenameProps { get; set; }
    public ChangeProperties? ChangeProps { get; set; }
}

public abstract class ActionProperties
{
    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        IgnoreReadOnlyFields = true
    };

    public static string Serialize<T>(T props) where T: ActionProperties
    {
        // GetType() guarantees the serializer will use the actual type of the object
        var res = JsonSerializer.Serialize(props, props.GetType(), options: _options);
        return res;
    }

    public static T? Deserialize<T>(string? json) where T: ActionProperties
    {
        if (json == null)
            return null;

        var res = JsonSerializer.Deserialize<T>(json, options: _options);
        return res ?? throw new JsonException($"Could not deserialize JSON to type {typeof(T)}: {json}");
    }
}

public class RenameProperties : ActionProperties
{
    public string Name { get; set; }
}

public class ChangeProperties : ActionProperties
{
    public DateTime LastWriteTime { get; init; }
    public long Length { get; init; }
}

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

// TODO: consider switching to the "record" type,
// so as to enable immutability and the "with" keyword usage
public class FileSystemEntryChange : IComparable<FileSystemEntryChange>
{
    public string Path { get; set; }
    public FileSystemEntryAction Action { get; set; }

    private DateTime? _timestamp = null;
    public DateTime? Timestamp
    {
        get => _timestamp ?? DateTime.MinValue;
        set => _timestamp = value;
    }
    public FileSystemEntryChangeProperties Properties { get; set; } = new();

    public string OldName => CustomFileSystemInfo.GetEntryName(Path);

    public override string ToString()
    {
        var timestamp = Timestamp?.ToString(CustomFileInfo.DateTimeFormat) ?? "--";
        var action = FileSystemEntryActionExtensions.ActionToString(Action);
        return $"{timestamp} {Path} {action}";
    }

    public static bool operator <(FileSystemEntryChange? a, FileSystemEntryChange? b) => Compare(a, b) < 0;
    public static bool operator <=(FileSystemEntryChange? a, FileSystemEntryChange? b) => Compare(a, b) <= 0;
    public static bool operator >(FileSystemEntryChange? a, FileSystemEntryChange? b) => Compare(a, b) > 0;
    public static bool operator >=(FileSystemEntryChange? a, FileSystemEntryChange? b) => Compare(a, b) >= 0;

    public int CompareTo(FileSystemEntryChange? other) => (int)Compare(this, other);

    private static long Compare(FileSystemEntryChange? change1, FileSystemEntryChange? change2) =>
        ((change1?.Timestamp ?? DateTime.MinValue) - (change2?.Timestamp ?? DateTime.MinValue)).Ticks;
}

public class CustomFileSystemInfo
{
    public string Name { get; set; }

    // TODO: move somewhere else
    public static FileSystemEntryType GetEntryType(string path) => 
        Path.EndsInDirectorySeparator(path) ? 
        FileSystemEntryType.Directory : 
        FileSystemEntryType.File;

    // TODO: move somewhere else
    public static Range GetEntryNameRange(string path)
    {
        int end = path.Length - (int)GetEntryType(path);
        int start = path.LastIndexOf(Path.DirectorySeparatorChar, end - 1, end) + 1;
        return new Range(start, end);
    }

    // TODO: this could be replaced with
    // Path.GetFileName(entry.AsSpan(0, entry.Length - (IsDirectory(entry) ? 1 : 0))
    public static string GetEntryName(string path) => path[GetEntryNameRange(path)];

    // Replace an entry name in the given path:
    // path/to/a/file -> path/to/a/renamed-file
    // path/to/a/directory/ -> path/to/a/renamed-directory/
    public static string ReplaceEntryName(string path, string name)
    {
        var range = GetEntryNameRange(path);
        return path[..range.Start] + name + path[range.End..];
    }
}

public class CustomFileInfo : CustomFileSystemInfo
{
    public static readonly string DateTimeFormat = "yyyyMMddHHmmssfff";

    public DateTime LastWriteTime { get; set; }
    public long Length { get; set; }

    public CustomFileInfo() { }

    public CustomFileInfo(string path) : this(new FileInfo(path)) { }

    public CustomFileInfo(FileInfo info)
    {
        if (!info.Exists)
            throw new FileNotFoundException();

        Name = info.Name;
        LastWriteTime = info.LastWriteTime;
        Length = info.Length;
    }
}

// TODO: consider adding HashSet<string> (or even Dictionary<string, ...>):
// 1. It would guarantee the uniqueness of all keys of both dictionaries (files + directories)
// 2. It would serve as a common interface for accessing both dictionaries, 
//    so that, instead of checking both dictionaries for a key, only one could be checked
public class CustomDirectoryInfo : CustomFileSystemInfo
{
    public LookupLinkedList<string, CustomDirectoryInfo> Directories { get; set; } = new(d => d.Name);
    public LookupLinkedList<string, CustomFileInfo> Files { get; set; } = new(f => f.Name);

    public CustomDirectoryInfo() { }

    public CustomDirectoryInfo(string path, Func<DirectoryInfo, bool>? directoryFilter = null) : 
        this(new DirectoryInfo(path), directoryFilter) { }

    public CustomDirectoryInfo(DirectoryInfo info, Func<DirectoryInfo, bool>? directoryFilter = null)
    {
        if (!info.Exists)
            throw new DirectoryNotFoundException();

        Name = info.Name;

        var directories = info.EnumerateDirectories();
        if (directoryFilter != null)
            directories = directories.Where(directoryFilter);
        foreach (var directory in directories)
        {
            Directories.Add(new CustomDirectoryInfo(directory, directoryFilter));
        }

        foreach (var file in info.EnumerateFiles())
        {
            Files.Add(new CustomFileInfo(file));
        }
    }

    public void Add(CustomFileSystemInfo info)
    {
        switch (info)
        {
            case CustomDirectoryInfo directory:
                Directories.Add(directory);
                break;
            case CustomFileInfo file:
                Files.Add(file);
                break;
            default:
                throw new ArgumentException($"Unknown derived type: {info.GetType()}");
        }
    }

    public bool Remove(string key) => Directories.Remove(key) || Files.Remove(key);

    public bool Remove(string key, [MaybeNullWhen(false)] out CustomFileSystemInfo info)
    {
        if (Directories.Remove(key, out var directory))
        {
            info = directory;
            return true;
        }

        if (Files.Remove(key, out var file))
        {
            info = file;
            return true;
        }

        info = default;
        return false;
    }

    public CustomDirectoryInfo GetDirectory(string subpath)
    {
        var names = Path.TrimEndingDirectorySeparator(subpath).Split(Path.DirectorySeparatorChar);
        var parent = this;
        foreach (var name in names)
            parent = parent.Directories[name];

        return parent;
    }

    public CustomDirectoryInfo GetParent(string subpath)
    {
        var names = Path.TrimEndingDirectorySeparator(subpath).Split(Path.DirectorySeparatorChar);
        var parent = this;
        foreach (var name in names[..^1])
            parent = parent.Directories[name];

        return parent;
    }

    public List<string> GetListing()
    {
        var builder = new StringBuilder();
        var result = new List<string>();
        GetListing(this, builder, result);
        return result;
    }

    private void GetListing(CustomDirectoryInfo root, StringBuilder builder, List<string> result)
    {
        foreach (var directory in builder.Wrap(root.Directories, d => d.Name + Path.DirectorySeparatorChar))
        {
            result.Add(builder.ToString());
            GetListing(directory, builder, result);
        }

        foreach (var file in builder.Wrap(root.Files, f => f.Name))
        {
            result.Add(builder.ToString());
        }
    }

    public void MergeChanges(List<FileSystemEntryChange> changes)
    {
        foreach (var change in changes)
        {
            var parent = GetParent(change.Path);
            var name = GetEntryName(change.Path);
            var properties = change.Properties;
            CustomFileSystemInfo entry;
            switch (change.Action)
            {
                case FileSystemEntryAction.Create:
                    entry = GetEntryType(change.Path) switch
                    {
                        FileSystemEntryType.File => new CustomFileInfo
                        {
                            Name = properties.RenameProps?.Name ?? name,
                            LastWriteTime = properties.ChangeProps!.LastWriteTime,
                            Length = properties.ChangeProps.Length
                        }, 
                        FileSystemEntryType.Directory => new CustomDirectoryInfo
                        {
                            Name = properties.RenameProps?.Name ?? name
                        }
                    };
                    parent.Add(entry);
                    break;
                case FileSystemEntryAction.Rename:
                    // TODO: consider adding a LookupLinkedList method
                    // that would handle lookup property change on its own;
                    // currently removing and adding an object moves it to the end of the list, 
                    // while it is better to preserve the initial order
                    parent.Remove(name, out entry);
                    entry.Name = properties.RenameProps.Name;
                    parent.Add(entry);
                    break;
                case FileSystemEntryAction.Change:
                    if (GetEntryType(change.Path) is FileSystemEntryType.Directory)
                        throw new DirectoryChangeActionNotAllowedException();

                    var file = parent.Files[name];
                    file.LastWriteTime = properties.ChangeProps!.LastWriteTime;
                    file.Length = properties.ChangeProps.Length;
                    break;
                case FileSystemEntryAction.Delete:
                    parent.Remove(name);
                    break;
            }
        }
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
        var rootInfo = new CustomDirectoryInfo();
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
                var file = new CustomFileInfo
                {
                    Name = name,
                    LastWriteTime = DateTime.ParseExact(split[1], CustomFileInfo.DateTimeFormat, null),
                    Length = long.Parse(split[2]),
                };
                parent.Files.Add(file);
            }
            else
            {
                // Directory
                var directory = new CustomDirectoryInfo
                {
                    Name = name
                };
                parent.Directories.Add(directory);
                stack.Push(directory);
            }
        }

        return rootInfo;
    }
}
