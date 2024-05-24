﻿using DatagentMonitor.FileSystem;
using System.Text;

namespace DatagentMonitor.Utils;

internal class Synchronizer
{
    private readonly SynchronizationSourceManager _sourceManager;
    public SynchronizationSourceManager SourceManager => _sourceManager;

    private readonly SynchronizationSourceManager _targetManager;
    public SynchronizationSourceManager TargetManager => _targetManager;

    public Synchronizer(
        SynchronizationSourceManager sourceManager, 
        SynchronizationSourceManager targetManager)
    {
        _sourceManager = sourceManager;
        _targetManager = targetManager;
    }

    public Synchronizer(SynchronizationSourceManager sourceManager, string targetRoot) : 
        this(sourceManager, 
             new SynchronizationSourceManager(targetRoot)) { }

    public Synchronizer(string sourceRoot, string targetRoot) : 
        this(new SynchronizationSourceManager(sourceRoot), 
             new SynchronizationSourceManager(targetRoot)) { }

    public void Run(
        out List<NamedEntryChange> appliedSource, 
        out List<NamedEntryChange> failedSource, 
        out List<NamedEntryChange> appliedTarget, 
        out List<NamedEntryChange> failedTarget)
    {
        GetIndexChanges(
            out var sourceToIndex,
            out var targetToIndex);

        GetRelativeChanges(
            sourceToIndex, 
            targetToIndex, 
            out var sourceToTarget, 
            out var targetToSource);

        ApplyChanges(
            _sourceManager.Root, _targetManager.Root,
            sourceToTarget, targetToSource,
            out appliedSource, out failedSource,
            out appliedTarget, out failedTarget);

        Console.WriteLine($"Source-to-Target:");
        Console.WriteLine($"\tApplied: {appliedSource.Count}");
        Console.WriteLine($"\tFailed: {failedSource.Count}");

        Console.WriteLine($"Target-to-Source:");
        Console.WriteLine($"\tApplied: {appliedTarget.Count}");
        Console.WriteLine($"\tFailed: {failedTarget.Count}");

        // TODO: index has to be updated in the following manner:
        // 1. Index trie is built upon sourceToIndex and targetToIndex
        //      1.1. Changes inside the intersection are compared and copied to the index trie according to their priorities
        //      1.2. Changes outside the intersection are copied to the index trie as-is
        // 2. Changes from the index trie are applied directly to the source (!) index file
        // 3*. New source index file then replaces the old target index file

        // Generate the new index based on the old one, according to the rule:
        // s(d(S_0) + d(ΔS)) = S_0 + ΔS
        // where s(x) and d(x) stand for serialization and deserialization routines resp
        //
        // TODO: deserialization is happening twice: here and in GetTargetDelta;
        // re-use the already deserialized index
        //var index = _sourceManager.DeserializeIndex();
        //index.MergeChanges(appliedTarget);
        //_sourceManager.SerializeIndex(index);

        // TODO: propose possible workarounds for failed changes

        //_targetManager.SyncDatabase.LastSyncTime = DateTime.Now;
        _sourceManager.SyncDatabase.ClearEvents();

        Console.WriteLine("Synchronization complete.");
    }

    private void GetIndexChanges(
        out FileSystemTrie sourceToIndex, 
        out FileSystemTrie targetToIndex)
    {
        Console.Write($"Resolving source changes... ");
        sourceToIndex = GetSourceDelta();
        Console.WriteLine($"Total: {sourceToIndex.Count}");

        Console.Write($"Resolving target changes... ");
        targetToIndex = GetTargetDelta();
        Console.WriteLine($"Total: {targetToIndex.Count}");
    }

