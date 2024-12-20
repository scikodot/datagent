using DatagentMonitor.Collections;
using DatagentMonitor.FileSystem;

namespace DatagentMonitor.Synchronization.Conflicts;

readonly record struct ResolveConflictArgs(
    SyncSourceManager SourceManager,
    SyncSourceManager TargetManager,
    FileSystemCommandTrie SourceToTarget,
    FileSystemCommandTrie TargetToSource,
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

    public FileSystemCommandTrie SourceToTarget => _args.SourceToTarget;
    public FileSystemCommandTrie TargetToSource => _args.TargetToSource;

    public FileSystemTrie.Node SourceNode => _args.SourceNode;
    public FileSystemTrie.Node TargetNode => _args.TargetNode;

    public ConflictBase(ResolveConflictArgs args)
    {
        _args = args;
    }

    public virtual void Resolve()
    {
        var sourceChange = SourceNode.Value;
        var targetChange = TargetNode.Value;

        Console.WriteLine($"{sourceChange} <> {targetChange}");
        switch (SourceNode.Type, sourceChange.Action, TargetNode.Type, targetChange.Action)
        {
            // Invalid conflicts
            case (EntryType.File, EntryAction.Create, EntryType.File, EntryAction.Rename):
            case (EntryType.File, EntryAction.Create, EntryType.File, EntryAction.Change):
            case (EntryType.File, EntryAction.Create, EntryType.File, EntryAction.Delete):
            case (EntryType.File, EntryAction.Rename, EntryType.File, EntryAction.Create):
            case (EntryType.File, EntryAction.Change, EntryType.File, EntryAction.Create):
            case (EntryType.File, EntryAction.Delete, EntryType.File, EntryAction.Create):

            case (EntryType.File, EntryAction.Rename, EntryType.Directory, EntryAction.Rename):
            case (EntryType.File, EntryAction.Rename, EntryType.Directory, EntryAction.Change):
            case (EntryType.File, EntryAction.Rename, EntryType.Directory, EntryAction.Delete):

            case (EntryType.File, EntryAction.Change, EntryType.Directory, EntryAction.Rename):
            case (EntryType.File, EntryAction.Change, EntryType.Directory, EntryAction.Change):
            case (EntryType.File, EntryAction.Change, EntryType.Directory, EntryAction.Delete):

            case (EntryType.File, EntryAction.Delete, EntryType.Directory, EntryAction.Rename):
            case (EntryType.File, EntryAction.Delete, EntryType.Directory, EntryAction.Change):
            case (EntryType.File, EntryAction.Delete, EntryType.Directory, EntryAction.Delete):

            case (EntryType.Directory, EntryAction.Rename, EntryType.File, EntryAction.Rename):
            case (EntryType.Directory, EntryAction.Rename, EntryType.File, EntryAction.Change):
            case (EntryType.Directory, EntryAction.Rename, EntryType.File, EntryAction.Delete):

            case (EntryType.Directory, EntryAction.Change, EntryType.File, EntryAction.Rename):
            case (EntryType.Directory, EntryAction.Change, EntryType.File, EntryAction.Change):
            case (EntryType.Directory, EntryAction.Change, EntryType.File, EntryAction.Delete):

            case (EntryType.Directory, EntryAction.Delete, EntryType.File, EntryAction.Rename):
            case (EntryType.Directory, EntryAction.Delete, EntryType.File, EntryAction.Change):
            case (EntryType.Directory, EntryAction.Delete, EntryType.File, EntryAction.Delete):

            case (EntryType.Directory, EntryAction.Create, EntryType.Directory, EntryAction.Rename):
            case (EntryType.Directory, EntryAction.Create, EntryType.Directory, EntryAction.Change):
            case (EntryType.Directory, EntryAction.Create, EntryType.Directory, EntryAction.Delete):
            case (EntryType.Directory, EntryAction.Rename, EntryType.Directory, EntryAction.Create):
            case (EntryType.Directory, EntryAction.Change, EntryType.Directory, EntryAction.Create):
            case (EntryType.Directory, EntryAction.Delete, EntryType.Directory, EntryAction.Create):
                throw new InvalidConflictException(SourceNode.Type, sourceChange.Action, TargetNode.Type, targetChange.Action);
        }
    }

    protected virtual void ResolveWithOptions(params Option[] options)
    {
        switch (options.Length)
        {
            case 0:
                throw new ArgumentException("No resolve options were provided.");

            // Single option -> no need to ask the user, just apply
            case 1:
                options[0].Apply(Args);
                break;

            // Multiple options -> show the user dialogue
            default:
                for (int i = 0; i < options.Length; i++)
                {
                    Console.WriteLine($"[{i + 1}] - {options[i].Description}");
                }

                var key = Console.ReadKey(intercept: true);
                if (key.Key < ConsoleKey.D1 || key.Key >= options.Length + ConsoleKey.D1)
                    throw new ArgumentException("The key was out of bounds.");

                options[key.Key - ConsoleKey.D1].Apply(Args);
                break;
        }
    }
}
