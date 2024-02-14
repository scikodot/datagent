using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;

namespace DatagentMonitor
{
    public enum Actions
    {
        Created,
        Renamed,
        Changed,
        RenamedChanged,
        Deleted,
    }

    public class StreamString
    {
        private Stream ioStream;
        private UnicodeEncoding streamEncoding;

        public StreamString(Stream ioStream)
        {
            this.ioStream = ioStream;
            streamEncoding = new UnicodeEncoding();
        }

        public string? ReadString()
        {
            int len;
            len = ioStream.ReadByte() * 256;
            len |= ioStream.ReadByte();
            var inBuffer = new byte[len];
            ioStream.Read(inBuffer, 0, len);

            return streamEncoding.GetString(inBuffer);
        }

        public int WriteString(string outString)
        {
            byte[] outBuffer = streamEncoding.GetBytes(outString);
            int len = outBuffer.Length;
            if (len > UInt16.MaxValue)
            {
                len = (int)UInt16.MaxValue;
            }
            ioStream.WriteByte((byte)(len / 256));
            ioStream.WriteByte((byte)(len & 255));
            ioStream.Write(outBuffer, 0, len);
            ioStream.Flush();

            return outBuffer.Length + 2;
        }
    }

    public class Program
    {
        private static string _processName = "DatagentMonitor";
        private static string _dbPath;
        private static string _tableName = "deltas";
        private static List<string> _tableColumns = new() { "path", "type", "action" };
        private static SqliteConnection _connection;

        static void Main(string[] args)
        {
            // Create a string stream for serving or accepting messages over a pipe
            StreamString stream;

            try
            {
                Console.WriteLine($"Args: [{string.Join(" ", args)}]\n");
                if (args.Length > 0 && args[0] == "listen")
                {
                    LaunchClient();
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                Console.ReadKey();
                return;
            }

            LaunchServer();

            /*foreach (var arg in args)
            {
                if (arg.Length < 3)
                    throw new ArgumentException("No argument name given.");

                var split = arg[2..].Split('=');
                if (split.Length < 2 || split[0] == "" || split[1] == "")
                    throw new ArgumentException("No argument name and/or value given.");

                (var argName, var argValue) = (split[0], split[1]);
                switch (argName)
                {
                    case "db-path":
                        _dbPath = argValue;
                        break;
                    //...
                    default:
                        throw new ArgumentException($"Unexpected argument: {argName}");
                }
            }*/
        }

        private static void LaunchClient()
        {
            bool up = true;

            var interrupt = (PosixSignalContext context) =>
            {
                up = false;
                Console.WriteLine("Shutting down...");
            };
            PosixSignalRegistration.Create(PosixSignal.SIGTSTP, interrupt);
            PosixSignalRegistration.Create(PosixSignal.SIGTERM, interrupt);
            PosixSignalRegistration.Create(PosixSignal.SIGINT, interrupt);

            Console.WriteLine("Setting up pipe client...");
            var pipeClient = new NamedPipeClientStream(".", "datamon", PipeDirection.In, PipeOptions.CurrentUserOnly);
            Console.Write("Connecting...");
            pipeClient.Connect();
            Console.WriteLine($"Result: {pipeClient.IsConnected}");
            var stream = new StreamString(pipeClient);
            Console.WriteLine("Polling...");
            while (up)
            {
                var text = stream.ReadString();
                if (text != null)
                    Console.WriteLine(text);
            }

            pipeClient.Close();
            Console.WriteLine("Process has exited, listening complete.");
        }

        private static void LaunchServer()
        {
            /*// Connect to the specified database
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = _dbPath,
                Mode = SqliteOpenMode.ReadWriteCreate
            }.ToString();
            _connection = new SqliteConnection(connectionString);
            _connection.Open();

            // Setup watcher
            // TODO: use aux file, say .monitor-filter, to watch only over a subset of files/directories
            var watcher = new FileSystemWatcher(Path.GetDirectoryName(_dbPath));

            watcher.NotifyFilter = NotifyFilters.Attributes
                                 | NotifyFilters.CreationTime
                                 | NotifyFilters.DirectoryName
                                 | NotifyFilters.FileName
                                 | NotifyFilters.LastAccess
                                 | NotifyFilters.LastWrite
                                 | NotifyFilters.Security
                                 | NotifyFilters.Size;

            watcher.Changed += OnChanged;
            watcher.Created += OnCreated;
            watcher.Deleted += OnDeleted;
            watcher.Renamed += OnRenamed;
            watcher.Error += OnError;

            watcher.Filter = "*";  // track all files, even with no extension
            watcher.IncludeSubdirectories = true;
            watcher.EnableRaisingEvents = true;*/

            Console.WriteLine("Setting up pipe server...");
            var pipeServer = new NamedPipeServerStream("datamon", PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly | PipeOptions.WriteThrough);
            var stream = new StreamString(pipeServer);
            pipeServer.BeginWaitForConnection(x => Console.WriteLine("Client connected!"), null);

            // Cleanup on system or emergency shutdown
            AppDomain.CurrentDomain.ProcessExit += (s, e) => pipeServer.Close();

            var stopwatch = new Stopwatch();
            stopwatch.Start();
            int seconds = 0;
            while (true)
            {
                // Events polling, logging, etc.
                var elapsed = stopwatch.ElapsedMilliseconds / 1000;
                if (elapsed > seconds + 1)
                {
                    seconds += 1;
                    var text = $"Time elapsed: {seconds}";
                    if (pipeServer.IsConnected)
                    {
                        Console.Write("[Sending] ");
                        try
                        {
                            stream.WriteString(text);
                        }
                        catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException or IOException)
                        {
                            pipeServer.Disconnect();
                            pipeServer.BeginWaitForConnection(x => Console.WriteLine("Client connected!"), null);
                        }
                    }
                    Console.WriteLine(text);
                }
            }

            //_connection.Dispose();
        }

        private static void OnChanged(object sender, FileSystemEventArgs e)
        {
            if (e.ChangeType != WatcherChangeTypes.Changed)
                return;

            // Track changes to files only; directory changes are not essential
            var attrs = File.GetAttributes(e.FullPath);
            if (attrs.HasFlag(FileAttributes.Directory))
                return;

            var command = new SqliteCommand("SELECT action WHERE path=:path");
            command.Parameters.AddWithValue(":path", e.FullPath);
            command.Connection = _connection;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var action = reader.GetString(0);

            }


            Console.WriteLine($"Changed: {e.FullPath}");
        }

        private static void OnCreated(object sender, FileSystemEventArgs e)
        {
            string value = $"Created: {e.FullPath}";
            Console.WriteLine(value);
        }

        private static void OnDeleted(object sender, FileSystemEventArgs e) =>
            Console.WriteLine($"Deleted: {e.FullPath}");

        private static void OnRenamed(object sender, RenamedEventArgs e)
        {
            Console.WriteLine($"Renamed:");
            Console.WriteLine($"    Old: {e.OldFullPath}");
            Console.WriteLine($"    New: {e.FullPath}");
        }

        private static void OnError(object sender, ErrorEventArgs e) =>
            PrintException(e.GetException());

        private static void PrintException(Exception? ex)
        {
            if (ex != null)
            {
                Console.WriteLine($"Message: {ex.Message}");
                Console.WriteLine("Stacktrace:");
                Console.WriteLine(ex.StackTrace);
                Console.WriteLine();
                PrintException(ex.InnerException);
            }
        }
    }
}