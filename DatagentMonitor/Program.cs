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

    public class StringStream
    {
        private Stream ioStream;
        private UnicodeEncoding streamEncoding;

        public StringStream(Stream ioStream)
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

        public async Task<string> ReadStringAsync()
        {
            // ReadAsync can read bytes faster than they are written to the stream;
            // continue reading until target count is reached
            int len = 2;
            var head = new byte[len];
            while (len > 0)
                len -= await ioStream.ReadAsync(head, head.Length - len, len);

            len = (head[0] * 256) | head[1];
            var inBuffer = new byte[len];
            while (len > 0)
                len -= await ioStream.ReadAsync(inBuffer, inBuffer.Length - len, len);

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

    public static class MonitorUtils
    {
        private static string _processName = "DatagentMonitor";
        private static int _timeout = 10000;

        private static void RegisterPosixInterruptSignals(Action<PosixSignalContext> action)
        {
            //PosixSignalRegistration.Create(PosixSignal.SIGTSTP, interrupt);  // TODO: use on Unix only
            PosixSignalRegistration.Create(PosixSignal.SIGTERM, action);
            PosixSignalRegistration.Create(PosixSignal.SIGINT, action);
        }

        public static Process? GetMonitorProcess()
        {
            int affine = Process.GetCurrentProcess().ProcessName == _processName ? 1 : 0;
            var processes = Process.GetProcessesByName(_processName);
            if (processes.Length == affine)
                return null;
            if (processes.Length > affine + 1)
                throw new Exception("Multiple active monitor instances.");

            return processes.MinBy(p => p.StartTime);
        }

        public static void Launch(string[] args)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = $"{_processName}.exe",
                Arguments = string.Join(" ", args),
                CreateNoWindow = true
            };
            new Process
            {
                StartInfo = startInfo,
            }.Start();
        }

        public static void Listen()
        {
            var monitor = GetMonitorProcess();
            if (monitor is null)
            {
                Console.WriteLine("No active monitor to listen.");
                return;
            }

            bool up = true;
            RegisterPosixInterruptSignals(ctx =>
            {
                up = false;
                Console.WriteLine($"{ctx.Signal} received, shutting down...");
            });

            var pipeClient = new NamedPipeClientStream(".", "datamon-out", PipeDirection.In, PipeOptions.CurrentUserOnly);
            Console.Write("Connecting to monitor... ");
            try
            {
                 pipeClient.Connect(_timeout);
            }
            catch (Exception e) when (e is TimeoutException or IOException)
            {
                Console.WriteLine("Failed.");
                Console.WriteLine("Connection timed out. Possible reasons are:\n1. Another listener is already up\n2. Monitor is not available");
                return;
            }
            
            Console.WriteLine("Done!");

            AppDomain.CurrentDomain.ProcessExit += (s, e) =>
            {
                pipeClient.Close();
            };

            var stream = new StringStream(pipeClient);
            while (up)
            {
                try
                {
                    Console.WriteLine(stream.ReadString());
                }
                catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException or IOException)
                {
                    Console.WriteLine("Monitor closed.");
                    break;
                }
            }
        }

        public static void Drop()
        {
            var monitor = GetMonitorProcess();
            if (monitor is null)
            {
                Console.WriteLine("No active monitor to drop.");
                return;
            }
            Console.WriteLine($"Monitor process ID: {monitor.Id}");

            var pipeClient = new NamedPipeClientStream(".", "datamon-in", PipeDirection.Out, PipeOptions.CurrentUserOnly);
            Console.Write("Connecting to monitor... ");
            pipeClient.Connect();
            Console.WriteLine("Done!");

            AppDomain.CurrentDomain.ProcessExit += (s, e) =>
            {
                pipeClient.Close();
            };

            Thread.Sleep(10000);

            var stream = new StringStream(pipeClient);
            Console.Write("Dropping... ");
            stream.WriteString("DROP");
            if (!monitor.WaitForExit(10000))
            {
                Console.WriteLine("No response. Killing...");
                monitor.Kill();
            }
            Console.WriteLine("Done!");
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
            var monitor = MonitorUtils.GetMonitorProcess();
            if (monitor is not null)
            {
                Console.WriteLine($"Monitor is already up. Process ID: {monitor.Id}");
                Console.WriteLine("Press any key to continue...");
                Console.ReadKey();
                return;
            }

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

            try
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

                var pipeServerIn = new NamedPipeServerStream("datamon-in", PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly | PipeOptions.WriteThrough);
                pipeServerIn.BeginWaitForConnection(x => Console.WriteLine("[In] Client connected!"), null);
                var pipeServerOut = new NamedPipeServerStream("datamon-out", PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly | PipeOptions.WriteThrough);
                pipeServerOut.BeginWaitForConnection(x => Console.WriteLine("[Out] Client connected!"), null);

                AppDomain.CurrentDomain.ProcessExit += (s, e) =>
                {
                    pipeServerIn.Close();
                    pipeServerOut.Close();
                    // TODO: log status
                };

                var streamIn = new StringStream(pipeServerIn);
                var streamOut = new StringStream(pipeServerOut);
                bool readComplete = true;

                var stopwatch = new Stopwatch();
                stopwatch.Start();
                int seconds = 0;
                bool up = true;
                while (up)
                {
                    if (readComplete && pipeServerIn.IsConnected)
                    {
                        readComplete = false;
                        _ = ProcessInput(streamIn, sig =>
                        {
                            Console.WriteLine($"Received: {sig}");
                            switch (sig)
                            {
                                case "DROP":
                                    up = false;
                                    Console.WriteLine("Shutting down...");
                                    break;
                            }

                            readComplete = true;
                        });
                    }

                    // Events polling, logging, etc.
                    var elapsed = stopwatch.ElapsedMilliseconds / 1000;
                    if (elapsed > seconds + 1)
                    {
                        seconds += 1;
                        var text = $"Time elapsed: {seconds}";
                        if (pipeServerOut.IsConnected)
                        {
                            Console.Write("[Out] ");
                            try
                            {
                                streamOut.WriteString(text);
                            }
                            catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException or IOException)
                            {
                                pipeServerOut.Disconnect();
                                pipeServerOut.BeginWaitForConnection(x => Console.WriteLine("Client connected!"), null);
                            }
                        }
                        Console.WriteLine(text);
                    }
                }

                Thread.Sleep(3000);
                //_connection.Dispose();
            }
            catch (Exception ex)
            {
                // TODO: log ex info somewhere
                Console.WriteLine(ex.ToString());
                Console.WriteLine($"Args: {string.Join(" ", args)}");
            }

            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();
        }

        private static async Task ProcessInput(StringStream stream, Action<string> action) => 
            action(await stream.ReadStringAsync());

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