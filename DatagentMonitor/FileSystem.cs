using DatagentShared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DatagentMonitor.FileSystem
{
    public abstract class ActionProperties
    {
        private static readonly JsonSerializerOptions _options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            IgnoreReadOnlyFields = true
        };

        public string Serialize() => JsonSerializer.Serialize(this, options: _options);

        public static ActionProperties Deserialize(string json)
        {
            var res = JsonSerializer.Deserialize<ActionProperties>(json, options: _options);
            return res ?? throw new ArgumentException("JSON input has invalid format.");
        }
    }

    public class DirectoryActionProperties : ActionProperties
    {
        public string Name { get; init; }  // This is a *new* name after renaming, if it took place
    }

    public class FileActionProperties : ActionProperties
    {
        public string Name { get; init; }  // This is a *new* name after renaming, if it took place
        public DateTime LastWriteTime { get; init; }
        public long Length { get; init; }
    }

    internal enum FileSystemEntryAction
    {
        Created,
        Renamed,
        Changed,
        Deleted,
    }

    internal class FileSystemEntryChange
    {
        public DateTime Timestamp { get; set; }
        public FileSystemEntryAction Action { get; set; }
        public ActionProperties? Properties { get; set; } = null;
    }

    internal class CustomFileInfo
    {
        public static readonly string DateTimeFormat = "yyyyMMddHHmmssfff";

        public string Name { get; set; }
        public DateTime LastWriteTime { get; set; }
        public long Length { get; set; }

        public CustomFileInfo() { }

        public CustomFileInfo(FileActionProperties properties)
        {
            Update(properties);
        }

        public void Update(FileActionProperties properties)
        {
            Name = properties.Name;
            LastWriteTime = properties.LastWriteTime;
            Length = properties.Length;
        }
    }

    internal class CustomDirectoryInfo
    {
        public string Name { get; set; }
        public Dictionary<string, CustomDirectoryInfo> Directories { get; } = new();
        public Dictionary<string, CustomFileInfo> Files { get; } = new();

        public void Update(DirectoryActionProperties properties)
        {
            Name = properties.Name;
        }

        private static void Serialize(DirectoryInfo root, TextWriter writer, StringBuilder builder)
        {
            foreach (var directory in builder.Wrap(root.EnumerateDirectories(), _ => '\t'))
            {
                // Do not track top-level service folder(-s)
                if (builder.Length == 1 && ServiceFilesManager.IsServiceLocation(directory.Name))
                    continue;

                writer.WriteLine(builder.ToString()[1..] + directory.Name);
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
                    throw new ArgumentException("Invalid index format.");

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
                    throw new ArgumentException("Invalid index format.");

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

        // TODO: use OrderedDictionary or order by timestamp
        public void MergeChanges(Dictionary<string, FileSystemEntryChange> changes)
        {
            foreach (var kvp in changes)
            {
                if (kvp.Key.EndsWith(Path.DirectorySeparatorChar))
                {
                    // The change was applied to a directory
                    var directory = GetEntry(kvp.Key[..^1], out var parent);
                    var properties = kvp.Value.Properties as DirectoryActionProperties;
                    switch (kvp.Value.Action)
                    {
                        case FileSystemEntryAction.Created:
                            parent.Directories.Add(directory, new CustomDirectoryInfo { Name = directory });
                            break;
                        case FileSystemEntryAction.Renamed:
                            parent.Directories[directory].Update(properties);
                            break;
                        case FileSystemEntryAction.Changed:
                            throw new ArgumentException("Changed action not allowed for directory.");
                        case FileSystemEntryAction.Deleted:
                            parent.Directories.Remove(directory);
                            break;
                        default:
                            throw new ArgumentException("Unknown action type.");
                    }
                }
                else
                {
                    // The change was applied to a file
                    var file = GetEntry(kvp.Key, out var parent);
                    var properties = kvp.Value.Properties as FileActionProperties;
                    switch (kvp.Value.Action)
                    {
                        case FileSystemEntryAction.Created:
                            parent.Files.Add(file, new CustomFileInfo(properties));
                            break;
                        case FileSystemEntryAction.Renamed:
                        case FileSystemEntryAction.Changed:
                            parent.Files[file].Update(properties);
                            break;
                        case FileSystemEntryAction.Deleted:
                            parent.Files.Remove(file);
                            break;
                        default:
                            throw new ArgumentException("Unknown action type.");
                    }
                }
            }
        }

        private string GetEntry(string subpath, out CustomDirectoryInfo parent)
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
}
