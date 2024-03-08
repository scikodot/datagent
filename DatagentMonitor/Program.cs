using DatagentShared;
using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using DatagentMonitor.FileSystem;
using DatagentMonitor.Utils;
using System.Net.NetworkInformation;
using System.Collections.Concurrent;

namespace DatagentMonitor
{
    public class Program
    {
        private static Database _database;
        private static NamedPipeServerStream _pipeServerIn;
        private static NamedPipeServerStream _pipeServerOut;
        private static ConcurrentQueue<Task> _tasks = new ConcurrentQueue<Task>();

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
                ServiceFilesManager.Initialize(root: Path.Combine("D:", "_source") + Path.DirectorySeparatorChar);
                var targetRoot = Path.Combine("D:", "_target");

                CustomDirectoryInfo.SerializeRoot();
                var info = CustomDirectoryInfo.DeserializeRoot();

                _database = new Database(ServiceFilesManager.MonitorDatabasePath);
                _database.ExecuteNonQuery(new SqliteCommand("CREATE TABLE IF NOT EXISTS events (time TEXT, type TEXT, path TEXT, misc TEXT)"));

                var watcher = new FileSystemWatcher(ServiceFilesManager.Root)
                {
                    NotifyFilter = NotifyFilters.Attributes
                                 | NotifyFilters.CreationTime
                                 | NotifyFilters.DirectoryName
                                 | NotifyFilters.FileName
                                 | NotifyFilters.LastWrite
                                 | NotifyFilters.Security
                                 | NotifyFilters.Size
                };

                watcher.Created += OnCreated;
                watcher.Renamed += OnRenamed;
                watcher.Changed += OnChanged;
                watcher.Deleted += OnDeleted;
                watcher.Error += OnError;

                watcher.Filter = "*";  // track all files, even with no extension
                watcher.IncludeSubdirectories = true;
                watcher.EnableRaisingEvents = true;                

                _pipeServerIn = new NamedPipeServerStream("datagent-monitor-in", PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly | PipeOptions.WriteThrough);
                _pipeServerIn.BeginWaitForConnection(x => Console.WriteLine("[In] Client connected!"), null);
                _pipeServerOut = new NamedPipeServerStream("datagent-monitor-out", PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly | PipeOptions.WriteThrough);
                _pipeServerOut.BeginWaitForConnection(x => Console.WriteLine("[Out] Client connected!"), null);

                AppDomain.CurrentDomain.ProcessExit += (s, e) =>
                {
                    _pipeServerIn.Close();
                    _pipeServerOut.Close();
                    // TODO: log status
                };

                bool readComplete = true;

                bool up = true;
                while (up)
                {
                    // Remove completed tasks up until the first uncompleted
                    while (_tasks.TryPeek(out var task) && task.IsCompleted)
                        _tasks.TryDequeue(out _);

                    if (readComplete && _pipeServerIn.IsConnected)
                    {
                        readComplete = false;
                        _ = ProcessInput(signal =>
                        {
                            Console.WriteLine($"Received: {signal}");
                            switch (signal)
                            {
                                case "SYNC":
                                    Synchronize(targetRoot);
                                    break;
                                case "DROP":
                                    up = false;
                                    Console.WriteLine("Shutting down...");
                                    break;
                            }

                            readComplete = true;
                        });
                    }

                    Thread.Sleep(1000);
                }
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

        private static async Task ProcessInput(Action<string> action) => 
            action(await _pipeServerIn.ReadStringAsync());

