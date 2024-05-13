using DatagentMonitor.FileSystem;
using DatagentShared;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

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
        var targetToIndex = GetTargetDelta(_targetManager.Root);
        Console.WriteLine($"Total: {targetToIndex.Count}");

        Console.Write($"Resolving source changes... ");
        var sourceToIndex = GetSourceDelta();
        Console.WriteLine($"Total: {sourceToIndex.Count}");

        var appliedDelta = new List<FileSystemEntryChange>();
        var failedDelta = new List<FileSystemEntryChange>();

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

        GetRelativeChanges(sourceToIndex, targetToIndex, out var sourceToTarget, out var targetToSource);

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

    private void GetRelativeChanges(
        FileSystemTrie sourceToIndex, 
        FileSystemTrie targetToIndex, 
        out FileSystemTrie sourceToTarget, 
        out FileSystemTrie targetToSource)
    {
        var sourceToTargetLocal = new FileSystemTrie();
        var targetToSourceLocal = new FileSystemTrie();

        void ResolveDirectoryConflictCmp(
            FileSystemEntryChange sourceCmp, 
            FileSystemEntryChange targetCmp, 
            FileSystemEntryChange? sourceChange, 
            FileSystemEntryChange? targetChange)
        {
            // Source-to-Target
            if (sourceCmp.Timestamp >= targetCmp.Timestamp)
            {
                var changes = ResolveDirectoryConflict(_sourceManager.Root, sourceToIndex, targetToIndex, sourceChange, targetChange);
                foreach (var change in changes)
                    sourceToTargetLocal.Add(change);
            }
            // Target-to-Source
            else
            {
                var changes = ResolveDirectoryConflict(_targetManager.Root, targetToIndex, sourceToIndex, targetChange, sourceChange);
                foreach (var change in changes)
                    targetToSourceLocal.Add(change);
            }
        }

        void ResolveFileConflictCmp(
            FileSystemEntryChange sourceChange, 
            FileSystemEntryChange targetChange)
        {
            // Source-to-Target
            if (sourceChange.Timestamp >= targetChange.Timestamp)
            {
                var change = ResolveFileConflict(sourceChange, targetChange);
                if (change is not null)
                    sourceToTargetLocal.Add(change);
            }
            // Target-to-Source
            else
            {
                var change = ResolveFileConflict(targetChange, sourceChange);
                if (change is not null)
                    targetToSourceLocal.Add(change);
            }
        }

        Console.Write("Target latest sync timestamp: ");
        var lastSyncTimestamp = GetTargetLastSyncTimestamp(_targetManager.EventsDatabase);
        Console.WriteLine(lastSyncTimestamp != null ? lastSyncTimestamp.Value.ToString(CustomFileInfo.DateTimeFormat) : "N/A");

        // Both sourceToIndex and targetToIndex deltas have to be enumerated in an insertion order;
        // otherwise Created directory contents can get scheduled before that directory creation.
        var targetListNode = targetToIndex.Values.First;
        while (targetListNode?.Next != null)
        {
            var targetChange = targetListNode.Value.Value! ?? throw new ArgumentException("Target change cannot be null.");
            var parts = Path.TrimEndingDirectorySeparator(targetChange.Path).Split(Path.DirectorySeparatorChar);
            var sourceNode = sourceToIndex.Root;
            var targetNode = targetToIndex.Root;
            foreach (var part in parts)
            {
                if (!sourceNode.Names.TryGetValue(part, out var sourceNext) &&
                    !sourceNode.Children.TryGetValue(part, out sourceNext))
                    break;

                if (!targetNode.Names.TryGetValue(part, out var targetNext) && 
                    !targetNode.Children.TryGetValue(part, out targetNext))
                    throw new KeyNotFoundException($"Broken tree path: {targetChange.Path}; Invalid part: {part}");

                sourceNode = sourceNext;
                targetNode = targetNext;
            }

            var sourceChange = sourceNode.Value;

            // File, if a leaf and not a directory
            // TODO: this does (falsely) assume that the source change
            // always has the same kind (file/directory) as the target change; fix it
            if (targetNode.Value == targetChange && !CustomFileSystemInfo.IsDirectory(targetChange.Path))
            {
                if (sourceChange == null)
                    throw new ArgumentException("Source file change cannot be null.");

                switch (sourceNode.Value?.Action, targetNode.Value?.Action)
                {
                    case (FileSystemEntryAction.Create, FileSystemEntryAction.Create):
                        ResolveFileConflictCmp(sourceChange, targetChange);
                        break;

                    case (FileSystemEntryAction.Create, _):
                    case (_, FileSystemEntryAction.Create):
                        throw new InvalidConflictException(sourceChange.Action, targetChange.Action);

                    case (FileSystemEntryAction.Delete, FileSystemEntryAction.Delete):
                        break;

                    default:
                        ResolveFileConflictCmp(sourceChange, targetChange);
                        break;
                }

                ResolveFileConflictCmp(sourceChange, targetChange);
            }
            // Directory
            else
            {
                // Target node value is null iff it did not align with the target change;
                // this is a consequence of parent changes being processed before child changes
                switch (sourceNode.Value?.Action, targetNode.Value?.Action)
                {
                    case (FileSystemEntryAction.Create, FileSystemEntryAction.Create):
                    case (FileSystemEntryAction.Delete, FileSystemEntryAction.Delete):
                        break;

                    case (FileSystemEntryAction.Create, _):
                    case (_, FileSystemEntryAction.Create):
                        throw new InvalidConflictException(sourceNode.Value?.Action, targetNode.Value?.Action);                                            

                    case (null, null):
                    case (null, FileSystemEntryAction.Rename):
                        targetToSourceLocal.Add(targetChange);
                        break;

                    case (FileSystemEntryAction.Rename, null):
                        throw new NotImplementedException();

                    case (FileSystemEntryAction.Rename, FileSystemEntryAction.Rename):
                        ResolveDirectoryConflictCmp(sourceNode.Value, targetNode.Value, sourceNode.Value, targetNode.Value);
                        break;

                    case (FileSystemEntryAction.Delete, null):
                    case (FileSystemEntryAction.Delete, FileSystemEntryAction.Rename):
                        ResolveDirectoryConflictCmp(sourceNode.Value, targetNode.GetPriority(), sourceNode.Value, targetNode.Value);
                        break;

                    case (null, FileSystemEntryAction.Delete):
                    case (FileSystemEntryAction.Rename, FileSystemEntryAction.Delete):
                        ResolveDirectoryConflictCmp(sourceNode.GetPriority(), targetNode.Value, sourceNode.Value, targetNode.Value);
                        break;
                }
            }
        }

        // The entries in sourceToIndex are ordered in an insertion order, i. e. by timestamp.
        foreach (var sourceChange in sourceToIndex)
        {
            // Entry change is present only on the source ->
            // propagate the change to the target
            Console.Write($"From source: {sourceChange.Path}; ");

            // Do not stack changes, as the ones obtained during 
            // conflict resolves are of a higher priority
            sourceToTargetLocal.Add(sourceChange, stack: false);
        }

        sourceToTarget = sourceToTargetLocal;
        targetToSource = targetToSourceLocal;
    }

    private bool Conflicts(FileSystemEntryChange sourceChange, FileSystemEntryChange targetChange)
    {
        throw new NotImplementedException();
    }

    private static List<FileSystemEntryChange> ResolveDirectoryConflict(
        string sourceRoot,
        FileSystemTrie sourceToIndex,
        FileSystemTrie targetToIndex,
        FileSystemEntryChange? sourceChange,
        FileSystemEntryChange? targetChange)
    {
        var result = new List<FileSystemEntryChange>();
        switch (sourceChange?.Action, targetChange?.Action)
        {
            case (FileSystemEntryAction.Rename, FileSystemEntryAction.Rename):
                result.Add(new FileSystemEntryChange
                {
                    Path = CustomFileSystemInfo.ReplaceEntryName(sourceChange.Path, targetChange.Properties.RenameProps!.Name),
                    Action = FileSystemEntryAction.Rename,
                    Properties = sourceChange.Properties
                });
                break;

            case (null, FileSystemEntryAction.Delete):
            case (FileSystemEntryAction.Rename, FileSystemEntryAction.Delete):
                var subpath = sourceChange == null ? targetChange.Path :
                    CustomFileSystemInfo.ReplaceEntryName(targetChange.Path, sourceChange.Properties.RenameProps!.Name);
                OnDirectoryCreated(new DirectoryInfo(Path.Combine(sourceRoot, subpath)), new StringBuilder(subpath), result);

                sourceToIndex.RemoveSubtree(subpath);
                break;            

            case (FileSystemEntryAction.Delete, null):
            case (FileSystemEntryAction.Delete, FileSystemEntryAction.Rename):
                result.Add(new FileSystemEntryChange
                {
                    Path = targetChange == null ? sourceChange.Path :
                        CustomFileSystemInfo.ReplaceEntryName(sourceChange.Path, targetChange.Properties.RenameProps!.Name),
                    Action = FileSystemEntryAction.Delete
                });

                targetToIndex.RemoveSubtree(sourceChange.Path);
                break;
        }

        return result;
    }

    private static FileSystemEntryChange? ResolveFileConflict(
        FileSystemEntryChange sourceChange, 
        FileSystemEntryChange targetChange)
    {
        FileInfo sourceFileInfo;
        switch (sourceChange.Action, targetChange.Action)
        {
            // Сhange the target file using the source file properties
            case (FileSystemEntryAction.Create, FileSystemEntryAction.Create):
                return new FileSystemEntryChange
                {
                    Path = sourceChange.Path,
                    Action = FileSystemEntryAction.Change,
                    Properties = new FileSystemEntryChangeProperties
                    {
                        ChangeProps = sourceChange.Properties.ChangeProps!
                    }
                };

            // Rename the target file (having the new name) to the source file new name
            case (FileSystemEntryAction.Rename, FileSystemEntryAction.Rename):
                return new FileSystemEntryChange
                {
                    Path = CustomFileSystemInfo.ReplaceEntryName(sourceChange.Path, targetChange.Properties.RenameProps!.Name),
                    Action = FileSystemEntryAction.Rename,
                    Properties = new FileSystemEntryChangeProperties
                    {
                        RenameProps = sourceChange.Properties.RenameProps!
                    }
                };

            // Change to the source file state and rename to the source file new name
            case (FileSystemEntryAction.Rename, FileSystemEntryAction.Change):
                sourceFileInfo = new FileInfo(CustomFileSystemInfo.ReplaceEntryName(sourceChange.Path, sourceChange.Properties.RenameProps!.Name));
                return new FileSystemEntryChange
                {
                    Path = targetChange.Properties.RenameProps == null ?
                                sourceChange.Path : CustomFileSystemInfo.ReplaceEntryName(sourceChange.Path, targetChange.Properties.RenameProps!.Name),
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
                };

            // Create on target using the source file name and properties
            case (FileSystemEntryAction.Rename, FileSystemEntryAction.Delete):
                sourceFileInfo = new FileInfo(CustomFileSystemInfo.ReplaceEntryName(sourceChange.Path, sourceChange.Properties.RenameProps!.Name));
                return new FileSystemEntryChange
                {
                    Path = CustomFileSystemInfo.ReplaceEntryName(sourceChange.Path, sourceChange.Properties.RenameProps!.Name),
                    Action = FileSystemEntryAction.Create,
                    Properties = new FileSystemEntryChangeProperties
                    {
                        ChangeProps = new ChangeProperties
                        {
                            LastWriteTime = sourceFileInfo.LastWriteTime,
                            Length = sourceFileInfo.Length
                        }
                    }
                };

            // Change to the source file state and rename to the actual source file name
            case (FileSystemEntryAction.Change, FileSystemEntryAction.Rename):
            case (FileSystemEntryAction.Change, FileSystemEntryAction.Change):
                return new FileSystemEntryChange
                {
                    Path = targetChange.Properties.RenameProps == null ?
                                sourceChange.Path : CustomFileSystemInfo.ReplaceEntryName(sourceChange.Path, targetChange.Properties.RenameProps!.Name),
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
                };

            case (FileSystemEntryAction.Change, FileSystemEntryAction.Delete):
                return new FileSystemEntryChange
                {
                    Path = sourceChange.Properties.RenameProps == null ?
                                sourceChange.Path : CustomFileSystemInfo.ReplaceEntryName(sourceChange.Path, sourceChange.Properties.RenameProps!.Name),
                    Action = FileSystemEntryAction.Create,
                    Properties = new FileSystemEntryChangeProperties
                    {
                        ChangeProps = sourceChange.Properties.ChangeProps
                    }
                };

            case (FileSystemEntryAction.Delete, FileSystemEntryAction.Rename):
            case (FileSystemEntryAction.Delete, FileSystemEntryAction.Change):
                return new FileSystemEntryChange
                {
                    Path = targetChange.Properties.RenameProps == null ?
                                sourceChange.Path : CustomFileSystemInfo.ReplaceEntryName(sourceChange.Path, targetChange.Properties.RenameProps!.Name),
                    Action = FileSystemEntryAction.Delete
                };
        }

        return null;
    }

    private static bool ApplyChange(string sourceRoot, string targetRoot, FileSystemEntryChange change)
    {
        Console.WriteLine($"{sourceRoot} -> {targetRoot}: [{change.Action}] {change.Path})");
        var renameProps = change.Properties.RenameProps;
        var changeProps = change.Properties.ChangeProps;
        string Rename(string path) => CustomFileSystemInfo.ReplaceEntryName(path, renameProps!.Name);

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
                    throw new DirectoryChangeActionNotAllowedException();

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

    private FileSystemTrie GetTargetDelta(string targetRoot)
    {
        var sourceDir = _sourceManager.DeserializeIndex();  // last synced source data
        var targetDir = new DirectoryInfo(targetRoot);
        var builder = new StringBuilder();
        var delta = new FileSystemTrie();
        GetTargetDelta(sourceDir, targetDir, builder, delta);
        return delta;
    }

    private void GetTargetDelta(CustomDirectoryInfo sourceDir, DirectoryInfo targetDir, StringBuilder builder, FileSystemTrie delta)
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

    private static void OnDirectoryCreated(DirectoryInfo root, StringBuilder builder, ICollection<FileSystemEntryChange> container)
    {
        container.Add(new FileSystemEntryChange
        {
            Path = builder.ToString(),
            Action = FileSystemEntryAction.Create,
        });

        foreach (var directory in builder.Wrap(root.EnumerateDirectories(), d => d.Name + Path.DirectorySeparatorChar))
        {
            OnDirectoryCreated(directory, builder, container);
        }

        foreach (var file in builder.Wrap(root.EnumerateFiles(), f => f.Name))
        {
            container.Add(new FileSystemEntryChange
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

        return delta;
    }
}
