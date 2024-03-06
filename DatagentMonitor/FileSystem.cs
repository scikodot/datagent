using DatagentShared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DatagentMonitor.FileSystem
{
    internal enum FileSystemEntryAction
    {
        Created,
        Renamed,
        Changed,
        Deleted,
    }

    internal class FileSystemEntryChange
    {
        public DateTime? Timestamp { get; set; } = null;
        public FileSystemEntryAction Action { get; set; }
    }

    internal class CustomFileInfo
    {
        public static readonly string DateTimeFormat = "ddMMyyyy_HHmmss.fff";

        public DateTime LastWriteTime { get; init; }
        public long Length { get; init; }
    }

    internal class CustomDirectoryInfo
    {
        public Dictionary<string, CustomDirectoryInfo> Directories { get; } = new();
        public Dictionary<string, CustomFileInfo> Files { get; } = new();

        private static void Serialize(DirectoryInfo root, StreamWriter writer, StringBuilder builder)
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

        public static void SerializeRoot()
        {
            using var writer = new StreamWriter(ServiceFilesManager.IndexPath, append: false, encoding: Encoding.UTF8);
            var builder = new StringBuilder();
            Serialize(new DirectoryInfo(ServiceFilesManager.Root), writer, builder);
        }

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
    }
}