        private static void Synchronize(string targetRoot)
        {
            var targetDelta = GetTargetDelta(targetRoot);
            var sourceDelta = GetSourceDelta();
            var lastSyncTimestamp = GetTargetLastSyncTimestamp(targetRoot);
            foreach (var targetEntry in targetDelta)
            {
                if (sourceDelta.Remove(targetEntry.Key, out var sourceEntry))
                {
                    // Entry change is present on both source and target
                    // -> determine which of two changes is actual
                    if (lastSyncTimestamp == null || sourceEntry.Timestamp >= lastSyncTimestamp)
                    {
                        // TODO:
                        // If lastSyncTimestamp == null
                        // -> target does not contain any info of last sync
                        // -> it is impossible to determine which changes are actual and which are outdated
                        // -> only 2 options, akin to Git:
                        //      1. Employ a specific strategy (ours, theirs, etc.)
                        //      2. Perform a manual merge
                        //
                        // This also holds if both timestamps are equal (which is highly unlikely, but not improbable).

                        // Source is actual
                        ApplyChange(ServiceFilesManager.Root, targetRoot, targetEntry.Key, sourceEntry);
                    }
                    else
                    {
                        // Target is actual
                        ApplyChange(targetRoot, ServiceFilesManager.Root, targetEntry.Key, targetEntry.Value);
                    }
                }
                else
                {
                    // Entry change is present only on target
                    // -> propagate the change to source
                    ApplyChange(targetRoot, ServiceFilesManager.Root, targetEntry.Key, targetEntry.Value);
                }
            }

            foreach (var sourceEntry in sourceDelta)
            {
                // Entry change is present only on source
                // -> propagate the change to target
                ApplyChange(ServiceFilesManager.Root, targetRoot, sourceEntry.Key, sourceEntry.Value);
            }
        }

        private static void ApplyChange(string sourceRoot, string targetRoot, string subpath, FileSystemEntryChange change)
        {
            var sourcePath = Path.Combine(sourceRoot, subpath);
            var targetPath = Path.Combine(targetRoot, subpath);
            switch (change.Action)
            {
                case FileSystemEntryAction.Created:
                    if (File.GetAttributes(sourcePath).HasFlag(FileAttributes.Directory))
                        DirectoryExtensions.Copy(sourcePath, targetPath);
                    else
                        File.Copy(sourcePath, targetPath);
                    break;
                case FileSystemEntryAction.Changed:
                    // Note: Changed action must not appear for a directory
                    File.Copy(sourcePath, targetPath, overwrite: true);
                    break;
                case FileSystemEntryAction.Deleted:
                    if (File.GetAttributes(targetPath).HasFlag(FileAttributes.Directory))
                        Directory.Delete(targetPath, true);
                    else
                        File.Delete(targetPath);
                    break;
            }
        }

        public static DateTime? GetTargetLastSyncTimestamp(string targetRoot)
        {
            var targetDatabase = new Database(Path.Combine(targetRoot, ServiceFilesManager.Folder, ServiceFilesManager.MonitorDatabase));
            DateTime? result = null;
            targetDatabase.ExecuteReader(new SqliteCommand("SELECT * FROM sync ORDER BY time DESC LIMIT 1"), reader =>
            {
                if (reader.Read())
                    result = DateTime.ParseExact(reader.GetString(1), CustomFileInfo.DateTimeFormat, null);
            });
            return result;
        }

        private static Dictionary<string, FileSystemEntryChange> GetTargetDelta(string targetRoot)
        {
            var sourceDir = CustomDirectoryInfo.DeserializeRoot();  // last synced source data
            var targetDir = new DirectoryInfo(targetRoot);
            var builder = new StringBuilder();
            var delta = new Dictionary<string, FileSystemEntryChange>();
            GetTargetDelta(sourceDir, targetDir, builder, delta);
            return delta;
        }

        private static void GetTargetDelta(CustomDirectoryInfo sourceDir, DirectoryInfo targetDir, StringBuilder builder, Dictionary<string, FileSystemEntryChange> delta)
        {
            foreach (var targetSubdir in builder.Wrap(targetDir.EnumerateDirectories(), d => d.Name))
            {
                if (sourceDir.Directories.Remove(targetSubdir.Name, out var sourceSubdir))
                {
                    GetTargetDelta(sourceSubdir, targetSubdir, builder, delta);
                }
                else
                {
                    delta[builder.ToString()] = new FileSystemEntryChange
                    {
                        Action = FileSystemEntryAction.Created
                    };
                }
            }

            foreach (var _ in builder.Wrap(sourceDir.Directories, kvp => kvp.Key))
            {
                delta[builder.ToString()] = new FileSystemEntryChange
                {
                    Action = FileSystemEntryAction.Deleted
                };
            }

            foreach (var targetFile in builder.Wrap(targetDir.EnumerateFiles(), f => f.Name))
            {
                if (sourceDir.Files.Remove(targetFile.Name, out var sourceFile))
                {
                    if (targetFile.LastWriteTime != sourceFile.LastWriteTime || targetFile.Length != sourceFile.Length)
                    {
                        delta[builder.ToString()] = new FileSystemEntryChange
                        {
                            Action = FileSystemEntryAction.Changed
                        };
                    }
                }
                else
                {
                    delta[builder.ToString()] = new FileSystemEntryChange
                    {
                        Action = FileSystemEntryAction.Created
                    };
                }
            }

            foreach (var _ in builder.Wrap(sourceDir.Files, kvp => kvp.Key))
            {
                delta[builder.ToString()] = new FileSystemEntryChange
                {
                    Action = FileSystemEntryAction.Deleted
                };
            }
        }