    private void GetRelativeChanges(
        FileSystemTrie sourceToIndex, 
        FileSystemTrie targetToIndex, 
        out FileSystemTrie sourceToTarget, 
        out FileSystemTrie targetToSource)
    {
        sourceToTarget = new(stack: false);
        targetToSource = new(stack: false);

        int levels = Math.Max(sourceToIndex.Levels.Count, targetToIndex.Levels.Count);
        for (int level = 0; level < levels; level++)
        {
            CorrelateTrieLevels(
                _sourceManager.Root, _targetManager.Root, 
                sourceToIndex, targetToIndex, 
                sourceToTarget, targetToSource, 
                level: level);
            CorrelateTrieLevels(
                _targetManager.Root, _sourceManager.Root, 
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

    private static void CorrelateTrieLevels(
        string sourceRoot, 
        string targetRoot, 
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
            if (targetToIndex.TryGetNode(sourceNode.OldPath, out var targetNode))
            {
                var targetChange = targetNode.Value;
                if (targetChange != null && flags.HasFlag(CorrelationFlags.DisallowExactMatch))
                    throw new ArgumentException($"Dangling change: {targetChange}");

                // TODO: this does (falsely) assume that the source change
                // always has the same kind (file/directory) as the target change; fix it
                // Related test:
                // Source: Create folder1/subfolder1 (directory)
                // Target: Create folder1/subfolder1: <time>, <size> (file)
                switch (sourceNode.Type)
                {
                    case FileSystemEntryType.Directory:
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
                                    sourceRoot, targetRoot,
                                    sourceNode, targetNode,
                                    sourceToTarget, targetToSource,
                                    (s, t) => s.Value >= t.Value);
                                break;

                            case (FileSystemEntryAction.Rename, FileSystemEntryAction.Delete):
                                ResolveDirectoryConflict(
                                    sourceRoot, targetRoot,
                                    sourceNode, targetNode,
                                    sourceToTarget, targetToSource,
                                    (s, t) => s.PriorityValue >= t.Value);
                                break;

                            case (FileSystemEntryAction.Delete, null):
                            case (FileSystemEntryAction.Delete, FileSystemEntryAction.Rename):
                                ResolveDirectoryConflict(
                                    sourceRoot, targetRoot,
                                    sourceNode, targetNode,
                                    sourceToTarget, targetToSource,
                                    // When initial source and target trie's arguments are swapped, 
                                    // if this predicate produces equality, initial target will be favored instead of initial source
                                    // TODO: add more specific predicates that would respect the order of arguments via, e.g., CorrelationFlags
                                    (s, t) => s.Value >= t.PriorityValue);
                                break;
                        }
                        break;

                    case FileSystemEntryType.File:
                        if (targetChange == null)
                            throw new ArgumentException($"File change was null: {sourceNode.Path}");

                        switch (sourceChange.Action, targetChange.Action)
                        {
                            case (FileSystemEntryAction.Create, FileSystemEntryAction.Create):
                                ResolveFileConflict(
                                    sourceNode, targetNode,
                                    sourceToTarget, targetToSource,
                                    (s, t) => s.Value >= t.Value);
                                break;

                            case (FileSystemEntryAction.Create, _):
                            case (_, FileSystemEntryAction.Create):
                                throw new InvalidConflictException(sourceChange.Action, targetChange.Action);

                            case (FileSystemEntryAction.Delete, FileSystemEntryAction.Delete):
                                break;

                            default:
                                ResolveFileConflict(
                                    sourceNode, targetNode,
                                    sourceToTarget, targetToSource,
                                    (s, t) => s.Value >= t.Value);
                                break;
                        }
                        break;
                }

                if (targetChange is not null)
                    targetNode.Clear();
            }
            else
            {
                if (targetNode.Value is not null)
                    throw new ArgumentException($"Dangling change: {targetNode.Value}");

                sourceToTarget.Add(new NamedEntryChange(sourceNode.OldPath, sourceChange.Action)
                {
                    RenameProperties = sourceChange.RenameProperties,
                    ChangeProperties = sourceChange.ChangeProperties
                });
            }
        }
    }

    private static void ResolveDirectoryConflict(
        string sourceRoot, 
        string targetRoot, 
        FileSystemTrieNode sourceNode,
        FileSystemTrieNode targetNode, 
        FileSystemTrie sourceToTarget,
        FileSystemTrie targetToSource,
        Func<FileSystemTrieNode, FileSystemTrieNode, bool> predicate)
    {
        // Source-to-Target
        if (predicate(sourceNode, targetNode))
        {
            var changes = ResolveDirectoryConflictExact(sourceRoot, sourceNode, targetNode);
            foreach (var change in changes)
                sourceToTarget.Add(change);
        }
        // Target-to-Source
        else
        {
            var changes = ResolveDirectoryConflictExact(targetRoot, targetNode, sourceNode);
            foreach (var change in changes)
                targetToSource.Add(change);
        }
    }

