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
using System.ComponentModel.DataAnnotations;

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

                var delta = GetTargetDelta(targetRoot);

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
            Console.WriteLine($"Awaiting tasks: {tasksCount}");
            for (int i = 0; i < tasksCount; i++)
            {
                _tasks.TryDequeue(out var task);
                task!.RunSynchronously();
            }

            Console.Write($"Resolving target changes... ");
            var targetDelta = GetTargetDelta(targetRoot);
            Console.WriteLine($"Total: {targetDelta.Count}");

            Console.Write($"Resolving source changes... ");
            var sourceDelta = GetSourceDelta();
            Console.WriteLine($"Total: {sourceDelta.Count}");

            var appliedDelta = new List<(string, FileSystemEntryChange)>();
            var failedDelta = new List<(string, FileSystemEntryChange)>();
            Console.Write("Target latest sync timestamp: ");
            var lastSyncTimestamp = GetTargetLastSyncTimestamp(targetRoot);
            Console.WriteLine(lastSyncTimestamp != null ? lastSyncTimestamp.Value.ToString(CustomFileInfo.DateTimeFormat) : "N/A");

            void TryApplyChange(string sourceRoot, string targetRoot, string subpath, FileSystemEntryChange change)
            {
                // TODO: use database instead of in-memory dicts
                var status = ApplyChange(sourceRoot, targetRoot, subpath, change);
                if (status)
                    appliedDelta!.Add((subpath, change));
                else
                    failedDelta!.Add((subpath, change));
                Console.WriteLine($"Status: {(status ? "applied" : "failed")}");
            }

            // Both source and target deltas have to be enumerated in an insertion order;
            // otherwise Created directory contents can get scheduled before that directory creation.
            // 
            // targetDelta is sorted by default, as it is a List.
            foreach (var (targetEntry, targetChange) in targetDelta)
            {
                if (sourceDelta.Remove(targetEntry, out var sourceEntry))
                {
                    // Entry change is present on both source and target
                    // -> determine which of two changes is actual
                    Console.Write($"Common: {targetEntry}; Strategy: ");
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
                        Console.Write("S->T; ");
                        TryApplyChange(ServiceFilesManager.Root, targetRoot, targetEntry, sourceEntry);
                    }
                    else
                    {
                        // Target is actual
                        Console.Write("T->S; ");
                        TryApplyChange(targetRoot, ServiceFilesManager.Root, targetEntry, targetChange);
                    }
                }
                else
                {
                    // Entry change is present only on target
                    // -> propagate the change to source
                    Console.Write($"From target: {targetEntry}; ");
                    TryApplyChange(targetRoot, ServiceFilesManager.Root, targetEntry, targetChange);
                }
            }

            // The remaining entries in sourceDelta have to be sorted by timestamp, same as insertion order.
            foreach (var sourceEntry in sourceDelta.OrderBy(kvp => kvp.Value.Timestamp))
            {
                // Entry change is present only on source
                // -> propagate the change to target
                Console.Write($"From source: {sourceEntry.Key}; ");
                TryApplyChange(ServiceFilesManager.Root, targetRoot, sourceEntry.Key, sourceEntry.Value);
            }

            //Console.WriteLine($"Applied changes:");
            //foreach (var appliedChange in appliedDelta)
            //    Console.WriteLine(appliedChange);
            Console.WriteLine($"Total changes applied: {appliedDelta.Count}.\n");

            // Generate the new index based on the old one, according to the rule:
            // s(d(S_0) + d(ΔS)) = S_0 + ΔS
            // where s(x) and d(x) stand for serialization and deserialization routines resp
            var index = CustomDirectoryInfo.Deserialize(ServiceFilesManager.IndexPath);
            index.MergeChanges(appliedDelta);
            index.Serialize(ServiceFilesManager.IndexPath);

            //Console.WriteLine($"Failed to apply changes:");
            //foreach (var failedChange in failedDelta)
            //    Console.WriteLine(failedChange);
            Console.WriteLine($"Total changes failed: {failedDelta.Count}.");

            // TODO: propose possible workarounds for failed changes

            Console.WriteLine("Synchronization complete.");
        }

        private static bool ApplyChange(string sourceRoot, string targetRoot, string subpath, FileSystemEntryChange change)
        {
            Console.WriteLine($"{sourceRoot} -> {targetRoot}: [{change.Action}] {subpath})");
            var sourcePath = Path.Combine(sourceRoot, subpath);
            var targetPath = Path.Combine(targetRoot, subpath);
            switch (change.Action)
            {
                case FileSystemEntryAction.Create:
                    
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
                        var createdProperties = change.Properties.ChangeProps;
                        if (createdSourceFileInfo.LastWriteTime != createdProperties.LastWriteTime ||
                            createdSourceFileInfo.Length != createdProperties.Length)
                            return false;

                        File.Copy(sourcePath, targetPath);
                    }
                    break;
                case FileSystemEntryAction.Change:
                    // If the entry is not present, the change is invalid
                    var changedSourceFileInfo = new FileInfo(sourcePath);
                    if (!changedSourceFileInfo.Exists)
                        return false;

                    // If the entry differs, the change is invalid
                    var changedProperties = change.Properties.ChangeProps;
                    if (changedSourceFileInfo.LastWriteTime != changedProperties.LastWriteTime ||
                        changedSourceFileInfo.Length != changedProperties.Length)
                        return false;

                    // Note: Changed action must not appear for a directory
                    File.Copy(sourcePath, targetPath, overwrite: true);
                    break;
                case FileSystemEntryAction.Delete:
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
            try
            {
                targetDatabase.ExecuteReader(new SqliteCommand("SELECT * FROM sync ORDER BY time DESC LIMIT 1"), reader =>
                {
                    if (reader.Read())
                        result = DateTime.ParseExact(reader.GetString(1), CustomFileInfo.DateTimeFormat, null);
                });
            }
            catch (SqliteException ex)
            {
                
            }
            return result;
        }

        private static List<(string, FileSystemEntryChange)> GetTargetDelta(string targetRoot)
        {
            var sourceDir = CustomDirectoryInfo.DeserializeRoot();  // last synced source data
            var targetDir = new DirectoryInfo(targetRoot);
            var builder = new StringBuilder();
            var delta = new List<(string, FileSystemEntryChange)>();
            GetTargetDelta(sourceDir, targetDir, builder, delta);
            return delta;
        }

        private static void GetTargetDelta(CustomDirectoryInfo sourceDir, DirectoryInfo targetDir, StringBuilder builder, List<(string, FileSystemEntryChange)> delta)
        {
            foreach (var targetSubdir in builder.Wrap(targetDir.EnumerateDirectories(), d => d.Name + Path.DirectorySeparatorChar))
            {
                var subpath = ServiceFilesManager.GetRootSubpath(targetSubdir.FullName);
                if (ServiceFilesManager.IsServiceLocation(subpath))
                    continue;

                if (sourceDir.Directories.Remove(targetSubdir.Name, out var sourceSubdir))
                {
                    GetTargetDelta(sourceSubdir, targetSubdir, builder, delta);
                }
                else
                {
                    // TODO: add created directory contents to delta?
                    delta.Add((builder.ToString(), new FileSystemEntryChange
                    {
                        Action = FileSystemEntryAction.Create,
                    }));
                }
            }

            foreach (var _ in builder.Wrap(sourceDir.Directories, kvp => kvp.Key))
            {
                delta.Add((builder.ToString(), new FileSystemEntryChange
                {
                    Action = FileSystemEntryAction.Delete
                }));
            }

            foreach (var targetFile in builder.Wrap(targetDir.EnumerateFiles(), f => f.Name))
            {
                if (sourceDir.Files.Remove(targetFile.Name, out var sourceFile))
                {
                    if (targetFile.LastWriteTime.TrimMicroseconds() != sourceFile.LastWriteTime || targetFile.Length != sourceFile.Length)
                    {
                        delta.Add((builder.ToString(), new FileSystemEntryChange
                        {
                            Action = FileSystemEntryAction.Change
                        }));
                    }
                }
                else
                {
                    delta.Add((builder.ToString(), new FileSystemEntryChange
                    {
                        Action = FileSystemEntryAction.Create
                    }));
                }
            }

            foreach (var _ in builder.Wrap(sourceDir.Files, kvp => kvp.Key))
            {
                delta.Add((builder.ToString(), new FileSystemEntryChange
                {
                    Action = FileSystemEntryAction.Delete
                }));
            }
        }

        private static Dictionary<string, FileSystemEntryChange> GetSourceDelta()
        {
            var delta = new Dictionary<string, FileSystemEntryChange>();
            _database.ExecuteReader(new SqliteCommand("SELECT * FROM events"), reader =>
            {
                while (reader.Read())
                {
                    var timestamp = DateTime.ParseExact(reader.GetString(0), CustomFileInfo.DateTimeFormat, null);
                    var action = FileSystemEntryUtils.StringToAction(reader.GetString(1));
                    var path = reader.GetString(2);
                    var json = reader.IsDBNull(3) ? null : reader.GetString(3);
                    var isDirectory = path.EndsWith(Path.DirectorySeparatorChar);
                    if (!delta.ContainsKey(path))
                    {
                        var change = new FileSystemEntryChange
                        {
                            Timestamp = timestamp,
                            Action = action == FileSystemEntryAction.Rename ? FileSystemEntryAction.Change : action
                        };
                        if (json != null)
                        {
                            switch (action)
                            {
                                case FileSystemEntryAction.Rename:
                                    change.Properties.RenameProps = ActionProps.Deserialize<RenameProps>(json);
                                    break;
                                case FileSystemEntryAction.Create:
                                case FileSystemEntryAction.Change:
                                    change.Properties.ChangeProps = ActionProps.Deserialize<ChangeProps>(json);
                                    break;
                            }
                        }
                        delta.Add(path, change);
                    }
                    else
                    {
                        var change = delta[path];
                        change.Timestamp = timestamp;
                        FileSystemEntryChange? changeOld;

                        // Currently, we follow this rule: Renamed = Deleted + Created;
                        // in other words ...
                        //if (action == FileSystemEntryAction.Rename)
                        //{
                        //    var props = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

                        //    // ... delete the file with the old name ...
                        //    change.Action = FileSystemEntryAction.Delete;

                        //    // ... and create the same file with the new name
                        //    var pathNew = path[..^props!["old_name"].Length] + props["new_name"];
                        //    if (delta.TryGetValue(pathNew, out changeOld))
                        //    {
                        //        // Attempt to rename the file to the already existing one
                        //        // (i.e. Created or Changed, but not Deleted)
                        //        //
                        //        // TODO: consider removing; this must be prevented by the OS itself
                        //        if (changeOld.Action != FileSystemEntryAction.Delete)
                        //            throw new ArgumentException("Renamed action detected for an already occupied name.");

                        //        // Created after Deleted = Changed; see below
                        //        delta[pathNew].Action = FileSystemEntryAction.Change;
                        //    }
                        //    else
                        //    {
                        //        // There was no file with that new name, so it can be considered Created
                        //        delta[pathNew].Action = FileSystemEntryAction.Create;
                        //    }

                        //    continue;
                        //}

                        if (change.Action == FileSystemEntryAction.Create)
                        {
                            switch (action)
                            {
                                // Create again -> duplicate
                                case FileSystemEntryAction.Create:
                                    throw new ArgumentException("Duplicate action detected.");

                                // Rename after Create -> entry is still new, update rename properties only
                                case FileSystemEntryAction.Rename:
                                    change.Properties.RenameProps = ActionProps.Deserialize<RenameProps>(json);
                                    break;

                                // Change after Create -> entry is still new, update change properties only
                                case FileSystemEntryAction.Change:
                                    change.Properties.ChangeProps = ActionProps.Deserialize<ChangeProps>(json);
                                    break;

                                // Delete after Create -> temporary entry, no need to track it
                                case FileSystemEntryAction.Delete:
                                    delta.Remove(path);
                                    break;
                            }
                        }
                        //else if (change.Action == FileSystemEntryAction.Rename)
                        //{
                        //    // Currently, we treat rename = delete + create;
                        //    // so Renamed must not appear amongst the actions
                        //    throw new ArgumentException("Renamed action detected.");
                        //}
                        else if (change.Action == FileSystemEntryAction.Change)
                        {
                            switch (action)
                            {
                                // Create after Change -> impossible
                                case FileSystemEntryAction.Create:
                                    throw new ArgumentException("Created action detected after Changed.");

                                // Rename after Change -> entry is still changed, update rename properties only
                                case FileSystemEntryAction.Rename:
                                    change.Properties.RenameProps = ActionProps.Deserialize<RenameProps>(json);
                                    break;

                                // Change again -> update change properties only
                                case FileSystemEntryAction.Change:
                                    change.Properties.ChangeProps = ActionProps.Deserialize<ChangeProps>(json);
                                    break;

                                // Delete after Change -> ok
                                case FileSystemEntryAction.Delete:
                                    change.Action = action;
                                    change.Properties.RenameProps = null;
                                    change.Properties.ChangeProps = null;
                                    break;
                            }
                        }
                        else if (change.Action == FileSystemEntryAction.Delete)
                        {
                            switch (action)
                            {
                                // Create after Delete -> 2 options:
                                // 1. Same file got restored
                                // 2. Another file was created with the same name
                                //
                                // Either way, instead of checking files equality, we simply treat it as being Changed
                                case FileSystemEntryAction.Create:
                                    change.Action = FileSystemEntryAction.Change;
                                    change.Properties.ChangeProps = ActionProps.Deserialize<ChangeProps>(json);
                                    break;

                                // Rename or Change after Delete -> impossible
                                case FileSystemEntryAction.Rename:
                                case FileSystemEntryAction.Change:
                                    throw new ArgumentException("Invalid action sequence detected.");

                                // Delete again -> duplicate
                                case FileSystemEntryAction.Delete:
                                    throw new ArgumentException("Duplicate action detected.");
                            }
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
                    await OnDirectoryCreated(new DirectoryInfo(e.FullPath));
                }
                else
                {
                    // TODO: consider switching to CreateProps w/ CreationTime property
                    var properties = new ChangeProps
                    {
                        LastWriteTime = file.LastWriteTime,
                        Length = file.Length
                    };
                    var action = FileSystemEntryUtils.ActionToString(FileSystemEntryAction.Create);
                    InsertEventEntry(action, subpath, misc: properties);
                    await WriteOutput($"[{action}] {e.FullPath}");
                }
            }));
        }

        private static async Task OnDirectoryCreated(DirectoryInfo root)
        {
            // Using a separator in the end of a directory name helps distinguishing file creation VS directory creation
            var rootPath = root.FullName + Path.DirectorySeparatorChar;
            var action = FileSystemEntryUtils.ActionToString(FileSystemEntryAction.Create);
            InsertEventEntry(action, ServiceFilesManager.GetRootSubpath(rootPath));
            await WriteOutput($"[{action}] {rootPath}");

            foreach (var directory in root.EnumerateDirectories())
            {
                await OnDirectoryCreated(directory);
            }

            foreach (var file in root.EnumerateFiles())
            {
                var properties = new ChangeProps
                {
                    LastWriteTime = file.LastWriteTime,
                    Length = file.Length
                };
                InsertEventEntry(action, ServiceFilesManager.GetRootSubpath(file.FullName), misc: properties);
                await WriteOutput($"[{action}] {file.FullName}");
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
                //ActionProperties properties = file.Attributes.HasFlag(FileAttributes.Directory) ? 
                //    new DirectoryActionProperties
                //    {
                //        Name = e.Name
                //    } : 
                //    new FileActionProperties
                //    {
                //        Name = e.Name,
                //        LastWriteTime = file.LastWriteTime,
                //        Length = file.Length
                //    };
                var properties = new RenameProps
                {
                    Name = e.Name
                };
                var action = FileSystemEntryUtils.ActionToString(FileSystemEntryAction.Rename);
                InsertEventEntry(action, subpath, misc: properties);
                await WriteOutput($"[{action}] {e.OldFullPath} -> {e.Name}");
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

                var properties = new ChangeProps
                {
                    LastWriteTime = file.LastWriteTime,
                    Length = file.Length
                };
                var action = FileSystemEntryUtils.ActionToString(FileSystemEntryAction.Change);
                InsertEventEntry(action, subpath, misc: properties);
                await WriteOutput($"[{action}] {e.FullPath}");
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

                var action = FileSystemEntryUtils.ActionToString(FileSystemEntryAction.Delete);
                InsertEventEntry(action, subpath);
                await WriteOutput($"[{action}] {e.FullPath}");
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

        private static void InsertEventEntry(string type, string path, DateTime? time = null, ActionProps? misc = null)
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
            command.Parameters.AddWithValue(":misc", misc != null ? ActionProps.Serialize(misc) : DBNull.Value);
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