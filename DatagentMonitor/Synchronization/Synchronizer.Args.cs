using DatagentMonitor.Collections;

namespace DatagentMonitor.Synchronization;

internal partial class Synchronizer
{
    private interface ISwappable<T>
    {
        public T Swap();
    }

    private readonly record struct ResolveConflictArgs(
        SyncSourceManager SourceManager,
        SyncSourceManager TargetManager,
        FileSystemTrie SourceToTarget,
        FileSystemTrie TargetToSource,
        List<string> SourcePath,
        List<string> TargetPath,
        FileSystemTrie.Node SourceNode,
        FileSystemTrie.Node TargetNode) : ISwappable<ResolveConflictArgs>
    {
        public ResolveConflictArgs Swap() => new(
            TargetManager, SourceManager,
            TargetToSource, SourceToTarget,
            TargetPath, SourcePath,
            TargetNode, SourceNode);
    }

    private readonly record struct ApplyChangeArgs(
        SyncSourceManager SourceManager,
        SyncSourceManager TargetManager,
        IEnumerable<string> SourcePath,
        IEnumerable<string> TargetPath,
        FileSystemTrie.Node? SourceNode,
        FileSystemTrie.Node? TargetNode,
        SynchronizationResult SourceResult,
        SynchronizationResult TargetResult) : ISwappable<ApplyChangeArgs>
    {
        public ApplyChangeArgs Swap() => new(
            TargetManager, SourceManager,
            TargetPath, SourcePath,
            TargetNode, SourceNode,
            TargetResult, SourceResult);
    }

    private static void SwapArgs<T>(Action<T> handler, T args, Func<T, bool> predicate) where T : ISwappable<T>
    {
        if (predicate(args))
            args = args.Swap();

        handler(args);
    }
}
