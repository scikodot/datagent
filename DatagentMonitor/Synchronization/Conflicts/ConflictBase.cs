using DatagentMonitor.Collections;

namespace DatagentMonitor.Synchronization.Conflicts;

readonly record struct ResolveConflictArgs(
    SyncSourceManager SourceManager,
    SyncSourceManager TargetManager,
    FileSystemTrie SourceToTarget,
    FileSystemTrie TargetToSource,
    //List<string> SourcePath,
    //List<string> TargetPath,
    FileSystemTrie.Node SourceNode,
    FileSystemTrie.Node TargetNode)
{
    public ResolveConflictArgs Swap() => new(
        TargetManager, SourceManager,
        TargetToSource, SourceToTarget,
        TargetNode, SourceNode);
}

internal class Option
{
    private readonly string _description;
    public string Description => _description;

    private readonly Action<ResolveConflictArgs> _func;

    public Option(string description, Action<ResolveConflictArgs> func)
    {
        _description = description;
        _func = func;
    }

    public void Apply(ResolveConflictArgs args) => _func(args);

    public override string ToString() => Description;
}

internal abstract class ConflictBase
{
    private readonly ResolveConflictArgs _args;

    protected ResolveConflictArgs Args => _args;

    public SyncSourceManager SourceManager => _args.SourceManager;
    public SyncSourceManager TargetManager => _args.TargetManager;

    public FileSystemTrie SourceToTarget => _args.SourceToTarget;
    public FileSystemTrie TargetToSource => _args.TargetToSource;

    public FileSystemTrie.Node SourceNode => _args.SourceNode;
    public FileSystemTrie.Node TargetNode => _args.TargetNode;

    public ConflictBase(ResolveConflictArgs args)
    {
        _args = args;
    }

    public abstract void Resolve();

    protected abstract void ResolveWithOptions(params Option[] options);
}
