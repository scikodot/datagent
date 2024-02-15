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

        public string ReadString()
        {
            int first = ioStream.ReadByte();
            if (first == -1)
                throw new IOException("End of stream.");

            int len;
            len = first * 256;
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
            Console.WriteLine($"Args: {string.Join(" ", args)}\n");
            if (args.Length > 0)
            {
                switch (args[0])
                {
                    case "up":
                        try
                        {
                            LaunchServer();
                        }
                        catch (Exception ex)
                        {
                            // TODO: log ex info somewhere
                            Console.WriteLine(ex.ToString());
                            Console.WriteLine("Press any key to continue...");
                            Console.ReadKey();
                        }
                        break;
                    case "listen":
                        try
                        {
                            LaunchClient();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex.ToString());
                            Console.WriteLine("Press any key to continue...");
                            Console.ReadKey();
                        }
                        break;
                }
            }

            return;

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

            void interrupt(PosixSignalContext context)
            {
                up = false;
                Console.WriteLine($"Received {context.Signal}, shutting down...");
            };
            //PosixSignalRegistration.Create(PosixSignal.SIGTSTP, interrupt);  // TODO: use on Unix only
            PosixSignalRegistration.Create(PosixSignal.SIGTERM, interrupt);
            PosixSignalRegistration.Create(PosixSignal.SIGINT, interrupt);

            Console.WriteLine("Setting up pipe client...");
            var pipeClient = new NamedPipeClientStream(".", "datamon", PipeDirection.In, PipeOptions.CurrentUserOnly);
            Console.Write("Connecting...");
            pipeClient.Connect();
            Console.WriteLine($"Result: {pipeClient.IsConnected}");
            var stream = new StreamString(pipeClient);
            Console.WriteLine("Start polling...");
            while (up)
            {
                string text;
                try
                {
                    text = stream.ReadString();
                }
                catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException or IOException)
                {
                    Console.WriteLine("Monitor closed, shutting down...");
                    break;
                }

                Console.WriteLine(text);
            }

            pipeClient.Close();
            Console.WriteLine("Listening complete.");
            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();
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

            bool up = true;

            void interrupt(PosixSignalContext context)
            {
                up = false;
                Console.WriteLine($"Received {context.Signal}, shutting down...");
            };
            PosixSignalRegistration.Create(PosixSignal.SIGTERM, interrupt);
            PosixSignalRegistration.Create(PosixSignal.SIGINT, interrupt);  // TODO: for debug purposes

            Console.WriteLine("Setting up pipe server...");
            var pipeServer = new NamedPipeServerStream("datamon", PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly | PipeOptions.WriteThrough);
            pipeServer.BeginWaitForConnection(x => Console.WriteLine("Client connected!"), null);
            var stream = new StreamString(pipeServer);

            // Cleanup on exit, incl. system or emergency shutdown
            AppDomain.CurrentDomain.ProcessExit += (s, e) =>
            {
                pipeServer.Close();
                // TODO: log status
            };

            var stopwatch = new Stopwatch();
            stopwatch.Start();
            int seconds = 0;
            while (up)
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