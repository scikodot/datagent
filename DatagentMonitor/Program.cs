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
        private static readonly ConcurrentQueue<Task> _tasks = new();

        static async Task Main(string[] args)
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
                if (!File.Exists(ServiceFilesManager.IndexPath))
                    CustomDirectoryInfo.SerializeRoot();

                var targetRoot = Path.Combine("D:", "_target");

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

                // TODO: consider moving pipe management to MonitorUtils or somewhere else
                _pipeServerIn = new NamedPipeServerStream(MonitorUtils.InputPipeServerName, PipeDirection.In, 1, 
                    PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly | PipeOptions.WriteThrough | PipeOptions.Asynchronous);
                _pipeServerOut = new NamedPipeServerStream(MonitorUtils.OutputPipeServerName, PipeDirection.Out, 1, 
                    PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly | PipeOptions.WriteThrough | PipeOptions.Asynchronous);

                AppDomain.CurrentDomain.ProcessExit += (s, e) =>
                {
                    _pipeServerIn.Close();
                    _pipeServerOut.Close();
                    // TODO: log status
                };

                bool up = true;
                while (up)
                {
                    // Remove completed tasks up until the first uncompleted
                    while (_tasks.TryPeek(out var task) && task.IsCompleted)
                        _tasks.TryDequeue(out _);

                    // Wait for connection for some time, continue with the main loop if no response
                    var result = await _pipeServerIn.WaitForConnectionSafeAsync(milliseconds: 15000);
                    Console.WriteLine($"Result: {result}");
                    var input = await _pipeServerIn.ReadStringSafeAsync();
                    if (input == null)
                        continue;

                    Console.WriteLine($"Received: {input}");
                    switch (input)
                    {
                        case "SYNC":
                            Synchronize(targetRoot);
                            break;
                        case "DROP":
                            up = false;
                            Console.WriteLine("Shutting down...");
                            break;
                    }
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

        // TODO: use OrderedDict's or order by timestamps
        private static void Synchronize(string targetRoot)
        {
            // Wait for all queued tasks to complete before syncing
            int tasksCount = _tasks.Count;
            for (int i = 0; i < tasksCount; i++)
            {
                _tasks.TryDequeue(out var task);
                task!.RunSynchronously();
            }

            var targetDelta = GetTargetDelta(targetRoot);
            Console.WriteLine($"Target changes: {targetDelta.Count}");
            var sourceDelta = GetSourceDelta();
            Console.WriteLine($"Source changes: {sourceDelta.Count}");
            var appliedDelta = new Dictionary<string, FileSystemEntryChange>();
            var failedDelta = new Dictionary<string, FileSystemEntryChange>();
            var lastSyncTimestamp = GetTargetLastSyncTimestamp(targetRoot);

            void TryApplyChange(string sourceRoot, string targetRoot, string subpath, FileSystemEntryChange change)
            {
                // TODO: use database instead of in-memory dicts
                if (ApplyChange(sourceRoot, targetRoot, subpath, change))
                    appliedDelta!.Add(subpath, change);
                else
                    failedDelta!.Add(subpath, change);
            }

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
                        TryApplyChange(ServiceFilesManager.Root, targetRoot, targetEntry.Key, sourceEntry);
                    }
                    else
                    {
                        // Target is actual
                        TryApplyChange(targetRoot, ServiceFilesManager.Root, targetEntry.Key, targetEntry.Value);
                    }
                }
                else
                {
                    // Entry change is present only on target
                    // -> propagate the change to source
                    TryApplyChange(targetRoot, ServiceFilesManager.Root, targetEntry.Key, targetEntry.Value);
                }
            }

            foreach (var sourceEntry in sourceDelta)
            {
                // Entry change is present only on source
                // -> propagate the change to target
                TryApplyChange(ServiceFilesManager.Root, targetRoot, sourceEntry.Key, sourceEntry.Value);
            }

            Console.WriteLine("Synchronization complete.");

            Console.WriteLine($"Applied changes: {appliedDelta.Count}");

            // Generate the new index based on the old one, according to the rule:
            // s(d(S_0) + d(ΔS)) = S_0 + ΔS
            // where s(x) and d(x) stand for serialization and deserialization routines resp
            var index = CustomDirectoryInfo.Deserialize(ServiceFilesManager.IndexPath);
            index.MergeChanges(appliedDelta);
            index.Serialize(ServiceFilesManager.IndexPath);

            Console.WriteLine($"Failed to apply changes: {failedDelta.Count}");

            // TODO: show failed changes and propose possible workarounds
        }

        private static bool ApplyChange(string sourceRoot, string targetRoot, string subpath, FileSystemEntryChange change)
        {
            Console.WriteLine($"{sourceRoot} -> {targetRoot}: [{change.Action}] {subpath})");
            var sourcePath = Path.Combine(sourceRoot, subpath);
            var targetPath = Path.Combine(targetRoot, subpath);
            switch (change.Action)
            {
                case FileSystemEntryAction.Created:
                    
                    // If the entry is not present, the change is invalid
                    var createdSourceFileInfo = new FileInfo(sourcePath);
                    if (!createdSourceFileInfo.Exists)
                        return false;

                    if (createdSourceFileInfo.Attributes.HasFlag(FileAttributes.Directory))
                    {
                        // Note: directory creation does not require contents comparison,
                        // as all contents are written as separate entries in database.
                        //
                        // See OnDirectoryCreated method
                        Directory.CreateDirectory(sourcePath);
                        //DirectoryExtensions.Copy(sourcePath, targetPath);
                    }
                    else
                    {
                        // If the entry differs, the change is invalid
                        var createdProperties = change.Properties as FileActionProperties;
                        if (createdSourceFileInfo.LastWriteTime != createdProperties.LastWriteTime ||
                            createdSourceFileInfo.Length != createdProperties.Length)
                            return false;

                        File.Copy(sourcePath, targetPath);
                    }
                    break;
                case FileSystemEntryAction.Changed:
                    // If the entry is not present, the change is invalid
                    var changedSourceFileInfo = new FileInfo(sourcePath);
                    if (!changedSourceFileInfo.Exists)
                        return false;

                    // If the entry differs, the change is invalid
                    var changedProperties = change.Properties as FileActionProperties;
                    if (changedSourceFileInfo.LastWriteTime != changedProperties.LastWriteTime ||
                        changedSourceFileInfo.Length != changedProperties.Length)
                        return false;

                    // Note: Changed action must not appear for a directory
                    File.Copy(sourcePath, targetPath, overwrite: true);
                    break;
                case FileSystemEntryAction.Deleted:
                    // If the source entry is not deleted, the change is invalid
                    var deletedSourceFileInfo = new FileInfo(sourcePath);
                    if (!deletedSourceFileInfo.Exists)
                        return false;

                    // If the target entry is not present, the change needs not to be applied
                    var deletedTargetFileInfo = new FileInfo(targetPath);
                    if (!deletedTargetFileInfo.Exists)
                        return true;

                    if (deletedTargetFileInfo.Attributes.HasFlag(FileAttributes.Directory))
                        Directory.Delete(targetPath, true);
                    else
                        File.Delete(targetPath);
                    break;
            }

            return true;
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
                    // TODO: add created directory contents to delta?
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
            _tasks.Enqueue(new Task(async () =>
            {
                // Ignore service files creation
                var subpath = ServiceFilesManager.GetRootSubpath(e.FullPath);
                if (ServiceFilesManager.IsServiceLocation(subpath))
                    return;

                var file = new FileInfo(e.FullPath);
                if (file.Attributes.HasFlag(FileAttributes.Directory))
                {
                    var properties = new FileActionProperties
                    {
                        Name = e.Name,
                        LastWriteTime = file.LastWriteTime,
                        Length = file.Length
                    };
                    InsertEventEntry("CREATE", subpath, misc: properties);
                    await WriteOutput($"[Create] {e.FullPath}");
                }
                else
                {
                    await OnDirectoryCreated(new DirectoryInfo(e.FullPath));
                }
            }));
        }

        private static async Task OnDirectoryCreated(DirectoryInfo root)
        {
            // Using a separator in the end of a directory name helps distinguishing file creation VS directory creation
            var rootPath = root.FullName + Path.DirectorySeparatorChar;
            InsertEventEntry("CREATE", ServiceFilesManager.GetRootSubpath(rootPath));
            await WriteOutput($"[Create] {rootPath}");

            foreach (var directory in root.EnumerateDirectories())
            {
                await OnDirectoryCreated(directory);
            }

            foreach (var file in root.EnumerateFiles())
            {
                InsertEventEntry("CREATE", ServiceFilesManager.GetRootSubpath(file.FullName));
                await WriteOutput($"[Create] {file.FullName}");
            }
        }

        private static void OnRenamed(object sender, RenamedEventArgs e)
        {
            _tasks.Enqueue(new Task(async () =>
            {
                var subpath = ServiceFilesManager.GetRootSubpath(e.OldFullPath);
                if (ServiceFilesManager.IsServiceLocation(subpath))
                {
                    // TODO: renaming service files may have unexpected consequences;
                    // revert and/or throw an exception/notification
                    return;
                }

                var file = new FileInfo(e.FullPath);
                ActionProperties properties = file.Attributes.HasFlag(FileAttributes.Directory) ? 
                    new DirectoryActionProperties
                    {
                        Name = e.Name
                    } : 
                    new FileActionProperties
                    {
                        Name = e.Name,
                        LastWriteTime = file.LastWriteTime,
                        Length = file.Length
                    };
                InsertEventEntry("RENAME", subpath, misc: properties);
                await WriteOutput($"[Rename] {e.OldFullPath} -> {e.Name}");
            }));
        }

        private static void OnChanged(object sender, FileSystemEventArgs e)
        {
            _tasks.Enqueue(new Task(async () =>
            {
                // Ignore service files changes; we cannot distinguish user-made changes from software ones 
                var subpath = ServiceFilesManager.GetRootSubpath(e.FullPath);
                if (ServiceFilesManager.IsServiceLocation(subpath))
                    return;

                // Track changes to files only; directory changes are not essential
                var file = new FileInfo(e.FullPath);
                if (file.Attributes.HasFlag(FileAttributes.Directory))
                    return;

                var properties = new FileActionProperties
                {
                    Name = e.Name,
                    LastWriteTime = file.LastWriteTime,
                    Length = file.Length
                };

                InsertEventEntry("CHANGE", subpath, misc: properties);
                await WriteOutput($"[Change] {e.FullPath}");
            }));
        }

        private static void OnDeleted(object sender, FileSystemEventArgs e)
        {
            _tasks.Enqueue(new Task(async () =>
            {
                var subpath = ServiceFilesManager.GetRootSubpath(e.FullPath);
                if (ServiceFilesManager.IsServiceLocation(subpath))
                {
                    // TODO: deleting service files may have unexpected consequences,
                    // and deleting the database means losing the track of all events up to the moment;
                    // revert and/or throw an exception/notification
                    return;
                }

                InsertEventEntry("DELETE", subpath);
                await WriteOutput($"[Delete] {e.FullPath}");
            }));
        }

        private static void OnError(object sender, ErrorEventArgs e)
        {
            Task.Run(async () =>
            {
                var ex = e.GetException();
                await WriteOutput($"Message: {ex.Message}\nStacktrace: {ex.StackTrace}\n");
            });
        }

        private static void InsertEventEntry(string type, string path, DateTime? time = null, ActionProperties? misc = null)
        {
            // Misc parameter is a JSON with additional properties relevant to the performed operation:
            // CREATE FILE -> NULL
            // CREATE DIRECTORY -> { "contents": ... }
            // RENAME FILE -> { "old_name": ..., "new_name": ... }
            // RENAME DIRECTORY -> { "old_name": ..., "new_name": ... }
            // CHANGE FILE -> { "old_length": ..., "new_length": ... } (?)
            // (CHANGE DIRECTORY is not used)
            // DELETE FILE -> NULL
            // DELETE DIRECTORY -> NULL
            var command = new SqliteCommand("INSERT INTO events VALUES (:time, :type, :path, :misc)");
            command.Parameters.AddWithValue(":time", time != null ? time: DateTime.Now.ToString(CustomFileInfo.DateTimeFormat));
            command.Parameters.AddWithValue(":type", type);
            command.Parameters.AddWithValue(":path", path);
            command.Parameters.AddWithValue(":misc", misc != null ? misc.Serialize() : DBNull.Value);
            _database.ExecuteNonQuery(command);
        }

        private static async Task WriteOutput(string message)
        {
#if DEBUG
            Console.WriteLine($"[Out] {message}");
#endif
            var tokenSource = new CancellationTokenSource(4000);
            await _pipeServerOut.WaitForConnectionAsync(tokenSource.Token);
            await _pipeServerOut.WriteStringSafeAsync(message);
        }
    }
}