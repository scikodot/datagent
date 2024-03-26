using DatagentShared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DatagentMonitor.FileSystem;

internal class FileSystemEntryChangeProperties
{
    public RenameProperties? RenameProps { get; set; }
    public ChangeProperties? ChangeProps { get; set; }
}

internal abstract class ActionProperties
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

internal class RenameProperties : ActionProperties
{
    public string Name { get; set; }
}

internal class ChangeProperties : ActionProperties
{
    public DateTime LastWriteTime { get; init; }
    public long Length { get; init; }
}

internal enum FileSystemEntryAction
{
    Create,
    Rename,
    Change,
    Delete,
}

// TODO: consider adding Path property to correspond the change object to *what* exactly is changed;
// it is anyway used as a dict key, thus no additional allocs will take place
internal class FileSystemEntryChange
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

internal class CustomFileInfo
{
    public static readonly string DateTimeFormat = "yyyyMMddHHmmssfff";

    public string Name { get; set; }
    public DateTime LastWriteTime { get; set; }
    public long Length { get; set; }
}

internal class CustomDirectoryInfo
{
    public string Name { get; set; }
    public Dictionary<string, CustomDirectoryInfo> Directories { get; } = new();
    public Dictionary<string, CustomFileInfo> Files { get; } = new();

    private static void Serialize(DirectoryInfo root, TextWriter writer, StringBuilder builder)
    {
        foreach (var directory in builder.Wrap(root.EnumerateDirectories(), _ => '\t'))
        {
            // Do not track top-level service folder(-s)
            if (builder.Length == 1 && ServiceFilesManager.IsServiceLocation(directory.Name))
                continue;

            writer.WriteLine(builder.ToString(1, builder.Length - 1) + directory.Name);
            Serialize(directory, writer, builder);
        }

        foreach (var _ in builder.Wrap(root.EnumerateFiles(), f => $"{f.Name}: {f.LastWriteTime.ToString(CustomFileInfo.DateTimeFormat)}, {f.Length}"))
        {
            writer.WriteLine(builder.ToString());
        }
    }

    public static void SerializeRoot(bool backup = false)
    {
        var path = backup ? ServiceFilesManager.BackupIndexPath : ServiceFilesManager.IndexPath;
        using var writer = new StreamWriter(path, append: false, encoding: Encoding.UTF8);
        var builder = new StringBuilder();
        Serialize(new DirectoryInfo(ServiceFilesManager.Root), writer, builder);
    }

    //public static string Serialize(string path)
    //{
    //    using var writer = new StringWriter();
    //    var builder = new StringBuilder();
    //    Serialize(new DirectoryInfo(path), writer, builder);
    //    return writer.ToString();
    //}

    public static CustomDirectoryInfo DeserializeRoot()
    {
        var rootInfo = new CustomDirectoryInfo();
        var stack = new Stack<CustomDirectoryInfo>();
        stack.Push(rootInfo);
        using var reader = new StreamReader(ServiceFilesManager.IndexPath, Encoding.UTF8);
        while (!reader.EndOfStream)
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
            if (split.Length > 1)
            {
                // File
                var file = new CustomFileInfo
                {
                    LastWriteTime = DateTime.ParseExact(split[1], CustomFileInfo.DateTimeFormat, null),
                    Length = long.Parse(split[2]),
                };
                parent.Files.Add(split[0], file);
            }
            else
            {
                // Directory
                var directory = new CustomDirectoryInfo();
                parent.Directories.Add(split[0], directory);
                stack.Push(directory);
            }
        }

