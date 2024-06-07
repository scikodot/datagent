using System.Text;

namespace DatagentMonitor.FileSystem;

// TODO: disallow future timestamps for LastWriteTime's of both files and directories
public static class CustomDirectoryInfoSerializer
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
