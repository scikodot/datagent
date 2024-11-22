using System.IO.Pipes;
using System.Text;

namespace DatagentMonitor;

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

internal static class StringBuilderExtensions
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
    public static void CopyTo(this DirectoryInfo sourceRoot, string targetRoot)
    {
        if (Directory.Exists(targetRoot))
            throw new IOException("Target directory already exists.");

        var sourceQueue = new Queue<DirectoryInfo>();
        var targetQueue = new Queue<DirectoryInfo>();
        sourceQueue.Enqueue(sourceRoot);
        targetQueue.Enqueue(Directory.CreateDirectory(targetRoot));
        while (sourceQueue.TryDequeue(out var sourceInfo))
        {
            var targetInfo = targetQueue.Dequeue();
            foreach (var directory in sourceInfo.EnumerateDirectories())
            {
                sourceQueue.Enqueue(directory);
                targetQueue.Enqueue(targetInfo.CreateSubdirectory(directory.Name));
            }

            foreach (var file in sourceInfo.EnumerateFiles())
            {
                file.CopyTo(Path.Combine(targetInfo.FullName, file.Name));
            }
        }
    }
}

internal static class DateTimeExtensions
{
    public static readonly string SerializedFormat = "yyyyMMddHHmmssfff";

    public static DateTime TrimMicroseconds(this DateTime dt) => 
        new(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second, dt.Millisecond);

    public static string Serialize(this DateTime dt) => dt.ToString(SerializedFormat);

    public static DateTime Parse(string s) => DateTime.ParseExact(s, SerializedFormat, null);
}

internal static class EnumExtensions
{
    public static string GetNameEx<TEnum>(TEnum value) where TEnum : struct, Enum => 
        Enum.GetName(value) ?? throw new ArgumentException($"{value.GetType().Name} does not contain a definition for value {value}.");
}

internal static class EnumerableExtensions
{
    public static IEnumerable<(IEnumerable<T> First, IEnumerable<T> Second)> ZipOuter<T>(
        this IEnumerable<IEnumerable<T>> first, IEnumerable<IEnumerable<T>> second)
    {
        using var enumFirst = first.GetEnumerator();
        using var enumSecond = second.GetEnumerator();
        while (enumFirst.MoveNext())
            yield return (enumFirst.Current, enumSecond.MoveNext() ? enumSecond.Current : Enumerable.Empty<T>());
        
        while (enumSecond.MoveNext())
            yield return (Enumerable.Empty<T>(), enumSecond.Current);
    }

    public static string ToPath(this IEnumerable<string> source) => Path.Combine(source.ToArray());
}

internal static class ListExtensions
{
    public static string ToPath(this List<string> list) => Path.Combine(list.ToArray());
}