        return rootInfo;
    }

    public static CustomDirectoryInfo Deserialize(string path)
    {
        var rootInfo = new CustomDirectoryInfo();
        var stack = new Stack<CustomDirectoryInfo>();
        stack.Push(rootInfo);
        using var reader = new StreamReader(path, Encoding.UTF8);
        while (!reader.EndOfStream)
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
            if (split.Length > 1)
            {
                // File
                var file = new CustomFileInfo
                {
                    LastWriteTime = DateTime.ParseExact(split[1], CustomFileInfo.DateTimeFormat, null),
                    Length = long.Parse(split[2]),
                };
                parent.Files.Add(split[0], file);
            }
            else
            {
                // Directory
                var directory = new CustomDirectoryInfo();
                parent.Directories.Add(split[0], directory);
                stack.Push(directory);
            }
        }

        return rootInfo;
    }

    public void MergeChanges(List<(string, FileSystemEntryChange)> changes)
    {
        foreach (var (entry, change) in changes)
        {
            // Directory
            if (entry.EndsWith(Path.DirectorySeparatorChar))
            {
                var directory = GetEntryName(entry[..^1], out var parent);
                var properties = change.Properties;
                switch (change.Action)
                {
                    case FileSystemEntryAction.Create:
                        var directoryInfo = new CustomDirectoryInfo
                        {
                            Name = properties.RenameProps?.Name ?? directory
                        };
                        parent.Directories.Add(directoryInfo.Name, directoryInfo);
                        break;

                    case FileSystemEntryAction.Rename:
                        parent.Directories.Remove(directory, out directoryInfo);
                        directoryInfo.Name = properties.RenameProps.Name;
                        parent.Directories.Add(directoryInfo.Name, directoryInfo);
                        break;

                    case FileSystemEntryAction.Change:
                        throw new DirectoryChangeActionNotAllowed();

                    case FileSystemEntryAction.Delete:
                        parent.Directories.Remove(directory);
                        break;
                }
            }
            // File
            else
            {
                var file = GetEntryName(entry, out var parent);
                var properties = change.Properties;
                switch (change.Action)
                {
                    case FileSystemEntryAction.Create:
                        var fileInfo = new CustomFileInfo
                        {
                            Name = properties.RenameProps?.Name ?? file,
                            LastWriteTime = properties.ChangeProps.LastWriteTime,
                            Length = properties.ChangeProps.Length
                        };
                        parent.Files.Add(fileInfo.Name, fileInfo);
                        break;

                    case FileSystemEntryAction.Rename:
                        parent.Files.Remove(file, out fileInfo);
                        fileInfo.Name = properties.RenameProps.Name;
                        parent.Files.Add(fileInfo.Name, fileInfo);
                        break;

                    case FileSystemEntryAction.Change:
                        fileInfo = parent.Files[file];
                        fileInfo.LastWriteTime = properties.ChangeProps.LastWriteTime;
                        fileInfo.Length = properties.ChangeProps.Length;
                        break;

                    case FileSystemEntryAction.Delete:
                        parent.Files.Remove(file);
                        break;
                }
            }
        }
    }

    private string GetEntryName(string subpath, out CustomDirectoryInfo parent)
    {
        var split = subpath.Split(Path.DirectorySeparatorChar);
        parent = this;
        foreach (var directoryName in split[..^1])
            parent = parent.Directories[directoryName];

        return split[^1];
    }

    public void Serialize(string path)
    {
        using var writer = new StreamWriter(path, append: false, encoding: new UnicodeEncoding());
        var builder = new StringBuilder();
        Serialize(this, writer, builder);
    }

    private static void Serialize(CustomDirectoryInfo root, StreamWriter writer, StringBuilder builder)
    {
        foreach (var kvp in builder.Wrap(root.Directories, _ => '\t'))
        {
            // Do not track top-level service folder(-s)
            if (builder.Length == 1 && ServiceFilesManager.IsServiceLocation(kvp.Key))
                continue;

            writer.WriteLine(builder.ToString()[1..] + kvp.Key);
            Serialize(kvp.Value, writer, builder);
        }

        foreach (var _ in builder.Wrap(root.Files, kvp => $"{kvp.Key}: {kvp.Value.LastWriteTime.ToString(CustomFileInfo.DateTimeFormat)}, {kvp.Value.Length}"))
        {
            writer.WriteLine(builder.ToString());
        }
    }
}