    private static List<NamedEntryChange> ResolveDirectoryConflictExact(
        string sourceRoot,
        FileSystemTrieNode sourceNode,
        FileSystemTrieNode targetNode)
    {
        var result = new List<NamedEntryChange>();
        var sourceChange = sourceNode.Value;
        var targetChange = targetNode.Value;
        switch (sourceChange?.Action, targetChange?.Action)
        {
            case (FileSystemEntryAction.Rename, null):
            case (FileSystemEntryAction.Rename, FileSystemEntryAction.Rename):
                result.Add(new NamedEntryChange(targetNode.Path, FileSystemEntryAction.Rename)
                {
                    RenameProperties = sourceChange.RenameProperties,
                    ChangeProperties = sourceChange.ChangeProperties
                });

                // Notify the counterpart subtree that the directory was renamed
                targetNode.Name = sourceChange.RenameProperties!.Value.Name;
                break;

            case (null, FileSystemEntryAction.Delete):
            case (FileSystemEntryAction.Rename, FileSystemEntryAction.Delete):
                var subpath = sourceNode.Path;
                OnDirectoryCreated(new DirectoryInfo(Path.Combine(sourceRoot, subpath)), new StringBuilder(subpath), result);

                // Only remove the subtree; the node itself will get removed later
                sourceNode.ClearSubtree();
                break;            

            case (FileSystemEntryAction.Delete, null):
            case (FileSystemEntryAction.Delete, FileSystemEntryAction.Rename):
                result.Add(new NamedEntryChange(targetNode.Path, FileSystemEntryAction.Delete));

                targetNode.ClearSubtree();
                break;
        }

        return result;
    }

    private static void ResolveFileConflict(
        FileSystemTrieNode sourceNode,
        FileSystemTrieNode targetNode,
        FileSystemTrie sourceToTarget,
        FileSystemTrie targetToSource,
        Func<FileSystemTrieNode, FileSystemTrieNode, bool> predicate)
    {
        // Source-to-Target
        if (predicate(sourceNode, targetNode))
        {
            ResolveFileConflictExact(sourceNode, targetNode, sourceToTarget, targetToSource);
        }
        // Target-to-Source
        else
        {
            ResolveFileConflictExact(targetNode, sourceNode, targetToSource, sourceToTarget);
        }
    }

    private static void ResolveFileConflictExact(
        FileSystemTrieNode sourceNode, 
        FileSystemTrieNode targetNode, 
        FileSystemTrie sourceToTarget, 
        FileSystemTrie targetToSource)
    {
        var sourceChange = sourceNode.Value!;
        var targetChange = targetNode.Value!;
        switch (sourceChange.Action, targetChange.Action)
        {
            // TODO: no conflict if both renames have the same new name; fix and add test
            case (FileSystemEntryAction.Create, FileSystemEntryAction.Create):
            case (FileSystemEntryAction.Rename, FileSystemEntryAction.Rename):
            case (FileSystemEntryAction.Rename, FileSystemEntryAction.Change):
            case (FileSystemEntryAction.Change, FileSystemEntryAction.Rename):
            case (FileSystemEntryAction.Change, FileSystemEntryAction.Change):
                sourceToTarget.Add(new NamedEntryChange(
                    targetNode.Path, 
                    sourceChange.ChangeProperties is null ?
                        FileSystemEntryAction.Rename :
                        FileSystemEntryAction.Change)
                {
                    RenameProperties = sourceChange.RenameProperties,
                    ChangeProperties = sourceChange.ChangeProperties
                });

                var renameProps = sourceChange.RenameProperties is null ? targetChange.RenameProperties : null;
                var changeProps = sourceChange.ChangeProperties is null ? targetChange.ChangeProperties : null;
                if (renameProps is not null || changeProps is not null)
                    targetToSource.Add(new NamedEntryChange(
                        sourceNode.Path, 
                        changeProps is null ?
                            FileSystemEntryAction.Rename :
                            FileSystemEntryAction.Change)
                    {
                        RenameProperties = renameProps,
                        ChangeProperties = changeProps
                    });
                break;

            case (FileSystemEntryAction.Rename, FileSystemEntryAction.Delete):
            case (FileSystemEntryAction.Change, FileSystemEntryAction.Delete):
                var sourceFileInfo = new FileInfo(sourceNode.Path);
                sourceToTarget.Add(new NamedEntryChange(sourceNode.Path, FileSystemEntryAction.Create)
                {
                    ChangeProperties = new ChangeProperties
                    {
                        LastWriteTime = sourceFileInfo.LastWriteTime,
                        Length = sourceFileInfo.Length
                    }
                });
                break;

            case (FileSystemEntryAction.Delete, FileSystemEntryAction.Rename):
            case (FileSystemEntryAction.Delete, FileSystemEntryAction.Change):
                sourceToTarget.Add(new NamedEntryChange(targetNode.Path, FileSystemEntryAction.Delete));
                break;
        }
    }

