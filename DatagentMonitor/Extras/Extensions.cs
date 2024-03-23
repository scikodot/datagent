using DatagentMonitor.FileSystem;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
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

    public static class PipeStreamExtensions
    {
        private static readonly UnicodeEncoding _encoding = new();

        public static string ReadString(this PipeStream stream)
        {
            int first = stream.ReadByte();
            if (first == -1)
                throw new IOException("End of stream.");

            int len;
            len = first * 256;
            len |= stream.ReadByte();
            var inBuffer = new byte[len];
            stream.Read(inBuffer, 0, len);

            return _encoding.GetString(inBuffer);
        }

        public static async Task<string> ReadStringAsync(this PipeStream stream)
        {
            // ReadAsync can read bytes faster than they are written to the stream;
            // continue reading until target count is reached
            int len = 2;
            var head = new byte[len];
            while (len > 0)
                len -= await stream.ReadAsync(head.AsMemory(head.Length - len, len));

            len = (head[0] * 256) | head[1];
            var inBuffer = new byte[len];
            while (len > 0)
                len -= await stream.ReadAsync(inBuffer.AsMemory(inBuffer.Length - len, len));

            return _encoding.GetString(inBuffer);
        }

        public static int WriteString(this PipeStream stream, string outString)
        {
            byte[] outBuffer = _encoding.GetBytes(outString);
            int len = outBuffer.Length;
            if (len > UInt16.MaxValue)
            {
                len = (int)UInt16.MaxValue;
            }
            stream.WriteByte((byte)(len / 256));
            stream.WriteByte((byte)(len & 255));
            stream.Write(outBuffer, 0, len);
            stream.Flush();

            return outBuffer.Length + 2;
        }

        public static async Task<int> WriteStringAsync(this PipeStream stream, string outString)
        {
            byte[] outBuffer = _encoding.GetBytes(outString);
            int len = outBuffer.Length;
            if (len > ushort.MaxValue)
            {
                // TODO: notify of a too long string
                len = ushort.MaxValue;
            }

            byte[] lenBuffer = new byte[2] { (byte)(len / 256), (byte)(len & 255) };
            await stream.WriteAsync(lenBuffer.AsMemory());
            await stream.WriteAsync(outBuffer.AsMemory());
            await stream.FlushAsync();

            return lenBuffer.Length + outBuffer.Length;
        }
    }

    public static class NamedPipeServerStreamExtensions
    {
        public static async Task<bool> WaitForConnectionSafeAsync(this NamedPipeServerStream stream, int milliseconds)
        {
            var tokenSource = new CancellationTokenSource(milliseconds);
            try
            {
                await stream.WaitForConnectionAsync(tokenSource.Token);
                return true;
            }
            catch (OperationCanceledException)
            {
                
            }

            return false;
        }

        public static async Task<string?> ReadStringSafeAsync(this NamedPipeServerStream stream)
        {
            // No sender
            if (!stream.IsConnected)
                return null;

            try
            {
                return await stream.ReadStringAsync();
            }
            catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException)
            {
                // Sender got closed; manually set server disconnected state
                stream.Disconnect();
            }

            return null;
        }

        public static async Task<int?> WriteStringSafeAsync(this NamedPipeServerStream stream, string message)
        {
            // No receiver
            if (!stream.IsConnected)
                return null;

            try
            {
                return await stream.WriteStringAsync(message);
            }
            catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException or IOException)
            {
                // Receiver got closed; manually set server disconnected state
                stream.Disconnect();
            }

            return null;
        }
    }

    public static class DateTimeExtensions
    {
        public static DateTime TrimMicroseconds(this DateTime dt) => 
            new(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second, dt.Millisecond);
    }
}
