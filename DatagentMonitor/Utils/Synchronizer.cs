using DatagentMonitor.FileSystem;
using DatagentShared;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DatagentMonitor.Utils;

internal class Synchronizer
{
    private readonly SynchronizationSourceManager _sourceManager;
    public SynchronizationSourceManager SourceManager => _sourceManager;

    private readonly SynchronizationSourceManager _targetManager;
    public SynchronizationSourceManager TargetManager => _targetManager;

    public Synchronizer(SynchronizationSourceManager sourceManager, SynchronizationSourceManager targetManager)
    {
        _sourceManager = sourceManager;
        _targetManager = targetManager;
    }

    public Synchronizer(SynchronizationSourceManager sourceManager, string targetRoot) : 
        this(sourceManager, new SynchronizationSourceManager(targetRoot)) { }

    public Synchronizer(string sourceRoot, string targetRoot) : 
        this(new SynchronizationSourceManager(sourceRoot), new SynchronizationSourceManager(targetRoot)) { }

    public void Run(out List<FileSystemEntryChange> applied, out List<FileSystemEntryChange> failed)
    {
        Console.Write($"Resolving target changes... ");
        // TODO: targetToIndex has to be a Dictionary (or a LookupLinkedList), 
        // because each path can only appear once amongst the changes.
        var targetToIndex = GetTargetDelta(_targetManager.Root);
        Console.WriteLine($"Total: {targetToIndex.Count}");

        Console.Write($"Resolving source changes... ");
        var sourceToIndex = GetSourceDelta();
        Console.WriteLine($"Total: {sourceToIndex.Count}");

        var appliedDelta = new List<FileSystemEntryChange>();
        var failedDelta = new List<FileSystemEntryChange>();
        Console.Write("Target latest sync timestamp: ");
        var lastSyncTimestamp = GetTargetLastSyncTimestamp(_targetManager.EventsDatabase);
        Console.WriteLine(lastSyncTimestamp != null ? lastSyncTimestamp.Value.ToString(CustomFileInfo.DateTimeFormat) : "N/A");

        void TryApplyChange(string sourceRoot, string targetRoot, FileSystemEntryChange change)
        {
            // TODO: use database instead of in-memory dicts
            var status = ApplyChange(sourceRoot, targetRoot, change);
            if (status)
                appliedDelta!.Add(change);
            else
                failedDelta!.Add(change);
            Console.WriteLine($"Status: {(status ? "applied" : "failed")}");
        }

        var sourceToTarget = new List<FileSystemEntryChange>();
        var targetToSource = new List<FileSystemEntryChange>();

        // Both sourceToIndex and targetToIndex deltas have to be enumerated in an insertion order;
        // otherwise Created directory contents can get scheduled before that directory creation.
        foreach (var targetChange in targetToIndex)
        {
            if (sourceToIndex.Remove(targetChange.Path, out var sourceChange))
            {
                // Entry change is present on both source and target ->
                // determine which of two changes is to be kept (source, target or both)
                Console.Write($"Common: {targetChange.Path}; Strategy: ");

                // TODO: implement merge
                //
                // Merge can be employed when the contents of two entries (files only) can be combined into one.
                // This holds true for the following pairs of actions:
                // CREATE vs CREATE (directories are always created empty, so nothing to be merged)
                // RENAME vs CHANGE (directory CHANGE not allowed)
                // CHANGE vs RENAME <-'
                // CHANGE vs CHANGE <-'
                // 
                // Note: if one of two actions is CREATE, the other *must* be CREATE.
                //
                // Merge cannot be employed for the following pairs of actions:
                // RENAME vs RENAME:
                //      Two names cannot be (reasonably) combined into one without specific rules, 
                //      so only one name is to be kept.
                // All pairs with DELETE:
                //      It is a question of whether accepting DELETE or not,
                //      rather than of merging entry contents.
                if (lastSyncTimestamp == null || sourceChange.Timestamp >= lastSyncTimestamp)
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
                    ResolveConflict(_sourceManager.Root, sourceChange, targetChange, sourceToTarget);
                }
                else
                {
                    // Target is actual
                    Console.Write("T->S; ");
                    ResolveConflict(_targetManager.Root, targetChange, sourceChange, targetToSource);
                }
            }
            else
            {
                // Entry change is present only on the target -> 
                // propagate the change to the source
                Console.Write($"From target: {targetChange.Path}; ");
                targetToSource.Add(targetChange);
            }
        }

