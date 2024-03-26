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
using System.Net.Http.Headers;
using System;

namespace DatagentMonitor;

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
        var sourceName = change.Properties.RenameProps?.Name;
        var sourcePath = Path.Combine(sourceRoot, sourceName == null ? subpath : ReplaceName(subpath, sourceName));
        var targetPath = Path.Combine(targetRoot, subpath);
        switch (change.Action)
        {
            case FileSystemEntryAction.Create:
                var createdSourceFileInfo = new FileInfo(sourcePath);

                // Entry is not present -> the change is invalid
                if (!createdSourceFileInfo.Exists)
                    return false;

                if (createdSourceFileInfo.Attributes.HasFlag(FileAttributes.Directory))
                {
                    // Note: directory creation does not require contents comparison,
                    // as all contents are written as separate entries in database.
                    //
                    // See OnDirectoryCreated method
                    Directory.CreateDirectory(sourcePath);
                }
                else
                {
                    var createdProperties = change.Properties.ChangeProps;

                    // Entry differs -> the change is invalid
                    if (createdSourceFileInfo.LastWriteTime != createdProperties.LastWriteTime ||
                        createdSourceFileInfo.Length != createdProperties.Length)
                        return false;

                    File.Copy(sourcePath, targetPath);
                }
                break;

            case FileSystemEntryAction.Change:
                var changedSourceFileInfo = new FileInfo(sourcePath);

                // Entry is not present -> the change is invalid
                if (!changedSourceFileInfo.Exists)
                    return false;

                var changedProperties = change.Properties.ChangeProps;

                // Entry differs -> the change is invalid
                if (changedSourceFileInfo.LastWriteTime != changedProperties.LastWriteTime ||
                    changedSourceFileInfo.Length != changedProperties.Length)
                    return false;

                // Note: Change action must not appear for a directory
                File.Copy(sourcePath, targetPath, overwrite: true);
                break;

            case FileSystemEntryAction.Delete:
                var deletedSourceFileInfo = new FileInfo(sourcePath);

                // Source entry is not deleted -> the change is invalid
                if (!deletedSourceFileInfo.Exists)
                    return false;

                var deletedTargetFileInfo = new FileInfo(targetPath);

                // Target entry is not present -> the change needs not to be applied
                if (!deletedTargetFileInfo.Exists)
                    return true;

                if (deletedTargetFileInfo.Attributes.HasFlag(FileAttributes.Directory))
                    Directory.Delete(targetPath, true);
                else
                    File.Delete(targetPath);
                break;
        }

        // Apply rename to target if necessary
        if (sourceName != null)
            File.Move(targetPath, ReplaceName(targetPath, sourceName));

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
        // Note:
        // There is no way to determine whether a file was renamed on target if that info is not present.
        // Therefore, every such change is split into Delete + Create sequence. It works, but is suboptimal.
        //
        // The only workaround is to compare files not by names but by content hashes:
        // - file was changed -> its hash is changed -> copy file
        // - file wasn't changed -> its hash isn't changed -> don't copy file, only compare metadata (names, times, etc.)
        //
        // TODO: consider comparing files by content hashes
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
                OnDirectoryCreated(targetSubdir, builder, delta);
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

    private static void OnDirectoryCreated(DirectoryInfo root, StringBuilder builder, List<(string, FileSystemEntryChange)> delta)
    {
        delta.Add((builder.ToString(), new FileSystemEntryChange
        {
            Action = FileSystemEntryAction.Create,
        }));

        foreach (var directory in builder.Wrap(root.EnumerateDirectories(), d => d.Name + Path.DirectorySeparatorChar))
        {
            OnDirectoryCreated(directory, builder, delta);
        }

        foreach (var file in builder.Wrap(root.EnumerateFiles(), f => f.Name))
        {
            var change = new FileSystemEntryChange
            {
                Action = FileSystemEntryAction.Create
            };
            change.Properties.ChangeProps = new ChangeProps
            {
                LastWriteTime = file.LastWriteTime,
                Length = file.Length
            };
            delta.Add((builder.ToString(), change));
        }
    }

    private static Dictionary<string, FileSystemEntryChange> GetSourceDelta()
    {
        var delta = new Dictionary<string, FileSystemEntryChange>();
        var names = new Dictionary<string, string>();
        _database.ExecuteReader(new SqliteCommand("SELECT * FROM events"), reader =>
        {
            while (reader.Read())
            {
                var timestamp = DateTime.ParseExact(reader.GetString(0), CustomFileInfo.DateTimeFormat, null);
                var action = FileSystemEntryUtils.StringToAction(reader.GetString(1));
                var path = reader.GetString(2);
                var json = reader.IsDBNull(3) ? null : reader.GetString(3);
                var isDirectory = path.EndsWith(Path.DirectorySeparatorChar);

                if (names.ContainsKey(path))
                {
                    switch (action)
                    {
                        case FileSystemEntryAction.Rename:
                            var properties = ActionProps.Deserialize<RenameProps>(json);

                            // 1. Update the original entry to include the new name
                            var orig = names[path];
                            var origNew = string.Join('|', orig.Split('|')[0], properties.Name);
                            delta.Remove(orig, out var value);
                            delta.Add(origNew, value);

                            // 2. Update the reference to the original entry
                            names.Remove(path);
                            names.Add(ReplaceName(path, properties.Name), origNew);
                            continue;
                    }

                    // Continue processing the original entry
                    path = names[path];
                }
                    
                if (delta.ContainsKey(path))
                {
                    var change = delta[path];
                    change.Timestamp = timestamp;
                    switch (action)
                    {
                        case FileSystemEntryAction.Create:
                            switch (change.Action)
                            {
                                // Create after Create or Rename or Change -> impossible
                                case FileSystemEntryAction.Create:
                                case FileSystemEntryAction.Rename:
                                case FileSystemEntryAction.Change:
                                    throw new InvalidActionSequenceException(change.Action, action);

                                // Create after Delete -> 2 options:
                                // 1. Same file got restored
                                // 2. Another file was created with the same name
                                //
                                // Either way, instead of checking files equality, we simply treat it as being changed.
                                case FileSystemEntryAction.Delete:
                                    change.Action = FileSystemEntryAction.Change;
                                    change.Properties.ChangeProps = ActionProps.Deserialize<ChangeProps>(json);
                                    break;
                            }
                            break;
                        case FileSystemEntryAction.Rename:
                            switch (change.Action)
                            {
                                // Rename after Create or Change -> ok
                                case FileSystemEntryAction.Create:
                                case FileSystemEntryAction.Change:
                                    var properties = ActionProps.Deserialize<RenameProps>(json);
                                    change.Properties.RenameProps = properties;

                                    // Update the original entry
                                    var orig = string.Join('|', path, properties.Name);
                                    delta.Remove(path, out var value);
                                    delta.Add(orig, value);

                                    // Create the reference to the original entry
                                    names.Add(ReplaceName(path, properties.Name), orig);
                                    break;

                                // Rename again -> processed earlier, must not appear here
                                // Rename after Delete -> impossible
                                case FileSystemEntryAction.Rename:
                                case FileSystemEntryAction.Delete:
                                    throw new InvalidActionSequenceException(change.Action, action);
                            }
                            break;
                        case FileSystemEntryAction.Change:
                            switch (change.Action)
                            {
                                // Change after Create -> ok but keep previous action
                                case FileSystemEntryAction.Create:
                                    change.Properties.ChangeProps = ActionProps.Deserialize<ChangeProps>(json);
                                    break;
                                    
                                // Change after Rename or Change -> ok
                                case FileSystemEntryAction.Rename:
                                case FileSystemEntryAction.Change:
                                    change.Action = action;
                                    change.Properties.ChangeProps = ActionProps.Deserialize<ChangeProps>(json);
                                    break;

                                // Change after Delete -> impossible
                                case FileSystemEntryAction.Delete:
                                    throw new InvalidActionSequenceException(change.Action, action);
                            }
                            break;
                        case FileSystemEntryAction.Delete:
                            switch (change.Action)
                            {
                                // Delete after Create -> temporary entry, no need to track it
                                case FileSystemEntryAction.Create:
                                    delta.Remove(path);
                                    break;

                                // Delete after Rename or Change -> ok
                                case FileSystemEntryAction.Rename:
                                case FileSystemEntryAction.Change:
                                    change.Action = action;
                                    change.Properties.RenameProps = null;
                                    change.Properties.ChangeProps = null;
                                    break;

                                // Delete again -> impossible
                                case FileSystemEntryAction.Delete:
                                    throw new InvalidActionSequenceException(change.Action, action);
                            }
                            break;
                    }
                }
                else
                {
                    var change = new FileSystemEntryChange
                    {
                        Timestamp = timestamp,
                        Action = action
                    };
                    switch (action)
                    {
                        case FileSystemEntryAction.Rename:
                            var properties = ActionProps.Deserialize<RenameProps>(json);
                            change.Properties.RenameProps = properties;

                            // Create the original entry
                            var orig = string.Join('|', path, properties.Name);
                            delta.Add(orig, change);

                            // Create the reference to the original entry
                            names.Add(ReplaceName(path, properties.Name), orig);
                            break;
                        case FileSystemEntryAction.Create:
                        case FileSystemEntryAction.Change:
                            change.Properties.ChangeProps = ActionProps.Deserialize<ChangeProps>(json);
                            delta.Add(path, change);
                            break;
                        case FileSystemEntryAction.Delete:
                            delta.Add(path, change);
                            break;
                    }
                        
                }
            }
        });

        // Trim entries' names appendixes, as they are already stored in RenameProps
        foreach (var key in names.Values)
        {
            delta.Remove(key, out var change);
            delta.Add(key.Split('|')[0], change);
        }
            
        // TODO: order by timestamp
        // return actions.OrderBy(kvp => kvp.Value.Time);
        return delta;
    }

    private static string ReplaceName(string entry, string name)
    {
        int startIndex = entry.Length - (Path.EndsInDirectorySeparator(entry) ? 2 : 1);
        int sepIndex = entry.LastIndexOf(Path.DirectorySeparatorChar, startIndex, startIndex + 1);
        return entry[..(sepIndex + 1)] + name;
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