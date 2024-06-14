using DatagentMonitor.Collections;
using DatagentMonitor.FileSystem;

namespace DatagentMonitor.Synchronization;

public readonly record struct SynchronizationResult(
    List<EntryChange> Applied,
    List<EntryChange> Failed);

internal partial class Synchronizer
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
             new SyncSourceManager(targetRoot))
    { }

    public Synchronizer(string sourceRoot, string targetRoot) :
        this(new SyncSourceManager(sourceRoot),
             new SyncSourceManager(targetRoot))
    { }

    // TODO: guarantee robustness, i.e. that even if the program crashes,
    // events, applied/failed changes, etc. will not get lost
    public void Run(
        out SynchronizationResult sourceResult,
        out SynchronizationResult targetResult)
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
            sourceToTarget, 
            targetToSource,
            out sourceResult,
            out targetResult);

        Console.WriteLine($"Source-to-Target:");
        Console.WriteLine($"\tApplied: {sourceResult.Applied.Count}");
        Console.WriteLine($"\tFailed: {sourceResult.Failed.Count}");

        Console.WriteLine($"Target-to-Source:");
        Console.WriteLine($"\tApplied: {targetResult.Applied.Count}");
        Console.WriteLine($"\tFailed: {targetResult.Failed.Count}");

        // Merge changes applied to the source into the source index
        // Note: this will only work if Index.Root is the *actual* state of the root
        if (targetResult.Applied.Count > 0)
            _sourceManager.Index.MergeChanges(targetResult.Applied);

        // Update the index file if there were any changes made
        // (either on the source itself or applied from the target)
        if (sourceToIndex.Count > 0 || targetResult.Applied.Count > 0)
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

        var sourcePath = new List<string>();
        var targetPath = new List<string>();
        var stack = new Stack<(FileSystemTrie.Node?, FileSystemTrie.Node?, bool)>();
        stack.Push((sourceToIndex.Root, targetToIndex.Root, false));
        while (stack.Count > 0)
        {
            var (sourceNode, targetNode, visited) = stack.Pop();
            if (!visited)
            {
                sourcePath.Add(sourceNode?.Name ?? targetNode?.OldName ?? "");
                targetPath.Add(targetNode?.Name ?? sourceNode?.OldName ?? "");

                var sourceChange = sourceNode?.Value;
                var targetChange = targetNode?.Value;
                switch (sourceChange, targetChange)
                {
                    case (null, not null):
                        targetToSource.Add(new EntryChange(
                            targetChange.Timestamp, sourcePath.ToPath(), 
                            targetChange.Type, targetChange.Action, 
                            targetChange.RenameProperties, targetChange.ChangeProperties));
                        break;

                    case (not null, null):
                        sourceToTarget.Add(new EntryChange(
                            sourceChange.Timestamp, targetPath.ToPath(),
                            sourceChange.Type, sourceChange.Action,
                            sourceChange.RenameProperties, sourceChange.ChangeProperties));
                        break;

                    case (not null, not null):
                        ResolveConflict(new ResolveConflictArgs(
                            _sourceManager, _targetManager,
                            sourceToTarget, targetToSource,
                            sourcePath, targetPath,
                            sourceNode!, targetNode!));
                        break;
                }                

                stack.Push((sourceNode, targetNode, true));

                var intersection = new HashSet<FileSystemTrie.Node>();
                if (sourceNode is not null)
                {
                    var pairs = new List<(FileSystemTrie.Node?, FileSystemTrie.Node?, bool)>();
                    foreach (var sourceSubnode in sourceNode.Names.Values.OrderByDescending(n => n.Value?.Action,
                        Comparer<EntryAction?>.Create((x, y) => (x ?? EntryAction.None) - (y ?? EntryAction.None))))
                    {
                        FileSystemTrie.Node? targetSubnode = null;
                        if (targetNode is not null && 
                            targetNode.TryGetNode(sourceSubnode.OldName, out targetSubnode) && 
                            !intersection.Add(targetSubnode))
                            targetSubnode = null;

                        pairs.Add((sourceSubnode, targetSubnode, false));
                    }

                    foreach (var pair in pairs.AsEnumerable().Reverse())
                        stack.Push(pair);
                }

                if (targetNode is not null)
                {
                    foreach (var targetSubnode in targetNode.Names.Values)
                    {
                        if (intersection.Contains(targetSubnode))
                            continue;

                        stack.Push((null, targetSubnode, false));
                    }
                }
            }
            else
            {
                sourcePath.RemoveAt(sourcePath.Count - 1);
                targetPath.RemoveAt(targetPath.Count - 1);
            }
        }
    }

    private static void ResolveConflict(ResolveConflictArgs args)
    {
        var sourceChange = args.SourceNode.Value;
        var targetChange = args.TargetNode.Value;

        // Total: 64 cases
        switch (args.SourceNode.Type, sourceChange.Action, args.TargetNode.Type, targetChange.Action)
        {
            // No-op; 2 cases
            case (EntryType.Directory, EntryAction.Delete, EntryType.Directory, EntryAction.Delete):
            case (EntryType.File, EntryAction.Delete, EntryType.File, EntryAction.Delete):
                break;

            // Subtree-dependent conflicts; 4 cases
            case (EntryType.Directory, EntryAction.Rename, EntryType.Directory, EntryAction.Delete):
            case (EntryType.Directory, EntryAction.Change, EntryType.Directory, EntryAction.Delete):
            case (EntryType.Directory, EntryAction.Delete, EntryType.Directory, EntryAction.Rename):
            case (EntryType.Directory, EntryAction.Delete, EntryType.Directory, EntryAction.Change):
                SwapArgs(ResolveConflictExact, args, a => a.SourceNode.PriorityValue < a.TargetNode.PriorityValue);
                break;

            // Subtree-independent conflicts; 14 cases
            // 
            // TODO: conflict handling does not account for equal final names; fix and add test
            // Source: Rename file1 -> file1-renamed-source
            // Target: Create file1-renamed-source
            case (EntryType.Directory, EntryAction.Create, EntryType.Directory, EntryAction.Create):
            case (EntryType.Directory, EntryAction.Rename, EntryType.Directory, EntryAction.Rename):
            case (EntryType.Directory, EntryAction.Rename, EntryType.Directory, EntryAction.Change):
            case (EntryType.Directory, EntryAction.Change, EntryType.Directory, EntryAction.Rename):
            case (EntryType.Directory, EntryAction.Change, EntryType.Directory, EntryAction.Change):
            case (EntryType.File, EntryAction.Create, EntryType.File, EntryAction.Create):
            case (EntryType.File, EntryAction.Rename, EntryType.File, EntryAction.Rename):
            case (EntryType.File, EntryAction.Rename, EntryType.File, EntryAction.Change):
            case (EntryType.File, EntryAction.Rename, EntryType.File, EntryAction.Delete):
            case (EntryType.File, EntryAction.Change, EntryType.File, EntryAction.Rename):
            case (EntryType.File, EntryAction.Change, EntryType.File, EntryAction.Change):
            case (EntryType.File, EntryAction.Change, EntryType.File, EntryAction.Delete):
            case (EntryType.File, EntryAction.Delete, EntryType.File, EntryAction.Rename):
            case (EntryType.File, EntryAction.Delete, EntryType.File, EntryAction.Change):
                SwapArgs(ResolveConflictExact, args, a => a.SourceNode.Value < a.TargetNode.Value);
                break;

            // Different types conflicts; 10 cases
            case (EntryType.Directory, EntryAction.Create, EntryType.File, EntryAction.Create):
            case (EntryType.Directory, EntryAction.Create, EntryType.File, EntryAction.Rename):
            case (EntryType.Directory, EntryAction.Create, EntryType.File, EntryAction.Change):
            case (EntryType.Directory, EntryAction.Rename, EntryType.File, EntryAction.Create):
            case (EntryType.Directory, EntryAction.Change, EntryType.File, EntryAction.Create):
            case (EntryType.File, EntryAction.Create, EntryType.Directory, EntryAction.Create):
            case (EntryType.File, EntryAction.Create, EntryType.Directory, EntryAction.Rename):
            case (EntryType.File, EntryAction.Create, EntryType.Directory, EntryAction.Change):
            case (EntryType.File, EntryAction.Rename, EntryType.Directory, EntryAction.Create):
            case (EntryType.File, EntryAction.Change, EntryType.Directory, EntryAction.Create):
                // TODO: consider improving the predicate,
                // e.g. by lowering the priority of empty directories
                SwapArgs(ResolveConflictExact, args, a => a.SourceNode.PriorityValue < a.TargetNode.PriorityValue);
                break;

            // Different types but the entry is created in place of a deleted one; 4 cases
            // Resolve in favor of the newly created entry; its counterpart is deleted anyway
            case (EntryType.Directory, EntryAction.Create, EntryType.File, EntryAction.Delete):
            case (EntryType.Directory, EntryAction.Delete, EntryType.File, EntryAction.Create):
            case (EntryType.File, EntryAction.Create, EntryType.Directory, EntryAction.Delete):
            case (EntryType.File, EntryAction.Delete, EntryType.Directory, EntryAction.Create):
                SwapArgs(ResolveConflictExact, args, a => a.TargetNode.Value!.Action is EntryAction.Create);
                break;

            // A new entry created when another one already exists at the same path; 12 cases
            // TODO: (Rename, Create) should be handled as a (Rename, Rename), 
            // because it might mean that the file has got renamed on the target
            case (_, EntryAction.Create, _, not EntryAction.Create):
            case (_, not EntryAction.Create, _, EntryAction.Create):

            // Two entries at the same path but of different types; 18 cases
            case (EntryType.Directory, not EntryAction.Create, EntryType.File, not EntryAction.Create):
            case (EntryType.File, not EntryAction.Create, EntryType.Directory, not EntryAction.Create):
                throw new InvalidConflictException(
                    args.SourceNode.Type, sourceChange.Action,
                    args.TargetNode.Type, targetChange.Action);
        }
    }

    private static void ResolveConflictExact(ResolveConflictArgs args)
    {
        var sourceChange = args.SourceNode.Value ?? throw new ArgumentException("Source change was null.");
        var targetChange = args.TargetNode.Value ?? throw new ArgumentException("Target change was null.");
        switch (args.SourceNode.Type, sourceChange.Action, args.TargetNode.Type, targetChange.Action)
        {
            // TODO: no conflict if both renames have the same new name; fix and add test
            case (EntryType.Directory, EntryAction.Create, EntryType.Directory, EntryAction.Create):
            case (EntryType.Directory, EntryAction.Rename, EntryType.Directory, EntryAction.Rename):
            case (EntryType.Directory, EntryAction.Rename, EntryType.Directory, EntryAction.Change):
            case (EntryType.Directory, EntryAction.Change, EntryType.Directory, EntryAction.Rename):
            case (EntryType.Directory, EntryAction.Change, EntryType.Directory, EntryAction.Change):
            case (EntryType.File, EntryAction.Create, EntryType.File, EntryAction.Create):
            case (EntryType.File, EntryAction.Rename, EntryType.File, EntryAction.Rename):
            case (EntryType.File, EntryAction.Rename, EntryType.File, EntryAction.Change):
            case (EntryType.File, EntryAction.Change, EntryType.File, EntryAction.Rename):
            case (EntryType.File, EntryAction.Change, EntryType.File, EntryAction.Change):
                args.SourceToTarget.Add(new EntryChange(
                    sourceChange.Timestamp, args.TargetPath.ToPath(), args.TargetNode.Type,
                    sourceChange.ChangeProperties is null ? EntryAction.Rename : EntryAction.Change,
                    sourceChange.RenameProperties, sourceChange.ChangeProperties));

                // Update the counterpart tree path
                if (targetChange.Type is EntryType.Directory && sourceChange.RenameProperties is not null)
                    args.TargetPath[^1] = sourceChange.RenameProperties!.Value.Name;

                var renameProps = sourceChange.RenameProperties is null ? targetChange.RenameProperties : null;
                var changeProps = sourceChange.ChangeProperties is null ? targetChange.ChangeProperties : null;
                if (renameProps is not null || changeProps is not null)
                {
                    args.TargetToSource.Add(new EntryChange(
                        targetChange.Timestamp, args.SourcePath.ToPath(), args.SourceNode.Type,
                        changeProps is null ? EntryAction.Rename : EntryAction.Change,
                        renameProps, changeProps));

                    if (sourceChange.Type is EntryType.Directory && renameProps is not null)
                        args.SourcePath[^1] = renameProps.Value.Name;
                }
                break;

            case (EntryType.Directory, EntryAction.Delete, EntryType.Directory, EntryAction.Rename or EntryAction.Change):
            case (EntryType.File, EntryAction.Delete, EntryType.File, EntryAction.Rename or EntryAction.Change):
                args.SourceToTarget.Add(new EntryChange(
                    sourceChange.Timestamp, args.TargetPath.ToPath(),
                    args.TargetNode.Type, EntryAction.Delete,
                    null, null));

                args.TargetNode.ClearSubtree();
                break;

            case (EntryType.Directory, EntryAction.Create, EntryType.File, _):
            case (EntryType.File, EntryAction.Create, EntryType.Directory, _):
                if (targetChange.RenameProperties is not null)
                    args.SourceToTarget.Add(new EntryChange(
                        sourceChange.Timestamp, args.TargetPath.ToPath(),
                        args.TargetNode.Type, EntryAction.Delete,
                        null, null));

                args.SourceToTarget.Add(new EntryChange(
                    sourceChange.Timestamp, args.SourcePath.ToPath(),
                    args.SourceNode.Type, EntryAction.Create,
                    null, sourceChange.ChangeProperties));

                args.TargetNode.ClearSubtree();
                break;

            case (EntryType.Directory, EntryAction.Rename or EntryAction.Change, EntryType.Directory, EntryAction.Delete):
            case (EntryType.Directory, EntryAction.Rename or EntryAction.Change, EntryType.File, EntryAction.Create):
                if (targetChange.Action is EntryAction.Create && sourceChange.RenameProperties is not null)
                    args.SourceToTarget.Add(new EntryChange(
                        sourceChange.Timestamp, args.TargetPath.ToPath(),
                        args.TargetNode.Type, EntryAction.Delete,
                        null, null));

                // Only remove the subtree; the node itself will get removed later
                args.SourceToTarget.AddRange(args.SourceManager.EnumerateCreatedDirectory(
                    new DirectoryInfo(Path.Combine(args.SourceManager.Root, args.SourcePath.ToPath()))));

                args.SourceNode.ClearSubtree();
                break;
            
            case (EntryType.File, EntryAction.Rename or EntryAction.Change, EntryType.Directory, EntryAction.Create):
            case (EntryType.File, EntryAction.Rename or EntryAction.Change, EntryType.File, EntryAction.Delete):
                if (targetChange.Action is EntryAction.Create && sourceChange.RenameProperties is not null)
                    args.SourceToTarget.Add(new EntryChange(
                        sourceChange.Timestamp, args.TargetPath.ToPath(),
                        args.TargetNode.Type, EntryAction.Delete,
                        null, null));

                args.SourceToTarget.Add(new EntryChange(
                    sourceChange.Timestamp, args.SourcePath.ToPath(),
                    args.SourceNode.Type, EntryAction.Create,
                    null, sourceChange.ChangeProperties ?? new ChangeProperties(
                        new FileInfo(Path.Combine(args.SourceManager.Root, args.SourcePath.ToPath())))));

                args.TargetNode.ClearSubtree();
                break;
        }
    }

    private void ApplyChanges(
        FileSystemTrie sourceToTarget,
        FileSystemTrie targetToSource,
        out SynchronizationResult sourceResult,
        out SynchronizationResult targetResult)
    {
        var sourceResultLocal = new SynchronizationResult(new(), new());
        var targetResultLocal = new SynchronizationResult(new(), new());

        var sourcePath = new List<string>();
        var targetPath = new List<string>();
        var stack = new Stack<(FileSystemTrie.Node?, FileSystemTrie.Node?, bool)>();
        stack.Push((sourceToTarget.Root, targetToSource.Root, false));
        while (stack.Count > 0)
        {
            var (sourceNode, targetNode, visited) = stack.Pop();
            ApplyChangeArgs GetArgs() => new(
                _sourceManager, _targetManager,
                sourcePath, targetPath,
                sourceNode, targetNode,
                sourceResultLocal, targetResultLocal);

            if (sourceNode?.Value?.Action is EntryAction.Create || 
                targetNode?.Value?.Action is EntryAction.Create)
            {
                SwapArgs(ApplyCreateChanges, GetArgs(), a => a.TargetNode?.Value?.Action is EntryAction.Create);
                continue;
            }

            if (!visited)
            {
                sourcePath.Add(targetNode?.OldName ?? sourceNode?.Name ?? "");
                targetPath.Add(sourceNode?.OldName ?? targetNode?.Name ?? "");

                stack.Push((sourceNode, targetNode, true));

                var intersection = new HashSet<FileSystemTrie.Node>();
                if (sourceNode is not null)
                {
                    foreach (var sourceSubnode in sourceNode.Names.Values.OrderBy(n => n.Value?.Action, 
                        Comparer<EntryAction?>.Create((x, y) => (x ?? EntryAction.None) - (y ?? EntryAction.None))))
                    {
                        FileSystemTrie.Node? targetSubnode = null;
                        if (targetNode is not null && targetNode.TryGetNode(sourceSubnode.Name, out targetSubnode))
                            intersection.Add(targetSubnode);

                        stack.Push((sourceSubnode, targetSubnode, false));
                    }
                }

                if (targetNode is not null)
                {
                    foreach (var targetSubnode in targetNode.Names.Values)
                    {
                        if (intersection.Contains(targetSubnode))
                            continue;

                        stack.Push((null, targetSubnode, false));
                    }
                }
            }
            else
            {
                // By default, apply the source change first and then the target change.
                // However, if one of the changes is a rename, it must always be applied the second.
                SwapArgs(ApplyChangesPair, GetArgs(), a => a.SourceNode?.Value?.RenameProperties is not null);

                sourcePath.RemoveAt(sourcePath.Count - 1);
                targetPath.RemoveAt(targetPath.Count - 1);
            }
        }

        sourceResult = sourceResultLocal;
        targetResult = targetResultLocal;
    }

    private static void ApplyCreateChanges(ApplyChangeArgs args)
    {
        var sourcePath = new List<string>(args.SourcePath);
        var targetPath = new List<string>(args.TargetPath);

        var stack = new Stack<(FileSystemTrie.Node, bool)>();
        stack.Push((args.SourceNode, false));
        while (stack.Count > 0)
        {
            var (sourceNode, visited) = stack.Pop();
            if (!visited)
            {
                sourcePath.Add(sourceNode.Name);
                targetPath.Add(sourceNode.Name);

                ApplyChange(args with
                {
                    SourcePath = sourcePath,
                    TargetPath = targetPath,
                    SourceNode = sourceNode
                });

                stack.Push((sourceNode, true));

                foreach (var sourceSubnode in sourceNode.Names.Values)
                    stack.Push((sourceSubnode, false));
            }
            else
            {
                sourcePath.RemoveAt(sourcePath.Count - 1);
                targetPath.RemoveAt(targetPath.Count - 1);
            }
        }
    }

    private static void ApplyChangesPair(ApplyChangeArgs args)
    {
        if (args.SourceNode?.Value is not null)
            ApplyChange(args);

        if (args.TargetNode?.Value is not null)
            ApplyChange(args.Swap());
    }

    private static void ApplyChange(ApplyChangeArgs args)
    {
        var sourcePathRes = args.SourcePath.ToPath();
        var targetPathRes = args.TargetPath.ToPath();

        var change = args.SourceNode!.Value!;
        var changeActual = new EntryChange(
            change.Timestamp, targetPathRes, 
            change.Type, change.Action, 
            change.RenameProperties, change.ChangeProperties);

        // TODO: use database for applied/failed entries instead of in-memory structures
        if (TryApplyChange(args.SourceManager, args.TargetManager, sourcePathRes, targetPathRes, change))
            // TODO: consider using the "with { Timestamp = DateTime.Now }" clause, 
            // because that is the time when the change gets applied
            args.SourceResult.Applied.Add(changeActual);
        else
            args.SourceResult.Failed.Add(changeActual);
    }

    // TODO: consider applying changes through SyncSourceManager's, 
    // so as to keep the Index up-to-date and maybe something else
    // 
    // TODO: if a change has no timestamp, the modified entry's 
    // LastWriteTime cannot be set to anything else except DateTime.Now;
    // implement it and add a test w/ a mock for DateTime.Now
    // 
    // TODO: consider setting all parent directories' LastWriteTime's to DateTime.Now
    private static bool TryApplyChange(
        SyncSourceManager sourceManager,
        SyncSourceManager targetManager,
        string sourcePath,
        string targetPath,
        EntryChange change)
    {
        Console.WriteLine($"{sourceManager.Root} -> {targetManager.Root}: [{change.Action}] {change.OldPath})");
        var renameProps = change.RenameProperties;
        var changeProps = change.ChangeProperties;

        sourcePath = Path.Combine(sourceManager.Root, sourcePath);
        targetPath = Path.Combine(targetManager.Root, targetPath);

        var targetPathRenamed = renameProps is null ? null : 
            Path.Combine(
                targetManager.Root, 
                string.Concat(targetPath.SkipLast(change.OldName.Length + 1)), 
                renameProps.Value.Name);

        var parent = new DirectoryInfo(Path.GetDirectoryName(targetPath)!);
        var parentLastWriteTime = parent.LastWriteTime;

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
                targetDirectory = new DirectoryInfo(targetPathRenamed!);
                if (targetDirectory.Exists)
                    return false;

                // Target directory is not present -> the change is invalid
                targetDirectory = new DirectoryInfo(targetPath);
                if (!targetDirectory.Exists)
                    return false;

                targetDirectory.MoveTo(targetPathRenamed!);
                break;

            case (EntryType.Directory, EntryAction.Change):

                // Source directory is not present -> the change is invalid
                sourceDirectory = new DirectoryInfo(sourcePath);
                if (!sourceDirectory.Exists)
                    return false;

                // Source directory is altered -> the change is invalid
                if (sourceDirectory != changeProps)
                    return false;

                if (targetPathRenamed is not null)
                {
                    // Renamed target directory is present -> the change is invalid
                    targetDirectory = new DirectoryInfo(targetPathRenamed);
                    if (targetDirectory.Exists)
                        return false;
                }

                // Target directory is not present -> the change is invalid
                targetDirectory = new DirectoryInfo(targetPath);
                if (!targetDirectory.Exists)
                    return false;

                targetDirectory.LastWriteTime = changeProps.Value.LastWriteTime;
                if (targetPathRenamed is not null)
                    targetDirectory.MoveTo(targetPathRenamed);
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
                if (sourceFile != changeProps)
                    return false;

                // Target file is present -> the change is invalid
                targetFile = new FileInfo(targetPath);
                if (targetFile.Exists)
                    return false;

                // Target directory is present -> replace it
                targetDirectory = new DirectoryInfo(targetPath);
                if (targetDirectory.Exists)
                    targetDirectory.Delete(recursive: true);

                sourceFile.CopyTo(targetPath);
                break;

            case (EntryType.File, EntryAction.Rename):

                // Renamed source file is not present -> the change is invalid
                sourceFile = new FileInfo(sourcePath);
                if (!sourceFile.Exists)
                    return false;

                // Renamed target file is present -> the change is invalid
                targetFile = new FileInfo(targetPathRenamed!);
                if (targetFile.Exists)
                    return false;

                // Target file is not present -> the change is invalid
                targetFile = new FileInfo(targetPath);
                if (!targetFile.Exists)
                    return false;

                targetFile.MoveTo(targetPathRenamed!);
                break;

            case (EntryType.File, EntryAction.Change):

                // Source file is not present -> the change is invalid
                sourceFile = new FileInfo(sourcePath);
                if (!sourceFile.Exists)
                    return false;

                // Source file is altered -> the change is invalid
                if (sourceFile != changeProps)
                    return false;

                if (targetPathRenamed is not null)
                {
                    // Renamed target file is present -> the change is invalid
                    targetFile = new FileInfo(targetPathRenamed);
                    if (targetFile.Exists)
                        return false;
                }

                // Target file is not present -> the change is invalid
                targetFile = new FileInfo(targetPath);
                if (!targetFile.Exists)
                    return false;

                targetFile = sourceFile.CopyTo(targetPath, overwrite: true);
                if (targetPathRenamed is not null)
                    targetFile.MoveTo(targetPathRenamed);
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

        // Revert parent's LastWriteTime set by the file system
        parent.LastWriteTime = parentLastWriteTime;

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
                            if (targetSubdir.LastWriteTime > directory.LastWriteTime)
                                yield return new EntryChange(
                                    targetSubdir.LastWriteTime,
                                    _targetManager.GetSubpath(Path.Combine(targetDir.FullName, sourceSubdir.Name)),
                                    EntryType.Directory, EntryAction.Change,
                                    null, new ChangeProperties(targetSubdir));

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
                var properties = new ChangeProperties(targetFile);
                if (sourceDir.Entries.Remove(targetFile.Name, out var sourceFile) &&
                    sourceFile is CustomFileInfo file &&
                    file == properties)
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
