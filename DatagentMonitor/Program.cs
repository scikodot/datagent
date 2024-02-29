using Microsoft.Data.Sqlite;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileProviders.Physical;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using Microsoft.Extensions.Primitives;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

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
        class Tracker : IDisposable
        {
            private string _subpath;
            public string Subpath => _subpath;

            private IChangeToken _token;
            private IDisposable _disposer;
            public IChangeToken Token => _token;

            private readonly Dictionary<string, IFileInfo> _directories;
            public Dictionary<string, IFileInfo> Directories => _directories;

            private readonly Dictionary<string, IFileInfo> _files;
            public Dictionary<string, IFileInfo> Files => _files;

            public Tracker(string subpath)
            {
                _subpath = subpath;
                _files = new();
                _directories = new();

                RefreshToken();
            }

            public void RefreshToken()
            {
                _token = _provider.Watch(Path.Combine(_subpath, _pattern));
                _disposer = _token.RegisterChangeCallback(OnTokenFired, this);
            }

            public override bool Equals(object? obj)
            {
                if (obj is not Tracker tracker)
                    return false;

                return Subpath == tracker.Subpath;
            }

            public override int GetHashCode() => _subpath.GetHashCode();

            public void Dispose()
            {
                _disposer.Dispose();
                GC.SuppressFinalize(this);
            }
        }

        private static string _processName = "DatagentMonitor";
        private static string _root;
        private static string _dbFolderName = ".datagent";
        private static string _dbName = "events.db";
        private static string _dbPath;
        private static string _connectionString;
        private static List<string> _tableColumns = new() { "path", "type", "action" };

        private static readonly Dictionary<string, Tracker> _trackers = new();
        private static readonly string _pattern = "*";
        private static PhysicalFileProvider _provider;

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
                _root = Path.Combine("D:", "_source") + Path.DirectorySeparatorChar;
                var dbFolderPath = Path.Combine(_root, _dbFolderName);
                var dbFolder = Directory.CreateDirectory(dbFolderPath);
                dbFolder.Attributes |= FileAttributes.Hidden;
                _dbPath = Path.Combine(_root, _dbFolderName, _dbName);
                var target = "D:/_target";

                _connectionString = new SqliteConnectionStringBuilder
                {
                    DataSource = _dbPath,
                    Mode = SqliteOpenMode.ReadWriteCreate
                }.ToString();

                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    new SqliteCommand("CREATE TABLE IF NOT EXISTS events (time TEXT, type TEXT, path TEXT, misc TEXT)", connection).ExecuteNonQuery();
                }

                //// Check that all event tables are in place
                //var tablesSql = new string[]
                //{
                //    "CREATE TABLE IF NOT EXISTS created (path TEXT, time TEXT)",
                //    "CREATE TABLE IF NOT EXISTS renamed (path TEXT, time TEXT, old_name TEXT, name TEXT)",
                //    "CREATE TABLE IF NOT EXISTS changed (path TEXT, time TEXT, lwt TEXT, size INTEGER)",
                //    "CREATE TABLE IF NOT EXISTS deleted (path TEXT, time TEXT)",
                //};
                //using (var connection = new SqliteConnection(_connectionString))
                //{
                //    connection.Open();
                //    foreach (var sql in tablesSql)
                //        new SqliteCommand(sql, connection).ExecuteNonQuery();
                //}

                var watcher = new FileSystemWatcher(_root);

                watcher.NotifyFilter = NotifyFilters.Attributes
                                        | NotifyFilters.CreationTime
                                        | NotifyFilters.DirectoryName
                                        | NotifyFilters.FileName
                                        | NotifyFilters.LastWrite
                                        | NotifyFilters.Security
                                        | NotifyFilters.Size;

                watcher.Created += OnCreated;
                watcher.Renamed += OnRenamed;
                watcher.Changed += OnChanged;
                watcher.Deleted += OnDeleted;
                watcher.Error += OnError;

                watcher.Filter = "*";  // track all files, even with no extension
                watcher.IncludeSubdirectories = true;
                watcher.EnableRaisingEvents = true;

                //var targetPath = "D:/_target";
                //var targetFiles = new Dictionary<string, FileInfo>(
                //    Directory.EnumerateFiles(targetPath, "*", SearchOption.AllDirectories)
                //             .Select(f => new KeyValuePair<string, FileInfo>(f[targetPath.Length..], new FileInfo(f)))
                //);

                //var sourceFilesDeleted = new List<FileInfo>();
                //var sourcePath = "D:/_source";
                //foreach (var file in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
                //{
                //    var sourceFileInfo = new FileInfo(file);
                //    var subpath = file[sourcePath.Length..];
                //    if (targetFiles.Remove(subpath, out var targetFileInfo))
                //    {
                //        // TODO: if last-write-time's are equal but lengths are not, preserve the bigger file
                //        if (sourceFileInfo.LastWriteTime != targetFileInfo.LastWriteTime ||
                //            sourceFileInfo.Length != targetFileInfo.Length)
                //            OnChanged();

                //        // If the file was not changed, everything's ok
                //    }
                //    else
                //    {
                //        // No file with the given name on the given path
                //        // -> it could be moved somewhere else
                //        // -> queue the file for a later search
                //        sourceFilesDeleted.Add(sourceFileInfo);
                //    }
                //}



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

                // Monitoring example: https://github.com/dotnet/runtime/discussions/69700
                //
                // Underlying FileSystemWatcher does not respect wildcard patterns:
                // 1. By default, IncludeSubdirectories = true
                // 2. By default, OnRenamed events for files are fired twice (for old and new paths), and at least two tokens get notified:
                //    a. token monitoring the directory where that event has appeared
                //    b. token monitoring that directory's parent
                //
                // This means tokens with '*.*' pattern can get notifications from their subdirectories.
                //
                // For now, we avoid dealing with FSW and unrelated notifications.
                // Switching to FSW is to be considered only if polling proves to severely impact performance.
                //_provider = new PhysicalFileProvider("D:/_temp")
                //{
                //    UsePollingFileWatcher = true,
                //    UseActivePolling = true,
                //};
                //SetupTracker("");
                //Console.WriteLine("Setup complete.\n");

                //var stopwatch = new Stopwatch();
                //stopwatch.Start();
                //int seconds = 0;
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
                                case "SYNC":
                                    Synchronize(target);
                                    break;
                                case "DROP":
                                    up = false;
                                    Console.WriteLine("Shutting down...");
                                    break;
                            }

                            readComplete = true;
                        });
                    }

                    // Events polling, logging, etc.

                    //var elapsed = stopwatch.ElapsedMilliseconds / 1000;
                    //if (elapsed > seconds + 1)
                    //{
                    //    seconds += 1;
                    //    var text = $"Time elapsed: {seconds}";
                    //    if (pipeServerOut.IsConnected)
                    //    {
                    //        Console.Write("[Out] ");
                    //        try
                    //        {
                    //            streamOut.WriteString(text);
                    //        }
                    //        catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException or IOException)
                    //        {
                    //            pipeServerOut.Disconnect();
                    //            pipeServerOut.BeginWaitForConnection(x => Console.WriteLine("Client connected!"), null);
                    //        }
                    //    }
                    //    Console.WriteLine(text);
                    //}
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

        private static void Synchronize(string target)
        {
            using var connection = new SqliteConnection(_connectionString);
            using var reader = new SqliteCommand("SELECT * FROM events", connection).ExecuteReader();

            var map = new Dictionary<string, List<object?>>();

            while(reader.Read())
            {
                var path = reader.GetString(3);
                if (!map.ContainsKey(path))
                    map.Add(path, new List<object?>());
            }
        }

        private static SqliteDataReader GetReader(SqliteConnection connection, SqliteCommand command)
        {
            command.Connection = connection;
            return command.ExecuteReader();
        }

        private static void SetupTracker(string subpath)
        {
            var contents = _provider.GetDirectoryContents(subpath);
            if (contents is null || !contents.Exists)
                throw new ArgumentException($"No directory found at {GetSubpathRepr(subpath)}");

            var tracker = new Tracker(subpath);
            if (!_trackers.TryAdd(subpath, tracker))
                throw new ArgumentException($"Duplicate tracker for {GetSubpathRepr(subpath)}");

            Console.WriteLine($"Create token at {GetSubpathRepr(subpath)}");
            foreach (var entry in contents)
            {
                if (entry.IsDirectory)
                {
                    tracker.Directories.Add(entry.Name, entry);
                    SetupTracker(Path.Combine(subpath, entry.Name));
                }
                else
                {
                    tracker.Files.Add(entry.Name, entry);
                }
            }
        }

        private static void ReleaseTracker(string subpath)
        {
            // TODO: consider using a Tree for trackers; we need to remove only a single subdirectory branch, 
            // scanning the whole dict is not the way to go
            var keys = new List<string>(_trackers.Keys.Where(k => k.StartsWith(subpath)));
            foreach (var key in keys)
            {
                _trackers.Remove(key, out var tracker);
                tracker!.Dispose();
                Console.WriteLine($"Release token at {GetSubpathRepr(subpath)}");
            }
        }

        private static void OnTokenFired(object? state)
        {
            // TODO: folders operations are NOT detected by Matcher in tokens!
            // Possible solutions:
            // 1. Use a separate poller for directories (i.e. another background task that polls directories every 4 seconds)
            // 2. Give up on polling and evaluate diff on sync procedure

            // Complexity: O(m + n) time, O(m + n) space, 
            // where m = old files count, n = new files count
            var tracker = state as Tracker;
            var contents = _provider.GetDirectoryContents(tracker!.Subpath);
            if (!contents.Exists)
            {
                // Tracked directory renamed
                // -> leave the processing to the parent tracker
                if (tracker.Subpath == "")
                    throw new NotImplementedException("Renaming the root directory is not supported.");

                var sep = tracker.Subpath.LastIndexOf(Path.DirectorySeparatorChar);
                var parent = tracker.Subpath[..Math.Max(sep, 0)];
                Task.Run(() => OnTokenFired(_trackers[parent]));
                return;
            }

            var directoriesOld = tracker.Directories;
            var directoriesRes = new List<IFileInfo>();
            var filesOld = tracker.Files;
            var filesNew = new Dictionary<(int, long), IFileInfo>();
            var filesRes = new List<IFileInfo>();
            foreach (var entry in contents)
            {
                if (entry.IsDirectory)
                {
                    if (!directoriesOld.Remove(entry.Name, out _))
                    {
                        // The directory with this name does not exist, meaning it is either an old directory renamed, or a brand new one.
                        // Here we just traverse it and store its current size and the number of files inside.
                        // Determining whether it was a rename or not is to be done later at some point ...
                        //
                        // ... or even skipped at all. In the worst case scenario, rename = delete + create,
                        // so during the sync procedure we might only lose some clock time on re-creating already existing folder.
                        SetupTracker(Path.Combine(tracker.Subpath, entry.Name));
                        OnCreated(tracker.Subpath, entry);
                    }
                    // Otherwise, the directory with this name already exists
                    // -> all changes to its contents are tracked by the inner trackers (which remain consistent)
                    // -> everything's ok

                    directoriesRes.Add(entry);
                }
                else
                {
                    if (filesOld.Remove(entry.Name, out var fileNotRenamed))
                    {
                        if (entry.LastModified.ToUnixTimeMilliseconds() != fileNotRenamed.LastModified.ToUnixTimeMilliseconds() ||
                            entry.Length != fileNotRenamed.Length)
                        {
                            // Changed only
                            OnChanged(tracker.Subpath, entry);
                        }
                        // Otherwise, neither renamed, nor changed

                        filesRes.Add(entry);
                    }
                    else
                    {
                        filesNew.Add((entry.LastModified.Millisecond, entry.Length), entry);
                    }
                }
            }

            foreach (var directory in directoriesOld.Values)
            {
                // The directory got deleted -> its tracker needs to be released
                ReleaseTracker(Path.Combine(tracker.Subpath, directory.Name));
                OnDeleted(tracker.Subpath, directory);
            }

            directoriesOld.Clear();

            // Track all processed directories
            foreach (var directory in directoriesRes)
                directoriesOld.Add(directory.Name, directory);

            foreach (var file in filesOld.Values)
            {
                if (filesNew.Remove((file.LastModified.Millisecond, file.Length), out var fileNotChanged))
                {
                    OnRenamed(tracker.Subpath, file, fileNotChanged);
                    filesRes.Add(fileNotChanged);
                }
                else
                {
                    OnDeleted(tracker.Subpath, file);
                }
            }

            filesOld.Clear();

            foreach (var file in filesNew.Values)
            {
                OnCreated(tracker.Subpath, file);
            }

            // Track created files
            foreach (var file in filesNew.Values)
                filesOld.Add(file.Name, file);

            // Track all the other processed files
            foreach (var file in filesRes)
                filesOld.Add(file.Name, file);

            tracker.RefreshToken();
        }

        private static void OnCreated(string path, IFileInfo file)
        {
            Console.WriteLine($"At {GetSubpathRepr(path)}: [Create] {file.Name}");
        }

        private static void OnRenamed(string path, IFileInfo fileOld, IFileInfo fileNew)
        {
            Console.WriteLine($"At {GetSubpathRepr(path)}: [Rename] {fileOld.Name} -> {fileNew.Name}");
        }

        private static void OnChanged(string path, IFileInfo file)
        {
            Console.WriteLine($"At {GetSubpathRepr(path)}: [Change] {file.Name}");
        }

        private static void OnDeleted(string path, IFileInfo file)
        {
            Console.WriteLine($"At {GetSubpathRepr(path)}: [Delete] {file.Name}");
        }

        private static string GetSubpathRepr(string subpath) => $".{Path.DirectorySeparatorChar}{subpath}";

        private static void OnCreated(object sender, FileSystemEventArgs e)
        {
            // Ignore service files creation
            var subpath = e.FullPath[_root.Length..];
            if (subpath.StartsWith(_dbFolderName))
                return;

            InsertEntry("CREATE", subpath, null);

            Console.WriteLine($"At {e.FullPath}: [Create] {e.Name}");
        }

        private static void OnRenamed(object sender, RenamedEventArgs e)
        {
            var subpath = e.OldFullPath[_root.Length..];
            if (subpath.StartsWith(_dbFolderName))
            {
                // TODO: renaming service files may have unexpected consequences;
                // revert and/or throw an exception/notification
                return;
            }

            InsertEntry("RENAME", subpath, JsonSerializer.Serialize(new { old_name = e.OldName, new_name = e.Name }));

            Console.WriteLine($"At {e.FullPath}: [Rename] {e.OldName} -> {e.Name}");
        }

        private static void OnChanged(object sender, FileSystemEventArgs e)
        {
            // Ignore service files changes; we cannot distinguish user-made changes from software ones 
            var subpath = e.FullPath[_root.Length..];
            if (subpath.StartsWith(_dbFolderName))
                return;

            // Track changes to files only; directory changes are not essential
            var file = new FileInfo(e.FullPath);
            if (file.Attributes.HasFlag(FileAttributes.Directory))
                return;

            InsertEntry("CHANGE", subpath, null);

            Console.WriteLine($"At {e.FullPath}: [Change] {e.Name}");
        }

        private static void OnDeleted(object sender, FileSystemEventArgs e)
        {
            var subpath = e.FullPath[_root.Length..];
            if (subpath.StartsWith(_dbFolderName))
            {
                // TODO: deleting service files may have unexpected consequences,
                // and deleting the database means losing the track of all events up to the moment;
                // revert and/or throw an exception/notification
                return;
            }

            InsertEntry("DELETE", subpath, null);

            Console.WriteLine($"At {e.FullPath}: [Delete] {e.Name}");
        }

        private static void OnError(object sender, ErrorEventArgs e)
        {
            var ex = e.GetException();
            Console.WriteLine($"Message: {ex.Message}");
            Console.WriteLine("Stacktrace:");
            Console.WriteLine(ex.StackTrace);
            Console.ReadKey();
        }

        private static void InsertEntry(string type, string path, string? misc)
        {
            var command = new SqliteCommand("INSERT INTO events VALUES (:time, :type, :path, :misc)");
            command.Parameters.AddWithValue(":time", DateTime.Now.ToString("dd-MM-yyyy HH:mm:ss.fff"));
            command.Parameters.AddWithValue(":type", type);
            command.Parameters.AddWithValue(":path", path);
            command.Parameters.AddWithValue(":misc", misc != null ? misc : DBNull.Value);
            ExecuteNonQuery(command);
        }

        private static void ExecuteNonQuery(SqliteCommand command)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                command.Connection = connection;
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                Console.ReadKey();
            }
        }
    }
}