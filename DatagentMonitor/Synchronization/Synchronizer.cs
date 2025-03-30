using DatagentMonitor.Collections;
using DatagentMonitor.FileSystem;
using DatagentMonitor.Synchronization.Conflicts;

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
        this(sourceManager, new SyncSourceManager(targetRoot)) { }

    public Synchronizer(string sourceRoot, string targetRoot) :
        this(new SyncSourceManager(sourceRoot), new SyncSourceManager(targetRoot)) { }

    // TODO: guarantee robustness, i.e. that even if the program crashes,
    // events, applied/failed changes, etc. will not get lost
    public async Task<(SynchronizationResult SourceResult, SynchronizationResult TargetResult)> Run()
    {
        var (sourceToIndex, targetToIndex) = await GetIndexChanges();
        var sourceToTargetCommands = new FileSystemCommandTrie();
        var targetToSourceCommands = new FileSystemCommandTrie();

        Console.WriteLine("Comparing source and target changes...");
        GetRelativeChanges(
            sourceToIndex,
            targetToIndex,
            sourceToTargetCommands,
            targetToSourceCommands,
            out var sourceToTarget,
            out var targetToSource, 
            out var namesConflicts, 
            out var contentsConflicts);

        ResolveConflicts( 
            namesConflicts, 
            contentsConflicts);

        ApplyChanges(
            sourceToTarget, 
            targetToSource,
            out var sourceResult,
            out var targetResult);

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

        await _targetManager.Database.SetLastSyncTimeAsync(DateTimeStaticProvider.Now);
        await _sourceManager.Database.ClearEventsAsync();

        Console.WriteLine("Synchronization complete.");

        return (sourceResult, targetResult);
    }

    private async Task<(FileSystemTrie SourceToIndex, FileSystemTrie TargetToIndex)> GetIndexChanges()
    {
        Console.Write($"Resolving source changes... ");
        var sourceToIndex = await GetSourceDelta();
        Console.WriteLine($"Total: {sourceToIndex.Count}");

        Console.Write($"Resolving target changes... ");
        var targetToIndex = GetTargetDelta();
        Console.WriteLine($"Total: {targetToIndex.Count}");

        return (sourceToIndex, targetToIndex);
    }

    private void GetRelativeChanges(
        FileSystemTrie sourceToIndex,
        FileSystemTrie targetToIndex,
        FileSystemCommandTrie sourceToTargetCommands,
        FileSystemCommandTrie targetToSourceCommands,
        out FileSystemTrie sourceToTarget,
        out FileSystemTrie targetToSource, 
        out List<NamesConflict> namesConflicts, 
        out List<ContentsConflict> contentsConflicts)
    {
        sourceToTarget = new(mode: FileSystemTrie.Mode.Stack);
        targetToSource = new(mode: FileSystemTrie.Mode.Stack);

        namesConflicts = new List<NamesConflict>();
        contentsConflicts = new List<ContentsConflict>();

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
                        //ResolveConflict(new ResolveConflictArgs(
                        //    _sourceManager, _targetManager,
                        //    sourceToTarget, targetToSource,
                        //    sourcePath, targetPath,
                        //    sourceNode!, targetNode!));
                        contentsConflicts.Add(new ContentsConflict(new ResolveConflictArgs(
                            _sourceManager, _targetManager, 
                            sourceToTargetCommands, targetToSourceCommands, 
                            sourceNode!, targetNode!)));
                        break;
                }                

                stack.Push((sourceNode, targetNode, true));

                // Skip subtree comparison. In these cases, subtrees' nodes cannot be compared
                // because their base nodes' conflicts are not resolved yet.
                // Depending on the result of that resolve, the subtrees may not need to be compared at all.
                switch (sourceChange.Type, sourceChange.Action, targetChange.Type, targetChange.Action)
                {
                    case (EntryType.Directory, _, EntryType.File, _):
                    case (EntryType.File, _, EntryType.Directory, _):
                    case (EntryType.Directory, EntryAction.Delete, EntryType.Directory, _):
                    case (EntryType.Directory, _, EntryType.Directory, EntryAction.Delete):
                        continue;
                }

                var intersection = new HashSet<FileSystemTrie.Node>();
                if (sourceNode is not null)
                {
                    var pairs = new List<(FileSystemTrie.Node?, FileSystemTrie.Node?, bool)>();
                    foreach (var sourceSubnode in sourceNode.NodesByNames.Values.OrderByDescending(n => n.Value?.Action,
                        Comparer<EntryAction?>.Create((x, y) => (x ?? EntryAction.None) - (y ?? EntryAction.None))))
                    {
                        if (targetNode is null)
                        {
                            pairs.Add((sourceSubnode, null, false));
                        }
                        else
                        {
                            // There is one entry that is renamed differently on source and target, 
                            // e.g. file1 -> file2; file1 -> file3.
                            var oneEntryTwoNames =
                                targetNode.NodesByOldNames.TryGetValue(sourceSubnode.OldName, out var targetSubnode) &&
                                sourceSubnode.Value.Action is EntryAction.Rename && targetSubnode.Name != sourceSubnode.Name;

                            // Store the target subnode; it must be used later as a pair for the source subnode.
                            var targetSubnodeTemp = targetSubnode;

                            // There are two entries (one on the source, the other on the target) that get the same new name.
                            // This includes:
                            // - Create VS Rename (new file2; file1 -> file2)
                            // - Rename VS Rename (file1 -> file2; file3 -> file2)
                            // - Rename VS Create (file1 -> file2; new file2)
                            var twoEntriesOneName =
                                // TODO: this will break if targetNode.NodesByOldNames contains sourceSubnode.Name
                                // (e.g. file2 -> file3, file1 -> file2; file2 has the old name the same as the file1's new name); 
                                // fix and add tests for such cyclic renames of multiple entries
                                targetNode.NodesByNames.TryGetValue(sourceSubnode.Name, out targetSubnode) &&
                                (sourceSubnode.Value.Action is EntryAction.Rename && targetSubnode.OldName != sourceSubnode.OldName || 
                                 sourceSubnode.Value.Action is EntryAction.Create && targetSubnode.OldName != targetSubnode.Name);

                            // Cases listed above are to be considered name conflicts 
                            // and must be resolved prior to all other conflicts.
                            if (oneEntryTwoNames || twoEntriesOneName)
                            {
                                namesConflicts.Add(new NamesConflict(new ResolveConflictArgs(
                                    _sourceManager, _targetManager,
                                    sourceToTargetCommands, targetToSourceCommands, 
                                    sourceSubnode, targetSubnode!)));
                            }

                            // Use the previously stored target subnode.
                            targetSubnode = targetSubnodeTemp;
                            if (targetSubnode is not null && intersection.Add(targetSubnode))
                                pairs.Add((sourceSubnode, targetSubnode, false));
                        }
                    }

                    foreach (var pair in pairs.AsEnumerable().Reverse())
                        stack.Push(pair);
                }

                if (targetNode is not null)
                {
                    foreach (var targetSubnode in targetNode.NodesByNames.Values)
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

    private static void ResolveConflicts(
        List<NamesConflict> namesConflicts,
        List<ContentsConflict> contentsConflicts)
    {
        // First, resolve all contents conflicts.
        // This is done first so that, if any entries are to be deleted,
        // they will not participate in names distribution.
        ResolveContentsConflicts(contentsConflicts);

        // Then resolve all names conflicts.
        ResolveNamesConflicts(namesConflicts);
    }

    private static void ResolveContentsConflicts(List<ContentsConflict> conflicts)
    {
        foreach (var conflict in conflicts)
            conflict.Resolve();
    }

    public static void ResolveNamesConflicts(IEnumerable<NamesConflict> conflicts)
    {
        var first = conflicts.First();
        var (sourceManager, targetManager) = (first.SourceManager, first.TargetManager);
        var (sourceToTarget, targetToSource) = (first.SourceToTarget, first.TargetToSource);

        // Extract unique conflicting names
        var sourceMap = new OrderedDictionary<string,
            (FileSystemTrie.Node SourceNode, FileSystemTrie.Node? TargetNode, FileSystemTrie.Node? TargetNodeOther)>();
        foreach (var conflict in conflicts)
        {
            var sourceNode = conflict.SourceNode;
            var targetNode = conflict.TargetNode;
            if (!conflict.IsConflict())
            {
                Console.WriteLine($"No conflict for {GetNodeRepr(sourceNode)} VS {GetNodeRepr(targetNode)}.");
                continue;
            }

            if (sourceMap.TryGetValue(sourceNode.OldPath, out var nodes))
            {
                // TODO: perhaps add guards?
                switch ((nodes.TargetNode, nodes.TargetNodeOther))
                {
                    case (null, null):
                        throw new ArgumentException($"Corrupted conflict entry for {GetNodeRepr(sourceNode)}.");

                    case (null, not null):
                        // Both target entries have the same name -> impossible
                        if (targetNode.Name == nodes.TargetNodeOther.Name)
                            throw new ArgumentException(
                                $"Invalid conflict for {GetNodeRepr(sourceNode)}:\n"
                                + $"{GetNodeRepr(targetNode)} (new)\n"
                                + $"{GetNodeRepr(nodes.TargetNodeOther)}");

                        sourceMap[sourceNode.OldPath] = (nodes.SourceNode, targetNode, nodes.TargetNodeOther);
                        break;

                    case (not null, null):
                        // Both target entries have the same old name -> duplication
                        if (nodes.TargetNode.OldName == targetNode.OldName)
                            throw new ArgumentException(
                                $"Invalid conflict for {GetNodeRepr(sourceNode)}:\n"
                                + $"{GetNodeRepr(nodes.TargetNode)}\n"
                                + $"{GetNodeRepr(targetNode)} (new)");

                        sourceMap[sourceNode.OldPath] = (nodes.SourceNode, nodes.TargetNode, targetNode);
                        break;

                    // For a single file called 'x' there can only be at most 2 conflicts:
                    // 1. x -> a VS x -> b
                    // 2. x -> a VS y -> a (or just 'a' for a new file)
                    //
                    // More than 2 conflicts is to be considered an exception.
                    case (not null, not null):
                        throw new ArgumentException(
                            $"More than two conflicts encountered for {GetNodeRepr(sourceNode)}:\n"
                            + $"{GetNodeRepr(nodes.TargetNode)}\n"
                            + $"{GetNodeRepr(nodes.TargetNodeOther)}\n"
                            + $"{GetNodeRepr(targetNode)} (new)");
                }
            }
            else
            {
                sourceMap.Add(sourceNode.OldPath,
                    sourceNode.OldName == targetNode.OldName ? (sourceNode, targetNode, null) : (sourceNode, null, targetNode));
            }
        }

        // Prompt the user to set a new name for every of the conflicting source entries
        var names = new HashSet<string>();
        var targetNodesPairs = new List<(FileSystemTrie.Node SourceNode, FileSystemTrie.Node TargetNode)>();
        foreach (var (sourceNode, targetNode, targetNodeOther) in sourceMap.Values)
        {
            var path = sourceNode.OldPath;
            bool success = false;
            while (!success)
            {
                Console.Write($"Enter a new name for \"{path}\": ");
                var name = Console.ReadLine();
                Console.Write(new string(' ', Console.BufferWidth));
                if (!ValidateEntryName(
                    sourceManager, targetManager,
                    sourceToTarget, targetToSource,
                    sourceNode, targetNode,
                    name, out var message))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Write($"Invalid name. {message}");
                    Console.ResetColor();
                }
                else
                {
                    if (name != sourceNode.Name)
                    {
                        targetToSource.Add(new EntryCommand(sourceNode.Path, CommandAction.Rename, new RenameProperties
                        {
                            Name = name!
                        }));
                    }

                    if (targetNode is null || name != targetNode.Name)
                    {
                        sourceToTarget.Add(new EntryCommand(targetNode.Path, CommandAction.Rename, new RenameProperties
                        {
                            Name = name!
                        }));
                    }

                    names.Add(Path.Combine(sourceNode.Parent.Path, name!));

                    if (targetNodeOther is not null)
                        targetNodesPairs.Add((sourceNode, targetNodeOther));

                    success = true;
                }

                Console.SetCursorPosition(0, Console.CursorTop - 1);
                Console.Write(new string(' ', Console.BufferWidth));
                Console.SetCursorPosition(0, Console.CursorTop);
            }
        }

        // Prompt the user to set a new name for every of the remaining target nodes.
        foreach (var (sourceNode, targetNode) in targetNodesPairs)
        {
            var path = targetNode.OldPath;
            bool success = false;
            while (!success)
            {
                Console.Write($"Enter a new name for \"{path}\": ");
                var name = Console.ReadLine();
                Console.Write(new string(' ', Console.BufferWidth));
                if (!ValidateEntryName(
                    sourceManager, targetManager,
                    sourceToTarget, targetToSource,
                    sourceNode, targetNode,
                    name, out var message))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Write($"Invalid name. {message}");
                    Console.ResetColor();
                }
                else
                {
                    if (name != targetNode.OldName)
                    {
                        targetToSource.Add(new EntryCommand(sourceNode.Path, CommandAction.Rename, new RenameProperties
                        {
                            Name = name!
                        }));
                    }

                    if (name != targetNode.Name)
                    {
                        sourceToTarget.Add(new EntryCommand(targetNode.Path, CommandAction.Rename, new RenameProperties
                        {
                            Name = name!
                        }));
                    }

                    success = true;
                }

                Console.SetCursorPosition(0, Console.CursorTop - 1);
                Console.Write(new string(' ', Console.BufferWidth));
                Console.SetCursorPosition(0, Console.CursorTop);
            }
        }
    }

    private static string GetNodeRepr(FileSystemTrie.Node node)
    {
        var nodeRepr = $"\"{node.OldPath}\"";
        if (node.OldName != node.Name)
            nodeRepr += $" -> {node.Name}";
        return nodeRepr;
    }

    // Validates that the given name is available on both source and target
    private static bool ValidateEntryName(
        SyncSourceManager sourceManager,
        SyncSourceManager targetManager,
        FileSystemCommandTrie sourceToTarget,
        FileSystemCommandTrie targetToSource,
        FileSystemTrie.Node sourceNode,
        FileSystemTrie.Node? targetNode,
        string? name,
        out string? message)
    {
        if (string.IsNullOrEmpty(name))
        {
            message = "The new name cannot be an empty string.";
            return false;
        }

        /* This variable determines whether the two provided nodes correspond to the same original node.
         * 
         * If true: X_s -> A; X_t -> B
         * Their old names are the same (X == X), and a pair (X_s, X_t) is validated.
         * The name A, which is the current name of X_s, is used.
         * 
         * If false: X_s -> A; Y_t -> A
         * Their old names differ (X != Y), and a pair (Y_s, Y_t) is validated. 
         * The name Y, which is the current name of Y_s, is used.
         */
        bool identity = sourceNode.OldName == targetNode.OldName;
        if (name != (identity ? sourceNode.Name : targetNode.OldName))
        {
            var sourcePathNew = Path.Combine(sourceNode.Parent.Path, name);
            if (targetToSource.TryGetValue(sourcePathNew, out var command)
                && command.RenameProperties is not null
                && command.RenameProperties.Value.Name == name)
            {
                message = $"The given name is already reserved on the source for \"{command.Path}\".";
                return false;
            }
            else if (FileOrDirectoryExists(Path.Combine(sourceManager.Root, sourcePathNew)))
            {
                message = "The given name is already occupied on the source.";
                return false;
            }
        }

        if (targetNode is null || name != targetNode.Name)
        {
            var targetPathNew = Path.Combine(targetNode.Parent.Path, name);
            if (sourceToTarget.TryGetValue(targetPathNew, out var command)
                && command.RenameProperties is not null
                && command.RenameProperties.Value.Name == name)
            {
                message = $"The given name is already reserved on the target for \"{command.Path}\".";
                return false;
            }
            else if (FileOrDirectoryExists(Path.Combine(targetManager.Root, targetPathNew)))
            {
                message = "The given name is already occupied on the target.";
                return false;
            }
        }

        message = null;
        return true;
    }
    private static bool FileOrDirectoryExists(string path) => File.Exists(path) || Directory.Exists(path);

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
                    foreach (var sourceSubnode in sourceNode.NodesByNames.Values.OrderBy(n => n.Value?.Action, 
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
                    foreach (var targetSubnode in targetNode.NodesByNames.Values)
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

                foreach (var sourceSubnode in sourceNode.NodesByNames.Values)
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
            args.SourceResult.Applied.Add(changeActual with
            {
                Timestamp = changeActual.ChangeProperties?.LastWriteTime ?? changeActual.Timestamp ?? DateTimeStaticProvider.Now
            });
        else
            args.SourceResult.Failed.Add(changeActual);
    }

    // TODO: consider applying changes through SyncSourceManager's, 
    // so as to keep the Index up-to-date and maybe something else
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

                // Source directory is altered -> the change is invalid
                if (sourceDirectory != changeProps)
                    return false;

                // Target directory is present -> the change is invalid
                targetDirectory = new DirectoryInfo(targetPath);
                if (targetDirectory.Exists)
                    return false;

                // TODO: implicit file deletion; consider removing
                // Target file is present -> replace it
                targetFile = new FileInfo(targetPath);
                if (targetFile.Exists)
                    targetFile.Delete();

                // Note: directory creation does not require contents comparison,
                // as all contents are written as separate entries in database
                targetDirectory.Create();
                targetDirectory.LastWriteTime = changeProps.Value.LastWriteTime;
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

                // TODO: implicit directory deletion; consider removing
                // Target directory is present -> replace it
                targetDirectory = new DirectoryInfo(targetPath);
                if (targetDirectory.Exists)
                    targetDirectory.Delete(recursive: true);

                sourceFile.CopyTo(targetPath);
                sourceFile.LastWriteTime = changeProps.Value.LastWriteTime;
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
                targetFile.LastWriteTime = changeProps.Value.LastWriteTime;
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

    private async Task<FileSystemTrie> GetSourceDelta()
    {
        var trie = new FileSystemTrie();
        await trie.AddRange(_sourceManager.Database.EnumerateEventsAsync());
        return trie;
    }

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

        // TODO: if an entry is deleted on the target and (!) there is no LastSyncTime provided, 
        // trie creation will fail as it requires a timestamp; perform manual resolve
        var timestamp = _targetManager.Database.LastSyncTime;
        var stack = new Stack<(DirectoryInfo, CustomDirectoryInfo)>();
        stack.Push((target, source));
        while (stack.TryPop(out var pair))
        {
            var (targetDir, sourceDir) = pair;

            // Directories
            foreach (var targetSubdir in targetDir.EnumerateDirectories())
            {
                if (_targetManager.Filter.ServiceExcludes(targetSubdir.FullName) || 
                    _targetManager.Filter.UserExcludes(targetSubdir.FullName, EntryType.Directory))
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
                if (_targetManager.Filter.UserExcludes(targetFile.FullName, EntryType.File))
                    continue;

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
