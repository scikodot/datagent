using DatagentMonitor.Collections;
using DatagentMonitor.FileSystem;

namespace DatagentMonitor.Utils;

internal class Synchronizer
{
    private readonly SyncSourceManager _sourceManager;
    public SyncSourceManager SourceManager => _sourceManager;

    private readonly SyncSourceManager _targetManager;
    public SyncSourceManager TargetManager => _targetManager;

    public Synchronizer(
        SyncSourceManager sourceManager, 
        SyncSourceManager targetManager)
    {
        _sourceManager = sourceManager;
        _targetManager = targetManager;
    }

    public Synchronizer(SyncSourceManager sourceManager, string targetRoot) : 
        this(sourceManager, 
             new SyncSourceManager(targetRoot)) { }

    public Synchronizer(string sourceRoot, string targetRoot) : 
        this(new SyncSourceManager(sourceRoot), 
             new SyncSourceManager(targetRoot)) { }

    // TODO: guarantee robustness, i.e. that even if the program crashes,
    // events, applied/failed changes, etc. will not get lost
    public void Run(
        out List<EntryChange> appliedSource, 
        out List<EntryChange> failedSource, 
        out List<EntryChange> appliedTarget, 
        out List<EntryChange> failedTarget)
    {
        GetIndexChanges(
            out var sourceToIndex,
            out var targetToIndex);

        var sourceTotal = sourceToIndex.Count;

        GetRelativeChanges(
            sourceToIndex, 
            targetToIndex, 
            out var sourceToTarget, 
            out var targetToSource);

        ApplyChanges(
            sourceToTarget, targetToSource,
            out appliedSource, out failedSource,
            out appliedTarget, out failedTarget);

        sourceTotal += appliedTarget.Count;

        Console.WriteLine($"Source-to-Target:");
        Console.WriteLine($"\tApplied: {appliedSource.Count}");
        Console.WriteLine($"\tFailed: {failedSource.Count}");

        Console.WriteLine($"Target-to-Source:");
        Console.WriteLine($"\tApplied: {appliedTarget.Count}");
        Console.WriteLine($"\tFailed: {failedTarget.Count}");

        // Merge changes applied to the source into the source index
        // Note: this will only work if Index.Root is the *actual* state of the root
        if (appliedTarget.Count > 0)
            _sourceManager.Index.MergeChanges(appliedTarget);

        // Update the index file if there were any changes made
        // (either on the source itself or applied from the target)
        if (sourceTotal > 0)
            _sourceManager.Index.Serialize(out _);

        // Copy the new index to the target
        _sourceManager.Index.CopyTo(_targetManager.Index.Path);

        // TODO: propose possible workarounds for failed changes

        _targetManager.SyncDatabase.LastSyncTime = DateTime.Now;
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
                _sourceManager, _targetManager, 
                sourceToIndex, targetToIndex, 
                sourceToTarget, targetToSource, 
                level: level);
            CorrelateTrieLevels(
                _targetManager, _sourceManager, 
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
        SyncSourceManager sourceManager, 
        SyncSourceManager targetManager, 
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

                // Total: 80 cases
                switch (sourceNode.Type, sourceChange.Action, targetNode.Type, targetChange?.Action)
                {
                    // No-op; 3 cases
                    case (EntryType.Directory, EntryAction.Create, EntryType.Directory, EntryAction.Create):
                    case (EntryType.Directory, EntryAction.Delete, EntryType.Directory, EntryAction.Delete):
                    case (EntryType.File, EntryAction.Delete, EntryType.File, EntryAction.Delete):
                        break;

                    // Subtree-dependent conflicts; 3 cases
                    case (EntryType.Directory, EntryAction.Rename, EntryType.Directory, EntryAction.Delete):
                    case (EntryType.Directory, EntryAction.Delete, EntryType.Directory, null):
                    case (EntryType.Directory, EntryAction.Delete, EntryType.Directory, EntryAction.Rename):
                        ResolveConflict(
                            sourceManager, targetManager,
                            sourceNode, targetNode,
                            sourceToTarget, targetToSource,
                            (s, t) => flags.HasFlag(CorrelationFlags.Swap) ?
                                s.PriorityValue > t.PriorityValue : 
                                s.PriorityValue >= t.PriorityValue);
                        break;

                    // Subtree-independent conflicts; 11 cases
                    case (EntryType.Directory, EntryAction.Rename, EntryType.Directory, null):
                    case (EntryType.Directory, EntryAction.Rename, EntryType.Directory, EntryAction.Rename):
                    case (EntryType.File, EntryAction.Create, EntryType.File, EntryAction.Create):
                    case (EntryType.File, EntryAction.Rename, EntryType.File, EntryAction.Rename):
                    case (EntryType.File, EntryAction.Rename, EntryType.File, EntryAction.Change):
                    case (EntryType.File, EntryAction.Rename, EntryType.File, EntryAction.Delete):
                    case (EntryType.File, EntryAction.Change, EntryType.File, EntryAction.Rename):
                    case (EntryType.File, EntryAction.Change, EntryType.File, EntryAction.Change):
                    case (EntryType.File, EntryAction.Change, EntryType.File, EntryAction.Delete):
                    case (EntryType.File, EntryAction.Delete, EntryType.File, EntryAction.Rename):
                    case (EntryType.File, EntryAction.Delete, EntryType.File, EntryAction.Change):
                        ResolveConflict(
                            sourceManager, targetManager,
                            sourceNode, targetNode,
                            sourceToTarget, targetToSource,
                            (s, t) => s.Value >= t.Value);
                        break;

                    // Different types conflicts; 9 cases
                    // TODO: add the following test
                    // Source: Rename file1 -> file1-renamed-source (file)
                    // Target: Create file1-renamed-source (file or directory; this test must present a conflict)
                    case (EntryType.Directory, EntryAction.Create, EntryType.File, EntryAction.Create):
                    case (EntryType.Directory, EntryAction.Create, EntryType.File, EntryAction.Rename):
                    case (EntryType.Directory, EntryAction.Create, EntryType.File, EntryAction.Change):
                    case (EntryType.Directory, EntryAction.Rename, EntryType.File, EntryAction.Create):
                    case (EntryType.File, EntryAction.Create, EntryType.Directory, null):
                    case (EntryType.File, EntryAction.Create, EntryType.Directory, EntryAction.Create):
                    case (EntryType.File, EntryAction.Create, EntryType.Directory, EntryAction.Rename):
                    case (EntryType.File, EntryAction.Rename, EntryType.Directory, EntryAction.Create):
                    case (EntryType.File, EntryAction.Change, EntryType.Directory, EntryAction.Create):
                        ResolveConflict(
                            sourceManager, targetManager,
                            sourceNode, targetNode,
                            sourceToTarget, targetToSource,
                            // TODO: consider improving the predicate,
                            // e.g. by lowering the priority of empty directories
                            (s, t) => s.PriorityValue >= t.PriorityValue);
                        break;

                    // Different types but the entry is created in place of a deleted one; 4 cases
                    // Resolve in favor of the newly created entry; its counterpart is deleted anyway
                    case (EntryType.Directory, EntryAction.Create, EntryType.File, EntryAction.Delete):
                    case (EntryType.Directory, EntryAction.Delete, EntryType.File, EntryAction.Create):
                    case (EntryType.File, EntryAction.Create, EntryType.Directory, EntryAction.Delete):
                    case (EntryType.File, EntryAction.Delete, EntryType.Directory, EntryAction.Create):
                        ResolveConflict(
                            sourceManager, targetManager,
                            sourceNode, targetNode,
                            sourceToTarget, targetToSource,
                            (s, t) => s.Value!.Action is EntryAction.Create);
                        break;

                    // Directory change not allowed; 16 cases
                    case (EntryType.Directory, EntryAction.Change, _, _):
                    case (_, _, EntryType.Directory, EntryAction.Change):

                    // File change cannot be null; 8 cases
                    case (_, _, EntryType.File, null):

                    // A new entry created when another one already exists at the same path; 11 cases
                    // TODO: (Rename, Create) should be handled as a (Rename, Rename), 
                    // because it might mean that the file has got renamed on the target
                    case (_, EntryAction.Create, _, not EntryAction.Create):
                    case (_, not EntryAction.Create, _, EntryAction.Create):

                    // Two entries at the same path but of different types; 15 cases
                    case (EntryType.Directory, not EntryAction.Create, EntryType.File, not EntryAction.Create):
                    case (EntryType.File, not EntryAction.Create, EntryType.Directory, not EntryAction.Create):
                        throw new InvalidConflictException(
                            sourceNode.Type, sourceChange.Action,
                            targetNode.Type, targetChange?.Action);
                }

                if (targetChange is not null)
                    targetNode.Clear();
            }
            else
            {
                if (targetNode.Value is not null)
                    throw new ArgumentException($"Dangling change: {targetNode.Value}");

                sourceToTarget.Add(sourceChange);
            }
        }
    }

    private static void ResolveConflict(
        SyncSourceManager sourceManager,
        SyncSourceManager targetManager,
        FileSystemTrieNode sourceNode,
        FileSystemTrieNode targetNode,
        FileSystemTrie sourceToTarget,
        FileSystemTrie targetToSource,
        Func<FileSystemTrieNode, FileSystemTrieNode, bool> predicate)
    {
        if (predicate(sourceNode, targetNode))
        {
            ResolveConflictExact(sourceManager, targetManager, sourceNode, targetNode, sourceToTarget, targetToSource);
        }
        else
        {
            ResolveConflictExact(targetManager, sourceManager, targetNode, sourceNode, targetToSource, sourceToTarget);
        }
    }

    private static void ResolveConflictExact(
        SyncSourceManager sourceManager,
        SyncSourceManager targetManager,
        FileSystemTrieNode sourceNode,
        FileSystemTrieNode targetNode,
        FileSystemTrie sourceToTarget,
        FileSystemTrie targetToSource)
    {
        var sourceChange = sourceNode.Value;
        var targetChange = targetNode.Value;
        switch (sourceNode.Type, sourceChange?.Action, targetNode.Type, targetChange?.Action)
        {
            case (EntryType.Directory, EntryAction.Rename, EntryType.Directory, null):
            case (EntryType.Directory, EntryAction.Rename, EntryType.Directory, EntryAction.Rename):
                sourceToTarget.Add(new EntryChange(
                    sourceChange.Timestamp, targetNode.Path,
                    targetNode.Type, EntryAction.Rename,
                    sourceChange.RenameProperties, sourceChange.ChangeProperties));

                // Notify the counterpart subtree that the directory was renamed
                targetNode.Name = sourceChange.RenameProperties!.Value.Name;
                break;

            case (EntryType.Directory, null, EntryType.Directory, EntryAction.Delete):
            case (EntryType.Directory, EntryAction.Rename, EntryType.Directory, EntryAction.Delete):
            case (EntryType.Directory, null, EntryType.File, EntryAction.Create):
                var directory = new DirectoryInfo(Path.Combine(sourceManager.Root, sourceNode.Path));
                sourceToTarget.AddRange(sourceManager.EnumerateCreatedDirectory(directory));

                // Only remove the subtree; the node itself will get removed later
                sourceNode.ClearSubtree();
                break;

            case (EntryType.Directory, EntryAction.Rename, EntryType.File, EntryAction.Create):
                // Explicitly remove the file on the target, because it still has the old name
                sourceToTarget.Add(new EntryChange(
                    sourceChange.Timestamp, targetNode.Path,
                    targetNode.Type, EntryAction.Delete,
                    null, null));

                sourceToTarget.AddRange(sourceManager.EnumerateCreatedDirectory(
                    new DirectoryInfo(Path.Combine(sourceManager.Root, sourceNode.Path))));

                sourceNode.ClearSubtree();
                break;

            case (EntryType.Directory, EntryAction.Delete, EntryType.Directory, null):
            case (EntryType.Directory, EntryAction.Delete, EntryType.Directory, EntryAction.Rename):
                sourceToTarget.Add(new EntryChange(
                    sourceChange.Timestamp, targetNode.Path,
                    targetNode.Type, EntryAction.Delete,
                    null, null));

                targetNode.ClearSubtree();
                break;

            // TODO: no conflict if both renames have the same new name; fix and add test
            case (EntryType.File, EntryAction.Create, EntryType.File, EntryAction.Create):
            case (EntryType.File, EntryAction.Rename, EntryType.File, EntryAction.Rename):
            case (EntryType.File, EntryAction.Rename, EntryType.File, EntryAction.Change):
            case (EntryType.File, EntryAction.Change, EntryType.File, EntryAction.Rename):
            case (EntryType.File, EntryAction.Change, EntryType.File, EntryAction.Change):
                sourceToTarget.Add(new EntryChange(
                    sourceChange.Timestamp, targetNode.Path, targetNode.Type,
                    sourceChange.ChangeProperties is null ? EntryAction.Rename : EntryAction.Change,
                    sourceChange.RenameProperties, sourceChange.ChangeProperties));

                var renameProps = sourceChange.RenameProperties is null ? targetChange.RenameProperties : null;
                var changeProps = sourceChange.ChangeProperties is null ? targetChange.ChangeProperties : null;
                if (renameProps is not null || changeProps is not null)
                    targetToSource.Add(new EntryChange(
                        targetChange.Timestamp, sourceNode.Path, sourceNode.Type,
                        changeProps is null ? EntryAction.Rename : EntryAction.Change,
                        renameProps, changeProps));
                break;

            case (EntryType.File, EntryAction.Rename, EntryType.File, EntryAction.Delete):
            case (EntryType.File, EntryAction.Change, EntryType.File, EntryAction.Delete):
                var sourceFileInfo = new FileInfo(sourceNode.Path);
                sourceToTarget.Add(new EntryChange(
                    sourceChange.Timestamp, sourceNode.Path,
                    sourceNode.Type, EntryAction.Create,
                    null, new ChangeProperties
                    {
                        LastWriteTime = sourceFileInfo.LastWriteTime,
                        Length = sourceFileInfo.Length
                    }));
                break;

            case (EntryType.File, EntryAction.Delete, EntryType.File, EntryAction.Rename):
            case (EntryType.File, EntryAction.Delete, EntryType.File, EntryAction.Change):
                sourceToTarget.Add(new EntryChange(
                    sourceChange.Timestamp, targetNode.Path,
                    targetNode.Type, EntryAction.Delete,
                    null, null));
                break;

            case (EntryType.Directory, EntryAction.Create, EntryType.File, EntryAction.Create):
            case (EntryType.Directory, EntryAction.Create, EntryType.File, EntryAction.Delete):
                sourceToTarget.Add(new EntryChange(
                    sourceChange.Timestamp, targetNode.Path,
                    sourceNode.Type, EntryAction.Create,
                    null, null));
                break;

            case (EntryType.Directory, EntryAction.Create, EntryType.File, EntryAction.Rename):
            case (EntryType.Directory, EntryAction.Create, EntryType.File, EntryAction.Change):
            case (EntryType.File, EntryAction.Create, EntryType.Directory, EntryAction.Rename):
                if (targetChange.RenameProperties is not null)
                    sourceToTarget.Add(new EntryChange(
                        sourceChange.Timestamp, targetNode.Path, 
                        targetNode.Type, EntryAction.Delete, 
                        null, null));

                sourceToTarget.Add(new EntryChange(
                    sourceChange.Timestamp, sourceNode.Path, 
                    sourceNode.Type, EntryAction.Create, 
                    null, sourceChange.ChangeProperties));
                break;

            case (EntryType.File, EntryAction.Create, EntryType.Directory, null):
            case (EntryType.File, EntryAction.Create, EntryType.Directory, EntryAction.Create):
            case (EntryType.File, EntryAction.Create, EntryType.Directory, EntryAction.Delete):
            case (EntryType.File, EntryAction.Change, EntryType.Directory, EntryAction.Create):
                sourceToTarget.Add(new EntryChange(
                    sourceChange.Timestamp, targetNode.Path,
                    sourceNode.Type, EntryAction.Create,
                    null, sourceChange.ChangeProperties));

                targetNode.ClearSubtree();
                break;

            case (EntryType.File, EntryAction.Rename, EntryType.Directory, EntryAction.Create):
                sourceToTarget.Add(new EntryChange(
                    sourceChange.Timestamp, targetNode.Path, 
                    targetNode.Type, EntryAction.Delete, 
                    null, null));

                var info = new FileInfo(Path.Combine(sourceManager.Root, sourceChange.Path));
                var properties = new ChangeProperties
                {
                    LastWriteTime = info.LastWriteTime,
                    Length = info.Length
                };

                sourceToTarget.Add(new EntryChange(
                    sourceChange.Timestamp, sourceNode.Path, 
                    sourceNode.Type, EntryAction.Create, 
                    null, properties));

                targetNode.ClearSubtree();
                break;
        }
    }

    private void ApplyChanges(
        FileSystemTrie sourceToTarget,
        FileSystemTrie targetToSource, 
        out List<EntryChange> appliedSource,
        out List<EntryChange> failedSource,
        out List<EntryChange> appliedTarget,
        out List<EntryChange> failedTarget)
    {
        appliedSource = new(); failedSource = new();
        appliedTarget = new(); failedTarget = new();
        int levels = Math.Max(sourceToTarget.Levels.Count, targetToSource.Levels.Count);
        for (int level = 0; level < levels; level++)
        {
            SplitChanges(sourceToTarget, level, out var sourceRenames, out var sourceOthers);
            SplitChanges(targetToSource, level, out var targetRenames, out var targetOthers);

            ApplyChanges(_sourceManager, _targetManager, sourceRenames, targetToSource, appliedSource, failedSource);
            ApplyChanges(_targetManager, _sourceManager, targetRenames, sourceToTarget, appliedTarget, failedTarget);

            ApplyChanges(_sourceManager, _targetManager, sourceOthers, targetToSource, appliedSource, failedSource);
            ApplyChanges(_targetManager, _sourceManager, targetOthers, sourceToTarget, appliedTarget, failedTarget);
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
        SyncSourceManager sourceManager,
        SyncSourceManager targetManager,
        IEnumerable<FileSystemTrieNode> sourceNodes, 
        FileSystemTrie targetToSource, 
        List<EntryChange> applied, 
        List<EntryChange> failed)
    {
        foreach (var sourceNode in sourceNodes)
        {
            // TODO: use database for applied/failed entries instead of in-memory structures
            var sourceChange = sourceNode.Value!;
            if (ApplyChange(sourceManager, targetManager, sourceChange))
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

    // TODO: consider applying changes through SyncSourceManager's, 
    // so as to keep the Index up-to-date and maybe something else
    // 
    // TODO: if a change has no timestamp, the modified entry's 
    // LastWriteTime cannot be set to anything else except DateTime.Now;
    // implement it and add a test w/ a mock for DateTime.Now
    private static bool ApplyChange(
        SyncSourceManager sourceManager,
        SyncSourceManager targetManager, 
        EntryChange change)
    {
        Console.WriteLine($"{sourceManager.Root} -> {targetManager.Root}: [{change.Action}] {change.OldPath})");
        var changeProps = change.ChangeProperties;

        var sourceOldPath = Path.Combine(sourceManager.Root, change.OldPath);
        var targetOldPath = Path.Combine(targetManager.Root, change.OldPath);
        var sourcePath = Path.Combine(sourceManager.Root, change.Path);
        var targetPath = Path.Combine(targetManager.Root, change.Path);
        DirectoryInfo sourceDirectory, targetDirectory;
        FileInfo sourceFile, targetFile;
        switch (change.Type, change.Action)
        {
            case (EntryType.Directory, EntryAction.Create):

                // Source directory is not present -> the change is invalid
                sourceDirectory = new DirectoryInfo(sourcePath);
                if (!sourceDirectory.Exists)
                    return false;

                // Target directory is present -> the change is invalid
                targetDirectory = new DirectoryInfo(targetPath);
                if (targetDirectory.Exists)
                    return false;

                // Target file is present -> replace it
                targetFile = new FileInfo(targetPath);
                if (targetFile.Exists)
                    targetFile.Delete();

                // Note: directory creation does not require contents comparison,
                // as all contents are written as separate entries in database
                targetDirectory.Create();
                targetDirectory.LastWriteTime = change.Timestamp!.Value;
                break;

            case (EntryType.Directory, EntryAction.Rename):

                // Renamed source directory is not present -> the change is invalid
                sourceDirectory = new DirectoryInfo(sourcePath);
                if (!sourceDirectory.Exists)
                    return false;

                // Renamed target directory is present -> the change is invalid
                targetDirectory = new DirectoryInfo(targetPath);
                if (targetDirectory.Exists)
                    return false;

                // Target directory is not present -> the change is invalid
                targetDirectory = new DirectoryInfo(targetOldPath);
                if (!targetDirectory.Exists)
                    return false;

                targetDirectory.MoveTo(targetPath);
                break;

            case (EntryType.Directory, EntryAction.Delete):

                // Source directory is present -> the change is invalid
                sourceDirectory = new DirectoryInfo(sourcePath);
                if (sourceDirectory.Exists)
                    return false;

                // Target directory is not present -> the change needs not to be applied
                targetDirectory = new DirectoryInfo(targetPath);
                if (!targetDirectory.Exists)
                    return true;

                targetDirectory.Delete(recursive: true);
                break;

            case (EntryType.File, EntryAction.Create):

                // Source file is not present -> the change is invalid
                sourceFile = new FileInfo(sourcePath);
                if (!sourceFile.Exists)
                    return false;

                // Source file is altered -> the change is invalid
                if (sourceFile.LastWriteTime.TrimMicroseconds() != changeProps!.Value.LastWriteTime ||
                    sourceFile.Length != changeProps.Value.Length)
                    return false;

                // Target file is present -> the change is invalid
                targetFile = new FileInfo(targetPath);
                if (targetFile.Exists)
                    return false;

                // Target directory is present -> replace it
                targetDirectory = new DirectoryInfo(targetPath);
                if (targetDirectory.Exists)
                    targetDirectory.Delete(recursive: true);

                targetFile = sourceFile.CopyTo(targetPath);
                break;

            case (EntryType.File, EntryAction.Rename):

                // Renamed source file is not present -> the change is invalid
                sourceFile = new FileInfo(sourcePath);
                if (!sourceFile.Exists)
                    return false;

                // Renamed target file is present -> the change is invalid
                targetFile = new FileInfo(targetPath);
                if (targetFile.Exists)
                    return false;

                // Target file is not present -> the change is invalid
                targetFile = new FileInfo(targetOldPath);
                if (!targetFile.Exists)
                    return false;

                targetFile.MoveTo(targetPath);
                break;

            case (EntryType.File, EntryAction.Change):

                // Source file is not present -> the change is invalid
                sourceFile = new FileInfo(sourcePath);
                if (!sourceFile.Exists)
                    return false;

                // Source file is altered -> the change is invalid
                if (sourceFile.LastWriteTime.TrimMicroseconds() != changeProps!.Value.LastWriteTime ||
                    sourceFile.Length != changeProps.Value.Length)
                    return false;

                // Renamed target file is present -> the change is invalid
                bool renamed = change.Name != change.OldName;
                if (renamed && new FileInfo(targetPath).Exists)
                    return false;

                targetFile = sourceFile.CopyTo(targetOldPath, overwrite: true);
                if (renamed)
                    targetFile.MoveTo(targetPath);
                break;

            case (EntryType.File, EntryAction.Delete):

                // Source file is present -> the change is invalid
                sourceFile = new FileInfo(sourcePath);
                if (sourceFile.Exists)
                    return false;

                // Target file is not present -> the change needs not to be applied
                targetFile = new FileInfo(targetPath);
                if (!targetFile.Exists)
                    return true;

                targetFile.Delete();
                break;
        }

        // Update LastWriteTime's for all parent directories up to the root
        var curr = new DirectoryInfo(Path.GetDirectoryName(targetOldPath)!);
        var infos = curr.EnumerateFileSystemInfos();
        curr.LastWriteTime = infos.GetEnumerator().MoveNext() ? infos.Max(d => d.LastWriteTime) : change.Timestamp!.Value;
        var prev = curr.Parent;
        while (prev is not null && 
            prev.FullName.Length >= targetManager.Root.Length && 
            prev.LastWriteTime < curr.LastWriteTime)
        {
            prev.LastWriteTime = curr.LastWriteTime;
            curr = prev;
            prev = prev.Parent;
        }

        return true;
    }

    private FileSystemTrie GetSourceDelta() => new(_sourceManager.SyncDatabase.EnumerateEvents());

    private FileSystemTrie GetTargetDelta() => new(EnumerateTargetChanges());

    private IEnumerable<EntryChange> EnumerateTargetChanges()
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
        var target = new DirectoryInfo(_targetManager.Root);
        _sourceManager.Index.Deserialize(out var source);
        var timestamp = _targetManager.SyncDatabase.LastSyncTime;
        var stack = new Stack<(DirectoryInfo, CustomDirectoryInfo)>();
        stack.Push((target, source));
        while (stack.TryPop(out var pair))
        {
            var (targetDir, sourceDir) = pair;

            // Directories
            foreach (var targetSubdir in targetDir.EnumerateDirectories())
            {
                if (_targetManager.IsServiceLocation(targetSubdir.FullName))
                    continue;

                if (sourceDir.Entries.Remove(targetSubdir.Name, out var sourceSubdir))
                {
                    switch (sourceSubdir)
                    {
                        case CustomDirectoryInfo directory:
                            stack.Push((targetSubdir, directory));
                            break;

                        case CustomFileInfo file:
                            foreach (var entry in _targetManager.EnumerateCreatedDirectory(targetSubdir, targetSubdir.LastWriteTime))
                                yield return entry;
                            break;
                    }
                }
                else
                {
                    foreach (var entry in _targetManager.EnumerateCreatedDirectory(targetSubdir, targetSubdir.LastWriteTime))
                        yield return entry;
                }
            }

            foreach (var sourceSubdir in sourceDir.Entries.Directories)
            {
                yield return new EntryChange(
                    timestamp, 
                    _targetManager.GetSubpath(Path.Combine(targetDir.FullName, sourceSubdir.Name)), 
                    EntryType.Directory, EntryAction.Delete, 
                    null, null);
            }

            // Files
            foreach (var targetFile in targetDir.EnumerateFiles())
            {
                var properties = new ChangeProperties
                {
                    LastWriteTime = targetFile.LastWriteTime.TrimMicroseconds(),
                    Length = targetFile.Length
                };
                if (sourceDir.Entries.Remove(targetFile.Name, out var sourceFile) && 
                    sourceFile is CustomFileInfo file && 
                    properties == file)
                    continue;

                yield return new EntryChange(
                    properties.LastWriteTime, 
                    _targetManager.GetSubpath(Path.Combine(targetDir.FullName, targetFile.Name)), 
                    EntryType.File, 
                    sourceFile is CustomFileInfo ? EntryAction.Change : EntryAction.Create, 
                    null, properties);
            }

            foreach (var sourceFile in sourceDir.Entries.Files)
            {
                yield return new EntryChange(
                    timestamp, 
                    _targetManager.GetSubpath(Path.Combine(targetDir.FullName, sourceFile.Name)), 
                    EntryType.File, EntryAction.Delete, 
                    null, null);
            }
        }
    }
}