        private static Dictionary<string, FileSystemEntryChange> GetSourceDelta()
        {
            var delta = new Dictionary<string, FileSystemEntryChange>();
            _database.ExecuteReader(new SqliteCommand("SELECT * FROM events"), reader =>
            {
                while (reader.Read())
                {
                    var timestamp = DateTime.ParseExact(reader.GetString(1), CustomFileInfo.DateTimeFormat, null);
                    var action = reader.GetString(2) switch
                    {
                        "CREATE" => FileSystemEntryAction.Created,
                        "RENAME" => FileSystemEntryAction.Renamed,
                        "CHANGE" => FileSystemEntryAction.Changed,
                        "DELETE" => FileSystemEntryAction.Deleted,
                        _ => throw new ArgumentException("Unsupported action type.")
                    };

                    var path = reader.GetString(3);
                    if (!delta.ContainsKey(path))
                    {
                        var change = new FileSystemEntryChange
                        {
                            Timestamp = timestamp,
                            Action = action,
                        };
                        delta.Add(path, change);
                    }
                    else
                    {
                        var change = delta[path];
                        change.Timestamp = timestamp;
                        FileSystemEntryChange? changeOld;

                        // Currently, we follow this rule: Renamed = Deleted + Created;
                        // in other words ...
                        if (action == FileSystemEntryAction.Renamed)
                        {
                            var json = reader.GetString(4);
                            var props = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

                            // ... delete the file with the old name ...
                            change.Action = FileSystemEntryAction.Deleted;

                            // ... and create the same file with the new name
                            var pathNew = path[..^props!["old_name"].Length] + props["new_name"];
                            if (delta.TryGetValue(pathNew, out changeOld))
                            {
                                // Attempt to rename the file to the already existing one
                                // (i.e. Created or Changed, but not Deleted)
                                //
                                // TODO: consider removing; this must be prevented by the OS itself
                                if (changeOld.Action != FileSystemEntryAction.Deleted)
                                    throw new ArgumentException("Renamed action detected for an already occupied name.");

                                // Created after Deleted = Changed; see below
                                delta[pathNew].Action = FileSystemEntryAction.Changed;
                            }
                            else
                            {
                                // There was no file with that new name, so it can be considered Created
                                delta[pathNew].Action = FileSystemEntryAction.Created;
                            }

                            continue;
                        }

                        if (change.Action == FileSystemEntryAction.Created)
                        {
                            // Any action besides Deleted has no meaning;
                            // the file is effectively new anyway
                            if (action != FileSystemEntryAction.Deleted)
                                continue;

                            // If that new file got deleted, it was temporary
                            delta.Remove(path);
                        }
                        else if (change.Action == FileSystemEntryAction.Renamed)
                        {
                            // Currently, we treat rename = delete + create;
                            // so Renamed must not appear amongst the actions
                            throw new ArgumentException("Renamed action detected.");
                        }
                        else if (change.Action == FileSystemEntryAction.Changed)
                        {
                            // Created after Changed is not possible
                            if (action == FileSystemEntryAction.Created)
                                throw new ArgumentException("Created action detected after Changed.");

                            // Changed but Deleted later -> ok
                            if (action == FileSystemEntryAction.Deleted)
                                change.Action = action;
                        }
                        else if (change.Action == FileSystemEntryAction.Deleted)
                        {
                            // Deleted but Created later -> 2 options:
                            // 1. Same file got restored
                            // 2. Another file was created with the same name
                            //
                            // Either way, instead of checking files equality, we simply treat it as being Changed
                            if (action == FileSystemEntryAction.Created)
                                change.Action = FileSystemEntryAction.Changed;

                            // Anything else after Deleted is not possible
                            else
                                throw new ArgumentException("Invalid action detected after Deleted.");
                        }
                    }
                }
            });
            
            // TODO: order by timestamp
            // return actions.OrderBy(kvp => kvp.Value.Time);
            return delta;
        }