    private static void ApplyChanges(
        string sourceRoot, 
        string targetRoot, 
        FileSystemTrie sourceToTarget,
        FileSystemTrie targetToSource, 
        out List<NamedEntryChange> appliedSource,
        out List<NamedEntryChange> failedSource,
        out List<NamedEntryChange> appliedTarget,
        out List<NamedEntryChange> failedTarget)
    {
        appliedSource = new(); failedSource = new();
        appliedTarget = new(); failedTarget = new();
        int levels = Math.Max(sourceToTarget.Levels.Count, targetToSource.Levels.Count);
        for (int level = 0; level < levels; level++)
        {
            SplitChanges(sourceToTarget, level, out var sourceRenames, out var sourceOthers);
            SplitChanges(targetToSource, level, out var targetRenames, out var targetOthers);

            ApplyChanges(sourceRoot, targetRoot, sourceRenames, targetToSource, appliedSource, failedSource);
            ApplyChanges(targetRoot, sourceRoot, targetRenames, sourceToTarget,appliedTarget, failedTarget);

            ApplyChanges(sourceRoot, targetRoot, sourceOthers, targetToSource, appliedSource, failedSource);
            ApplyChanges(targetRoot, sourceRoot, targetOthers, sourceToTarget, appliedTarget, failedTarget);
        }
    }

    private static void SplitChanges(
        FileSystemTrie changes, int level, 
        out List<FileSystemTrieNode> renames, 
        out List<FileSystemTrieNode> others)
    {
        renames = new(); others = new();
        foreach (var change in changes.TryGetLevel(level))
        {
            if (change.Value!.RenameProperties != null)
                renames.Add(change);
            else
                others.Add(change);
        }
    }

    private static void ApplyChanges(
        string sourceRoot, 
        string targetRoot, 
        IEnumerable<FileSystemTrieNode> sourceNodes, 
        FileSystemTrie targetToSource, 
        List<NamedEntryChange> applied, 
        List<NamedEntryChange> failed)
    {
        foreach (var sourceNode in sourceNodes)
        {
            // TODO: use database for applied/failed entries instead of in-memory structures
            var sourceChange = sourceNode.Value!;
            if (ApplyChange(sourceRoot, targetRoot, sourceChange))
            {
                applied.Add(sourceChange);
                if (sourceChange.RenameProperties != null && 
                    targetToSource.TryGetNode(sourceNode.OldPath, out var targetNode))
                    targetNode.Name = sourceNode.Name;
            }
            else
            {
                failed.Add(sourceChange);
            }
        }
    }

