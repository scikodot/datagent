﻿using DatagentMonitor.FileSystem;
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
        sourceToTarget = new(stack: false);
        targetToSource = new(stack: false);

        Console.Write("Target latest sync timestamp: ");
        var lastSyncTimestamp = GetTargetLastSyncTimestamp(_targetManager.EventsDatabase);
        Console.WriteLine(lastSyncTimestamp != null ? lastSyncTimestamp.Value.ToString(CustomFileInfo.DateTimeFormat) : "N/A");

        int levels = Math.Max(sourceToIndex.Levels.Count, targetToIndex.Levels.Count);
        for (int level = 0; level < levels; level++)
        {
            CorrelateTrieLevels(
                sourceToIndex, targetToIndex, 
                sourceToTarget, targetToSource, 
                level: level);
            CorrelateTrieLevels(
                targetToIndex, sourceToIndex,
                targetToSource, sourceToTarget,
                level: level, flags: CorrelationFlags.Swap | CorrelationFlags.DisallowExactMatch);
        }
    }

    [Flags]
    private enum CorrelationFlags
    {
        None = 0,
        Swap = 1,
        DisallowExactMatch = 2
    }

    private void CorrelateTrieLevels(
        FileSystemTrie sourceToIndex,
        FileSystemTrie targetToIndex,
        FileSystemTrie sourceToTarget,
        FileSystemTrie targetToSource,
        int level,
        CorrelationFlags flags = CorrelationFlags.None)
    {
        foreach (var sourceNode in sourceToIndex.TryPopLevel(level))
        {
            var sourceChange = sourceNode.Value ?? throw new ArgumentException("Tracked change was null.");
            if (targetToIndex.TryGetNode(sourceChange.Path, out var targetNode))
            {
                var targetChange = targetNode.Value;
                if (targetChange != null && flags.HasFlag(CorrelationFlags.DisallowExactMatch))
                    throw new ArgumentException($"Dangling change: {targetChange}");

                // TODO: this does (falsely) assume that the source change
                // always has the same kind (file/directory) as the target change; fix it
                // Related test:
                // Source: Create folder1/subfolder1 (directory)
                // Target: Create folder1/subfolder1: <time>, <size> (file)
                if (CustomFileSystemInfo.IsDirectory(sourceChange.Path))
                {
                    switch (sourceChange.Action, targetChange?.Action)
                    {
                        case (FileSystemEntryAction.Create, FileSystemEntryAction.Create):
                        case (FileSystemEntryAction.Delete, FileSystemEntryAction.Delete):
                            break;

                        case (FileSystemEntryAction.Create, _):
                        case (_, FileSystemEntryAction.Create):
                            throw new InvalidConflictException(sourceChange.Action, targetChange?.Action);

                        case (FileSystemEntryAction.Rename, null):
                        case (FileSystemEntryAction.Rename, FileSystemEntryAction.Rename):
                            ResolveDirectoryConflict(
                                sourceNode, targetNode,
                                sourceToTarget, targetToSource,
                                (s, t) => t.Value == null || s.Value!.Timestamp >= t.Value.Timestamp);
                            break;

                        case (FileSystemEntryAction.Rename, FileSystemEntryAction.Delete):
                            ResolveDirectoryConflict(
                                sourceNode, targetNode,
                                sourceToTarget, targetToSource,
                                (s, t) => s.PriorityValue!.Timestamp >= t.Value!.Timestamp);
                            break;

                        case (FileSystemEntryAction.Delete, null):
                        case (FileSystemEntryAction.Delete, FileSystemEntryAction.Rename):
                            ResolveDirectoryConflict(
                                sourceNode, targetNode,
                                sourceToTarget, targetToSource,
                                // When initial source and target trie's arguments are swapped, 
                                // if this predicate produces equality, initial target will be favored instead of initial source
                                // TODO: add more specific predicates that would respect the order of arguments via, e.g., CorrelationFlags
                                (s, t) => t.PriorityValue == null || s.Value!.Timestamp >= t.PriorityValue.Timestamp);
                            break;
                    }
                }
                else
                {
                    if (targetChange == null)
                        throw new ArgumentException($"File change was null: {sourceChange.Path}");

                    switch (sourceChange.Action, targetChange.Action)
                    {
                        case (FileSystemEntryAction.Create, FileSystemEntryAction.Create):
                            ResolveFileConflict(
                                sourceChange, targetChange,
                                sourceToTarget, targetToSource,
                                (s, t) => s.Timestamp >= t.Timestamp);
                            break;

                        case (FileSystemEntryAction.Create, _):
                        case (_, FileSystemEntryAction.Create):
                            throw new InvalidConflictException(sourceChange.Action, targetChange.Action);

                        case (FileSystemEntryAction.Delete, FileSystemEntryAction.Delete):
                            break;

                        default:
                            ResolveFileConflict(
                                sourceChange, targetChange,
                                sourceToTarget, targetToSource,
                                (s, t) => s.Timestamp >= t.Timestamp);
                            break;
                    }
                }

                if (targetChange != null)
                    targetNode.Clear();
            }
            else
            {
                if (targetNode.Value != null)
                    throw new ArgumentException($"Dangling change: {targetNode.Value}");

                sourceToTarget.Add(sourceChange);
            }
        }
    }

    private void ResolveDirectoryConflict(
        FileSystemTrieNode sourceNode,
        FileSystemTrieNode targetNode, 
        FileSystemTrie sourceToTarget,
        FileSystemTrie targetToSource,
        Func<FileSystemTrieNode, FileSystemTrieNode, bool> predicate)
    {
        // Source-to-Target
        if (predicate(sourceNode, targetNode))
        {
            var changes = ResolveDirectoryConflictExact(_sourceManager.Root, sourceNode, targetNode);
            foreach (var change in changes)
                sourceToTarget.Add(change);
        }
        // Target-to-Source
        else
        {
            var changes = ResolveDirectoryConflictExact(_targetManager.Root, targetNode, sourceNode);
            foreach (var change in changes)
                targetToSource.Add(change);
        }
    }

    private static List<FileSystemEntryChange> ResolveDirectoryConflictExact(
        string sourceRoot,
        FileSystemTrieNode sourceNode,
        FileSystemTrieNode targetNode)
    {
        var result = new List<FileSystemEntryChange>();
        var sourceChange = sourceNode.Value;
        var targetChange = targetNode.Value;
        switch (sourceChange?.Action, targetChange?.Action)
        {
            case (FileSystemEntryAction.Rename, null):
            case (FileSystemEntryAction.Rename, FileSystemEntryAction.Rename):
                result.Add(new FileSystemEntryChange
                {
                    Path = targetChange == null ? sourceChange.Path : 
                               CustomFileSystemInfo.ReplaceEntryName(sourceChange.Path, targetChange.Properties.RenameProps!.Name),
                    Action = FileSystemEntryAction.Rename,
                    Properties = sourceChange.Properties
                });

                // Notify the counterpart subtree that the directory was renamed
                targetNode.MoveTo(sourceChange.Properties.RenameProps!.Name);
                break;

            case (null, FileSystemEntryAction.Delete):
            case (FileSystemEntryAction.Rename, FileSystemEntryAction.Delete):
                var subpath = sourceChange == null ? targetChange.Path :
                                  CustomFileSystemInfo.ReplaceEntryName(targetChange.Path, sourceChange.Properties.RenameProps!.Name);
                OnDirectoryCreated(new DirectoryInfo(Path.Combine(sourceRoot, subpath)), new StringBuilder(subpath), result);

                sourceNode.ClearSubtree();
                break;            

            case (FileSystemEntryAction.Delete, null):
            case (FileSystemEntryAction.Delete, FileSystemEntryAction.Rename):
                result.Add(new FileSystemEntryChange
                {
                    Path = targetChange == null ? sourceChange.Path :
                               CustomFileSystemInfo.ReplaceEntryName(sourceChange.Path, targetChange.Properties.RenameProps!.Name),
                    Action = FileSystemEntryAction.Delete
                });

                targetNode.ClearSubtree();
                break;
        }

        return result;
    }

    private void ResolveFileConflict(
        FileSystemEntryChange sourceChange,
        FileSystemEntryChange targetChange,
        FileSystemTrie sourceToTarget,
        FileSystemTrie targetToSource,
        Func<FileSystemEntryChange, FileSystemEntryChange, bool> predicate)
    {
        // Source-to-Target
        if (predicate(sourceChange, targetChange))
        {
            var change = ResolveFileConflictExact(sourceChange, targetChange);
            if (change is not null)
                sourceToTarget.Add(change);
        }
        // Target-to-Source
        else
        {
            var change = ResolveFileConflictExact(targetChange, sourceChange);
            if (change is not null)
                targetToSource.Add(change);
        }
    }

    private static FileSystemEntryChange? ResolveFileConflictExact(
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
