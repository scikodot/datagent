using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DatagentMonitor
{
    internal static class StringExtensions
    {
        public static int StartsWithCount(this string str, char value)
        {
            int count = 0;
            foreach (var ch in str)
            {
                if (ch != value)
                    break;

                count++;
            }

            return count;
        }
    }

    internal static class StringBuildExtensions
    {
        public static IEnumerable<T> Wrap<T, S>(this StringBuilder builder, IEnumerable<T> collection, Func<T, S> selector)
        {
            int len;
            foreach (var item in collection)
            {
                len = builder.Length;
                builder.Append(selector(item));
                yield return item;
                builder.Remove(len, builder.Length - len);
            }
        }
    }

    internal static class DirectoryExtensions
    {
        public static void Copy(string sourceRoot, string targetRoot)
        {
            if (Directory.Exists(targetRoot))
                throw new ArgumentException("Directory already exists on target.");

            Directory.CreateDirectory(targetRoot);
            var queue = new Queue<DirectoryInfo>();
            queue.Enqueue(new DirectoryInfo(sourceRoot));
            while (queue.TryDequeue(out var info))
            {
                foreach (var directory in info.EnumerateDirectories())
                    queue.Enqueue(directory);

                foreach (var file in info.EnumerateFiles())
                    File.Copy(file.FullName, file.FullName.Replace(sourceRoot, targetRoot));
            }
        }
    }
}