    private static bool ApplyChange(string sourceRoot, string targetRoot, NamedEntryChange change)
    {
        Console.WriteLine($"{sourceRoot} -> {targetRoot}: [{change.Action}] {change.Path})");
        var renameProps = change.RenameProperties;
        var changeProps = change.ChangeProperties;
        string Rename(string path) => CustomFileSystemInfo.ReplaceEntryName(path, renameProps!.Value.Name);

        var sourcePath = Path.Combine(sourceRoot, change.Path);
        var targetPath = Path.Combine(targetRoot, change.Path);
        switch (CustomFileSystemInfo.GetEntryType(sourcePath))
        {
            case FileSystemEntryType.Directory:
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
                break;

            case FileSystemEntryType.File:
                FileInfo sourceFile, targetFile;
                switch (change.Action)
                {
                    case FileSystemEntryAction.Create:
                        sourceFile = new FileInfo(sourcePath);

                        // Source file is not present -> the change is invalid
                        if (!sourceFile.Exists)
                            return false;

                        // Source file is altered -> the change is invalid
                        if (sourceFile.LastWriteTime.TrimMicroseconds() != changeProps!.Value.LastWriteTime ||
                            sourceFile.Length != changeProps.Value.Length)
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
                        if (sourceFile.LastWriteTime.TrimMicroseconds() != changeProps!.Value.LastWriteTime ||
                            sourceFile.Length != changeProps.Value.Length)
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
                break;
        }

        return true;
    }

    private FileSystemTrie GetSourceDelta() => new(_sourceManager.SyncDatabase.GetEvents());

    private FileSystemTrie GetTargetDelta()
    {
        var sourceDir = _sourceManager.Index.Deserialize();
        var targetDir = new DirectoryInfo(_targetManager.Root);
        var builder = new StringBuilder();
        var delta = new FileSystemTrie();
        GetTargetDelta(sourceDir, targetDir, builder, delta, timestamp: _targetManager.SyncDatabase.LastSyncTime);
        return delta;
    }

    private void GetTargetDelta(CustomDirectoryInfo sourceDir, DirectoryInfo targetDir, StringBuilder builder, FileSystemTrie delta, DateTime? timestamp = null)
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
            delta.Add(new NamedEntryChange(builder.ToString(), FileSystemEntryAction.Delete)
            {
                Timestamp = timestamp ?? DateTime.MinValue
            });
        }

        foreach (var targetFile in builder.Wrap(targetDir.EnumerateFiles(), f => f.Name))
        {
            var targetLastWriteTime = targetFile.LastWriteTime.TrimMicroseconds();
            if (sourceDir.Files.Remove(targetFile.Name, out var sourceFile))
            {
                if (targetLastWriteTime != sourceFile.LastWriteTime || targetFile.Length != sourceFile.Length)
                {
                    delta.Add(new NamedEntryChange(builder.ToString(), FileSystemEntryAction.Change)
                    {
                        Timestamp = timestamp ?? targetLastWriteTime,
                        ChangeProperties = new ChangeProperties
                        {
                            LastWriteTime = targetLastWriteTime,
                            Length = targetFile.Length
                        }
                    });
                }
            }
            else
            {
                delta.Add(new NamedEntryChange(builder.ToString(), FileSystemEntryAction.Create)
                {
                    Timestamp = timestamp ?? targetLastWriteTime,
                    ChangeProperties = new ChangeProperties
                    {
                        LastWriteTime = targetLastWriteTime,
                        Length = targetFile.Length
                    }
                });
            }
        }

        foreach (var _ in builder.Wrap(sourceDir.Files, f => f.Name))
        {
            delta.Add(new NamedEntryChange(builder.ToString(), FileSystemEntryAction.Delete)
            {
                Timestamp = timestamp ?? DateTime.MinValue
            });
        }
    }

    // TODO: this method is present in two instances: here and in SynchronizationManager; unify
    private static void OnDirectoryCreated(DirectoryInfo root, StringBuilder builder, ICollection<NamedEntryChange> container, DateTime? timestamp = null)
    {
        container.Add(new NamedEntryChange(builder.ToString(), FileSystemEntryAction.Create)
        {
            Timestamp = timestamp ?? root.LastWriteTime
        });

        foreach (var directory in builder.Wrap(root.EnumerateDirectories(), d => d.Name + Path.DirectorySeparatorChar))
        {
            OnDirectoryCreated(directory, builder, container, timestamp);
        }

        foreach (var file in builder.Wrap(root.EnumerateFiles(), f => f.Name))
        {
            container.Add(new NamedEntryChange(builder.ToString(), FileSystemEntryAction.Create)
            {
                Timestamp = timestamp ?? file.LastWriteTime,
                ChangeProperties = new ChangeProperties
                {
                    LastWriteTime = file.LastWriteTime.TrimMicroseconds(),
                    Length = file.Length
                }
            });
        }
    }
}