        private static void OnCreated(object sender, FileSystemEventArgs e)
        {
            _tasks.Enqueue(Task.Run(() =>
            {
                // Ignore service files creation
                var subpath = ServiceFilesManager.GetRootSubpath(e.FullPath);
                if (ServiceFilesManager.IsServiceLocation(subpath))
                    return;

                InsertEventEntry("CREATE", subpath, null);
                Notify($"[Create] {e.FullPath}");
            }));
        }

        private static void OnRenamed(object sender, RenamedEventArgs e)
        {
            _tasks.Enqueue(Task.Run(() =>
            {
                var subpath = ServiceFilesManager.GetRootSubpath(e.OldFullPath);
                if (ServiceFilesManager.IsServiceLocation(subpath))
                {
                    // TODO: renaming service files may have unexpected consequences;
                    // revert and/or throw an exception/notification
                    return;
                }

                InsertEventEntry("RENAME", subpath, JsonSerializer.Serialize(new { old_name = e.OldName, new_name = e.Name }));
                Notify($"[Rename] {e.OldFullPath} -> {e.Name}");
            }));
        }

        private static void OnChanged(object sender, FileSystemEventArgs e)
        {
            _tasks.Enqueue(Task.Run(() =>
            {
                // Ignore service files changes; we cannot distinguish user-made changes from software ones 
                var subpath = ServiceFilesManager.GetRootSubpath(e.FullPath);
                if (ServiceFilesManager.IsServiceLocation(subpath))
                    return;

                // Track changes to files only; directory changes are not essential
                var file = new FileInfo(e.FullPath);
                if (file.Attributes.HasFlag(FileAttributes.Directory))
                    return;

                InsertEventEntry("CHANGE", subpath, null);
                Notify($"[Change] {e.FullPath}");
            }));
        }

        private static void OnDeleted(object sender, FileSystemEventArgs e)
        {
            _tasks.Enqueue(Task.Run(() =>
            {
                var subpath = ServiceFilesManager.GetRootSubpath(e.FullPath);
                if (ServiceFilesManager.IsServiceLocation(subpath))
                {
                    // TODO: deleting service files may have unexpected consequences,
                    // and deleting the database means losing the track of all events up to the moment;
                    // revert and/or throw an exception/notification
                    return;
                }

                InsertEventEntry("DELETE", subpath, null);
                Notify($"[Delete] {e.FullPath}");
            }));
        }

        private static void OnError(object sender, ErrorEventArgs e)
        {
            _tasks.Enqueue(Task.Run(() =>
            {
                var ex = e.GetException();
                Notify($"Message: {ex.Message}\nStacktrace: {ex.StackTrace}\n");
            }));
        }

        private static void InsertEventEntry(string type, string path, string? misc)
        {
            var command = new SqliteCommand("INSERT INTO events VALUES (:time, :type, :path, :misc)");
            command.Parameters.AddWithValue(":time", DateTime.Now.ToString(CustomFileInfo.DateTimeFormat));
            command.Parameters.AddWithValue(":type", type);
            command.Parameters.AddWithValue(":path", path);
            command.Parameters.AddWithValue(":misc", misc != null ? misc : DBNull.Value);
            _database.ExecuteNonQuery(command);
        }

        private static void Notify(string message)
        {
#if DEBUG
            Console.WriteLine(message);
#else
            // No listener to receive messages
            if (!_pipeServerOut.IsConnected)
                return;

            try
            {
                _pipeServerOut.WriteString(message);
            }
            catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException or IOException)
            {
                // Listener got closed -> reset and wait for a new one
                _pipeServerOut.Disconnect();
                _pipeServerOut.BeginWaitForConnection(s =>
                {
                    // TODO: replace with logging or remove
                    Console.WriteLine("Client connected!");
                }, null);
            }
#endif
        }
    }
}