        // The entries in sourceToIndex are ordered in an insertion order, i. e. by timestamp.
        foreach (var sourceChange in sourceToIndex)
        {
            // Entry change is present only on the source ->
            // propagate the change to the target
            Console.Write($"From source: {sourceChange.Path}; ");
            sourceToTarget.Add(sourceChange);
        }

        foreach (var sourceChange in sourceToTarget)
            TryApplyChange(_sourceManager.Root, _targetManager.Root, sourceChange);

        foreach (var targetChange in targetToSource)
            TryApplyChange(_targetManager.Root, _sourceManager.Root, targetChange);

        Console.WriteLine($"Total changes applied: {appliedDelta.Count}.\n");

        // Generate the new index based on the old one, according to the rule:
        // s(d(S_0) + d(ΔS)) = S_0 + ΔS
        // where s(x) and d(x) stand for serialization and deserialization routines resp
        //
        // TODO: deserialization is happening twice: here and in GetTargetDelta;
        // re-use the already deserialized index
        var index = _sourceManager.DeserializeIndex();
        index.MergeChanges(appliedDelta);
        _sourceManager.SerializeIndex(index);

        Console.WriteLine($"Total changes failed: {failedDelta.Count}.");

        // TODO: propose possible workarounds for failed changes

        SetTargetLastSyncTimestamp(_targetManager.EventsDatabase);

        ClearEventsDatabase();

        Console.WriteLine("Synchronization complete.");

