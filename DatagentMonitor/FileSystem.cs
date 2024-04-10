using DatagentShared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DatagentMonitor.FileSystem;

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

public enum FileSystemEntryAction
{
    Create,
    Rename,
    Change,
    Delete,
}

// TODO: consider adding Path property to correspond the change object to *what* exactly is changed;
// it is anyway used as a dict key, thus no additional allocs will take place
public class FileSystemEntryChange
{
    public DateTime? Timestamp { get; set; } = null;
    public FileSystemEntryAction Action { get; set; }
    public FileSystemEntryChangeProperties Properties { get; set; } = new();

    public override string ToString()
    {
        var timestampString = Timestamp != null ? Timestamp.Value.ToString(CustomFileInfo.DateTimeFormat) : "--";
        return $"{timestampString} {FileSystemEntryActionExtensions.ActionToString(Action)}:";
    }
}

public class CustomFileInfo
{
    public static readonly string DateTimeFormat = "yyyyMMddHHmmssfff";

    public string Name { get; set; }
    public DateTime LastWriteTime { get; set; }
    public long Length { get; set; }
}

public class CustomDirectoryInfo
{
    public string Name { get; set; }
    public LookupLinkedList<string, CustomDirectoryInfo> Directories { get; set; } = new(d => d.Name);
    public LookupLinkedList<string, CustomFileInfo> Files { get; set; } = new(f => f.Name);

    public CustomDirectoryInfo() { }

    public CustomDirectoryInfo(string path) : this(new DirectoryInfo(path)) { }

    public CustomDirectoryInfo(DirectoryInfo info)
    {
        // TODO: skip service files folder?

        if (!info.Exists)
            throw new DirectoryNotFoundException();

        Name = info.Name;
        foreach (var directory in info.EnumerateDirectories())
        {
            Directories.Add(new CustomDirectoryInfo(directory));
        }

        foreach (var file in info.EnumerateFiles())
        {
            Files.Add(new CustomFileInfo
            {
                Name = file.Name,
                LastWriteTime = file.LastWriteTime,
                Length = file.Length
            });
        }
    }

    public void MergeChanges(List<(string, FileSystemEntryChange)> changes)
    {
        foreach (var (entry, change) in changes)
        {
            ParseEntry(entry, out var parent, out var entryName, out var isDirectory);
            var properties = change.Properties;
            if (isDirectory)
            {
                switch (change.Action)
                {
                    case FileSystemEntryAction.Create:
                        var directoryInfo = new CustomDirectoryInfo
                        {
                            Name = properties.RenameProps?.Name ?? entryName
                        };
                        parent.Directories.Add(directoryInfo);
                        break;

                    case FileSystemEntryAction.Rename:
                        // TODO: consider adding a LookupLinkedList method
                        // that would handle lookup property change on its own;
                        // currently removing and adding an object moves it to the end of the list, 
                        // while it is better to preserve the initial order
                        parent.Directories.Remove(entryName, out directoryInfo);
                        directoryInfo.Name = properties.RenameProps.Name;
                        parent.Directories.Add(directoryInfo);
                        break;

                    case FileSystemEntryAction.Change:
                        throw new DirectoryChangeActionNotAllowed();

                    case FileSystemEntryAction.Delete:
                        parent.Directories.Remove(entryName);
                        break;
                }
            }
            else
            {
                switch (change.Action)
                {
                    case FileSystemEntryAction.Create:
                        var fileInfo = new CustomFileInfo
                        {
                            Name = properties.RenameProps?.Name ?? entryName,
                            LastWriteTime = properties.ChangeProps.LastWriteTime,
                            Length = properties.ChangeProps.Length
                        };
                        parent.Files.Add(fileInfo);
                        break;

                    case FileSystemEntryAction.Rename:
                        parent.Files.Remove(entryName, out fileInfo);
                        fileInfo.Name = properties.RenameProps.Name;
                        parent.Files.Add(fileInfo);
                        break;

                    case FileSystemEntryAction.Change:
                        fileInfo = parent.Files[entryName];
                        fileInfo.LastWriteTime = properties.ChangeProps.LastWriteTime;
                        fileInfo.Length = properties.ChangeProps.Length;
                        break;

                    case FileSystemEntryAction.Delete:
                        parent.Files.Remove(entryName);
                        break;
                }
            }
        }
    }

    private void ParseEntry(string subpath, out CustomDirectoryInfo parent, out string entryName, out bool isDirectory)
    {
        var directoryNames = Path.TrimEndingDirectorySeparator(subpath).Split(Path.DirectorySeparatorChar);
        parent = this;
        foreach (var directoryName in directoryNames[..^1])
            parent = parent.Directories[directoryName];

        entryName = directoryNames[^1];
        isDirectory = Path.EndsInDirectorySeparator(subpath);
    }
}

public class CustomDirectoryInfoSerializer
{
    public static StringBuilder Serialize(CustomDirectoryInfo root)
    {
        var builder = new StringBuilder();
        Serialize(root, builder, depth: 0);
        return builder;
    }

    private static void Serialize(CustomDirectoryInfo root, StringBuilder builder, int depth)
    {
        foreach (var directory in root.Directories)
        {
            // Do not track top-level service folder(-s)
            if (builder.Length == 1 && SourceManager.IsServiceLocation(directory.Name))
                continue;

            builder.Append('\t', depth).Append(directory.Name).Append('\n');
            Serialize(directory, builder, depth + 1);
        }

        foreach (var file in root.Files)
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