        applied = appliedDelta;
        failed = failedDelta;
    }

    private static void ResolveConflict(
        string sourceRoot,
        FileSystemEntryChange sourceChange, 
        FileSystemEntryChange targetChange, 
        List<FileSystemEntryChange> container)
    {
        if ((sourceChange.Action == FileSystemEntryAction.Create && targetChange.Action != FileSystemEntryAction.Create) ||
                    (sourceChange.Action != FileSystemEntryAction.Create && targetChange.Action == FileSystemEntryAction.Create))
            throw new ArgumentException($"Inconsistent conflicting actions; {sourceChange.Action} on source, {targetChange.Action} on target.");

        // Directory
        if (CustomFileSystemInfo.IsDirectory(sourceChange.Path))
        {
            switch (sourceChange.Action)
            {
                // Target action is guaranteed to be Create ->
                // Create on both sides -> nothing to be done
                case FileSystemEntryAction.Create:
                    break;

                case FileSystemEntryAction.Rename:
                    switch (targetChange.Action)
                    {
                        case FileSystemEntryAction.Rename:
                            container.Add(new FileSystemEntryChange
                            {
                                Path = ReplaceName(sourceChange.Path, targetChange.Properties.RenameProps!.Name),
                                Action = FileSystemEntryAction.Rename,
                                Properties = sourceChange.Properties
                            });
                            break;

                        // Rename on source VS Delete on target ->
                        // create on target with the new source directory name and all its contents
                        case FileSystemEntryAction.Delete:
                            var path = ReplaceName(Path.Combine(sourceRoot, sourceChange.Path), sourceChange.Properties.RenameProps!.Name);
                            OnDirectoryCreated(new DirectoryInfo(path), new StringBuilder(), container);
                            break;
                    }
                    break;

                case FileSystemEntryAction.Delete:
                    switch (targetChange.Action)
                    {
                        case FileSystemEntryAction.Rename:
                            container.Add(new FileSystemEntryChange
                            {
                                Path = ReplaceName(sourceChange.Path, targetChange.Properties.RenameProps!.Name),
                                Action = FileSystemEntryAction.Delete
                            });
                            break;

                        // Deleted on both sides -> nothing to be done
                        case FileSystemEntryAction.Delete:
                            break;
                    }
                    break;
            }
        }
        // File
        else
        {
            switch (sourceChange.Action)
            {
                case FileSystemEntryAction.Create:
                    switch (targetChange.Action)
                    {
                        // Create on source VS Create on target -> 
                        // change the target file using the source file properties
                        case FileSystemEntryAction.Create:
                            container.Add(new FileSystemEntryChange
                            {
                                Path = sourceChange.Path,
                                Action = FileSystemEntryAction.Change,
                                Properties = new FileSystemEntryChangeProperties
                                {
                                    ChangeProps = sourceChange.Properties.ChangeProps!
                                }
                            });
                            break;
                    }
                    break;

                case FileSystemEntryAction.Rename:
                    var sourceFileInfo = new FileInfo(ReplaceName(sourceChange.Path, sourceChange.Properties.RenameProps!.Name));
                    switch (targetChange.Action)
                    {
                        // Rename on source VS Rename on target -> 
                        // rename the target file (having the new name) to the source file new name
                        case FileSystemEntryAction.Rename:
                            container.Add(new FileSystemEntryChange
                            {
                                Path = ReplaceName(sourceChange.Path, targetChange.Properties.RenameProps!.Name),
                                Action = FileSystemEntryAction.Rename,
                                Properties = new FileSystemEntryChangeProperties
                                {
                                    RenameProps = sourceChange.Properties.RenameProps!
                                }
                            });
                            break;

                        // Rename on source VS Change on target -> 
                        // change to the source file state and rename to the source file new name
                        case FileSystemEntryAction.Change:
                            container.Add(new FileSystemEntryChange
                            {
                                Path = targetChange.Properties.RenameProps == null ?
                                    sourceChange.Path : ReplaceName(sourceChange.Path, targetChange.Properties.RenameProps!.Name),
                                Action = FileSystemEntryAction.Change,
                                Properties = new FileSystemEntryChangeProperties
                                {
                                    RenameProps = sourceChange.Properties.RenameProps!,
                                    ChangeProps = new ChangeProperties
                                    {
                                        LastWriteTime = sourceFileInfo.LastWriteTime,
                                        Length = sourceFileInfo.Length
                                    }
                                }
                            });
                            break;

                        // Rename on source VS Delete on target -> 
                        // create on target using the source file name and properties
                        case FileSystemEntryAction.Delete:
                            container.Add(new FileSystemEntryChange
                            {
                                Path = ReplaceName(sourceChange.Path, sourceChange.Properties.RenameProps!.Name),
                                Action = FileSystemEntryAction.Create,
                                Properties = new FileSystemEntryChangeProperties
                                {
                                    ChangeProps = new ChangeProperties
                                    {
                                        LastWriteTime = sourceFileInfo.LastWriteTime,
                                        Length = sourceFileInfo.Length
                                    }
                                }
                            });
                            break;
                    }
                    break;

                case FileSystemEntryAction.Change:
                    switch (targetChange.Action)
                    {
                        // Change on source VS Rename/Change on target -> 
                        // change to the source file state and rename to the actual source file name
                        case FileSystemEntryAction.Rename:
                        case FileSystemEntryAction.Change:
                            container.Add(new FileSystemEntryChange
                            {
                                Path = targetChange.Properties.RenameProps == null ?
                                    sourceChange.Path : ReplaceName(sourceChange.Path, targetChange.Properties.RenameProps!.Name),
                                Action = FileSystemEntryAction.Change,
                                Properties = new FileSystemEntryChangeProperties
                                {
                                    RenameProps = sourceChange.Properties.RenameProps ??
                                        (targetChange.Properties.RenameProps == null ? null : new RenameProperties
                                        {
                                            Name = Path.GetFileName(sourceChange.Path)
                                        }),
                                    ChangeProps = sourceChange.Properties.ChangeProps
                                }
                            });
                            break;

                        case FileSystemEntryAction.Delete:
                            container.Add(new FileSystemEntryChange
                            {
                                Path = sourceChange.Properties.RenameProps == null ?
                                    sourceChange.Path : ReplaceName(sourceChange.Path, sourceChange.Properties.RenameProps!.Name),
                                Action = FileSystemEntryAction.Create,
                                Properties = new FileSystemEntryChangeProperties
                                {
                                    ChangeProps = sourceChange.Properties.ChangeProps
                                }
                            });
                            break;
                    }
                    break;

                case FileSystemEntryAction.Delete:
                    switch (targetChange.Action)
                    {
                        case FileSystemEntryAction.Rename:
                        case FileSystemEntryAction.Change:
                            container.Add(new FileSystemEntryChange
                            {
                                Path = targetChange.Properties.RenameProps == null ?
                                    sourceChange.Path : ReplaceName(sourceChange.Path, targetChange.Properties.RenameProps!.Name),
                                Action = FileSystemEntryAction.Delete
                            });
                            break;

                        case FileSystemEntryAction.Delete:
                            break;
                    }
                    break;
            }
        }
    }

    private static bool ApplyChange(string sourceRoot, string targetRoot, FileSystemEntryChange change)
    {
        Console.WriteLine($"{sourceRoot} -> {targetRoot}: [{change.Action}] {change.Path})");
        var renameProps = change.Properties.RenameProps;
        var changeProps = change.Properties.ChangeProps;
        string Rename(string path) => ReplaceName(path, renameProps!.Name);

        var sourcePath = Path.Combine(sourceRoot, change.Path);
        var targetPath = Path.Combine(targetRoot, change.Path);
        if (CustomFileSystemInfo.IsDirectory(sourcePath))
        {
            DirectoryInfo sourceDirectory, targetDirectory;
            switch (change.Action)
            {
                case FileSystemEntryAction.Create:
                    sourceDirectory = new DirectoryInfo(sourcePath);

                    // Source directory is not present -> the change is invalid
                    if (!sourceDirectory.Exists)
                        return false;

                    targetDirectory = new DirectoryInfo(targetPath);

                    // Target directory is present -> the change is invalid
                    if (targetDirectory.Exists)
                        return false;

                    // Note: directory creation does not require contents comparison,
                    // as all contents are written as separate entries in database;
                    // see OnDirectoryCreated method
                    targetDirectory.Create();
                    break;

                case FileSystemEntryAction.Rename:
                    sourceDirectory = new DirectoryInfo(Rename(sourcePath));

                    // Source directory is not present -> the change is invalid
                    if (!sourceDirectory.Exists)
                        return false;

                    targetDirectory = new DirectoryInfo(Rename(targetPath));

                    // Target directory (with a new name) is present -> the change is invalid
                    if (targetDirectory.Exists)
                        return false;

                    targetDirectory = new DirectoryInfo(targetPath);

                    // Target directory is not present -> the change is invalid
                    if (!targetDirectory.Exists)
                        return false;

                    targetDirectory.MoveTo(Rename(targetPath));
                    break;

                case FileSystemEntryAction.Change:
                    throw new DirectoryChangeActionNotAllowed();

                case FileSystemEntryAction.Delete:
                    sourceDirectory = new DirectoryInfo(sourcePath);

                    // Source directory is present -> the change is invalid
                    if (sourceDirectory.Exists)
                        return false;

                    targetDirectory = new DirectoryInfo(targetPath);

                    // Target directory is not present -> the change needs not to be applied
                    if (!targetDirectory.Exists)
                        return true;

                    targetDirectory.Delete(recursive: true);
                    break;
            }
        }
        else
        {
            FileInfo sourceFile, targetFile;
            switch (change.Action)
            {
                case FileSystemEntryAction.Create:
                    sourceFile = new FileInfo(sourcePath);

                    // Source file is not present -> the change is invalid
                    if (!sourceFile.Exists)
                        return false;

                    // Source file is altered -> the change is invalid
                    if (sourceFile.LastWriteTime.TrimMicroseconds() != changeProps!.LastWriteTime ||
                        sourceFile.Length != changeProps.Length)
                        return false;

                    targetFile = new FileInfo(targetPath);

                    // Target file is present -> the change is invalid
                    if (targetFile.Exists)
                        return false;

                    sourceFile.CopyTo(targetPath);
                    break;

                case FileSystemEntryAction.Rename:
                    sourceFile = new FileInfo(Rename(sourcePath));

                    // Source file is not present -> the change is invalid
                    if (!sourceFile.Exists)
                        return false;

                    targetFile = new FileInfo(Rename(targetPath));

                    // Target file (with a new name) is present -> the change is invalid
                    if (targetFile.Exists)
                        return false;

                    targetFile = new FileInfo(targetPath);

                    // Target file is not present -> the change is invalid
                    if (!targetFile.Exists)
                        return false;

                    targetFile.MoveTo(Rename(targetPath));
                    break;

                case FileSystemEntryAction.Change:
                    sourceFile = new FileInfo(renameProps != null ? Rename(sourcePath) : sourcePath);

                    // Source file is not present -> the change is invalid
                    if (!sourceFile.Exists)
                        return false;

                    // Source file is altered -> the change is invalid
                    if (sourceFile.LastWriteTime.TrimMicroseconds() != changeProps!.LastWriteTime ||
                        sourceFile.Length != changeProps.Length)
                        return false;

                    if (renameProps != null)
                    {
                        targetFile = new FileInfo(Rename(targetPath));

                        // Target file (with a new name) is present -> the change is invalid
                        if (targetFile.Exists)
                            return false;
                    }

                    targetFile = sourceFile.CopyTo(targetPath, overwrite: true);
                    if (renameProps != null)
                        targetFile.MoveTo(Rename(targetPath));
                    break;

                case FileSystemEntryAction.Delete:
                    sourceFile = new FileInfo(sourcePath);

                    // Source file is present -> the change is invalid
                    if (sourceFile.Exists)
                        return false;

                    targetFile = new FileInfo(targetPath);

                    // Target file is not present -> the change needs not to be applied
                    if (!targetFile.Exists)
                        return true;

                    targetFile.Delete();
                    break;
            }
        }

        return true;
    }

    private static DateTime? GetTargetLastSyncTimestamp(Database targetDatabase)
    {
        DateTime? result = null;
        try
        {
            using var command = new SqliteCommand("SELECT * FROM sync ORDER BY time DESC LIMIT 1");
            targetDatabase.ExecuteReader(command, reader =>
            {
                if (reader.Read())
                    result = DateTime.ParseExact(reader.GetString(1), CustomFileInfo.DateTimeFormat, null);
            });
        }
        catch (SqliteException ex)
        {
            // TODO: table not found ->
            // create it here so that timetstamp could be set later
        }
        return result;
    }

    private static void SetTargetLastSyncTimestamp(Database targetDatabase)
    {
        //using var command = new SqliteCommand("INSERT INTO sync VALUES :time");
        //command.Parameters.AddWithValue(":time", DateTime.Now.ToString(CustomFileInfo.DateTimeFormat));
        //targetDatabase.ExecuteNonQuery(command);
    }

    private void ClearEventsDatabase()
    {
        using var command = new SqliteCommand("DELETE FROM events");
        _sourceManager.EventsDatabase.ExecuteNonQuery(command);
    }

    private List<FileSystemEntryChange> GetTargetDelta(string targetRoot)
    {
        var sourceDir = _sourceManager.DeserializeIndex();  // last synced source data
        var targetDir = new DirectoryInfo(targetRoot);
        var builder = new StringBuilder();
        var delta = new List<FileSystemEntryChange>();
        GetTargetDelta(sourceDir, targetDir, builder, delta);
        return delta;
    }

    private void GetTargetDelta(CustomDirectoryInfo sourceDir, DirectoryInfo targetDir, StringBuilder builder, List<FileSystemEntryChange> delta)
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
            if (_targetManager.IsServiceLocation(targetSubdir.FullName))
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

        foreach (var _ in builder.Wrap(sourceDir.Directories, d => d.Name + Path.DirectorySeparatorChar))
        {
            delta.Add(new FileSystemEntryChange
            {
                Path = builder.ToString(),
                Action = FileSystemEntryAction.Delete
            });
        }

        foreach (var targetFile in builder.Wrap(targetDir.EnumerateFiles(), f => f.Name))
        {
            var targetLastWriteTime = targetFile.LastWriteTime.TrimMicroseconds();
            if (sourceDir.Files.Remove(targetFile.Name, out var sourceFile))
            {
                if (targetLastWriteTime != sourceFile.LastWriteTime || targetFile.Length != sourceFile.Length)
                {
                    delta.Add(new FileSystemEntryChange
                    {
                        Path = builder.ToString(),
                        Action = FileSystemEntryAction.Change,
                        Properties = new FileSystemEntryChangeProperties
                        {
                            ChangeProps = new ChangeProperties
                            {
                                LastWriteTime = targetLastWriteTime,
                                Length = targetFile.Length
                            }
                        }
                    });
                }
            }
            else
            {
                delta.Add(new FileSystemEntryChange
                {
                    Path = builder.ToString(),
                    Action = FileSystemEntryAction.Create,
                    Properties = new FileSystemEntryChangeProperties
                    {
                        ChangeProps = new ChangeProperties
                        {
                            LastWriteTime = targetLastWriteTime,
                            Length = targetFile.Length
                        }
                    }
                });
            }
        }

        foreach (var _ in builder.Wrap(sourceDir.Files, f => f.Name))
        {
            delta.Add(new FileSystemEntryChange
            {
                Path = builder.ToString(),
                Action = FileSystemEntryAction.Delete
            });
        }
    }

    private static void OnDirectoryCreated(DirectoryInfo root, StringBuilder builder, List<FileSystemEntryChange> delta)
    {
        delta.Add(new FileSystemEntryChange
        {
            Path = builder.ToString(),
            Action = FileSystemEntryAction.Create,
        });

        foreach (var directory in builder.Wrap(root.EnumerateDirectories(), d => d.Name + Path.DirectorySeparatorChar))
        {
            OnDirectoryCreated(directory, builder, delta);
        }

        foreach (var file in builder.Wrap(root.EnumerateFiles(), f => f.Name))
        {
            delta.Add(new FileSystemEntryChange
            {
                Path = builder.ToString(),
                Action = FileSystemEntryAction.Create,
                Properties = new FileSystemEntryChangeProperties
                {
                    ChangeProps = new ChangeProperties
                    {
                        LastWriteTime = file.LastWriteTime.TrimMicroseconds(),
                        Length = file.Length
                    }
                }
            });
        }
    }

    // TODO: consider returning LookupLinkedList?
    private FileSystemTrie GetSourceDelta()
    {
        var delta = new FileSystemTrie();
        using var command = new SqliteCommand("SELECT * FROM events");
        _sourceManager.EventsDatabase.ExecuteReader(command, reader =>
        {
            while (reader.Read())
            {
                var change = new FileSystemEntryChange
                {
                    Path = reader.GetString(1),
                    Action = FileSystemEntryActionExtensions.StringToAction(reader.GetString(2)),
                    Timestamp = DateTime.ParseExact(reader.GetString(0), CustomFileInfo.DateTimeFormat, null)
                };

                if (!reader.IsDBNull(3))
                {
                    var json = reader.GetString(3);
                    switch (change.Action)
                    {
                        case FileSystemEntryAction.Rename:
                            change.Properties.RenameProps = ActionProperties.Deserialize<RenameProperties>(json)!;
                            break;

                        case FileSystemEntryAction.Create:
                        case FileSystemEntryAction.Change:
                            change.Properties.ChangeProps = ActionProperties.Deserialize<ChangeProperties>(json)!;
                            break;
                    }
                }

                delta.Add(change);
            }
        });

        delta.Close();
        return delta;
    }

    // Replace an entry name in the given path:
    // path/to/a/file -> path/to/a/renamed-file
    // path/to/a/directory/ -> path/to/a/renamed-directory/
    private static string ReplaceName(string entry, string name)
    {
        var range = CustomFileSystemInfo.GetEntryNameRange(entry);
        return entry[..range.Start] + name + entry[range.End..];
    }
}
