using DatagentMonitor.FileSystem;

namespace DatagentMonitor.Synchronization.Conflicts;

internal class NamesConflict : ConflictBase
{
    public NamesConflict(ResolveConflictArgs args) : base(args)
    {

    }

    // TODO: no conflict if both renames have the same new name; implement and add test
    public sealed override void Resolve()
    {
        base.Resolve();

        var sourceChange = SourceNode.Value;
        var targetChange = TargetNode.Value;
        switch (SourceNode.Type, sourceChange.Action, TargetNode.Type, targetChange.Action)
        {
            // No names conflict
            case (EntryType.Directory, EntryAction.Create, EntryType.Directory, EntryAction.Create):
            case (EntryType.Directory, EntryAction.Rename, EntryType.Directory, EntryAction.Delete):
            case (EntryType.Directory, EntryAction.Change, EntryType.Directory, EntryAction.Delete):
            case (EntryType.Directory, EntryAction.Delete, EntryType.Directory, EntryAction.Rename):
            case (EntryType.Directory, EntryAction.Delete, EntryType.Directory, EntryAction.Change):
            case (EntryType.Directory, EntryAction.Delete, EntryType.Directory, EntryAction.Delete):
            case (EntryType.Directory, EntryAction.Create, EntryType.File, EntryAction.Create):
            case (EntryType.Directory, EntryAction.Create, EntryType.File, EntryAction.Delete):
            case (EntryType.File, EntryAction.Create, EntryType.Directory, EntryAction.Create):
            case (EntryType.File, EntryAction.Create, EntryType.Directory, EntryAction.Delete):
            case (EntryType.File, EntryAction.Create, EntryType.File, EntryAction.Create):
            case (EntryType.File, EntryAction.Rename, EntryType.File, EntryAction.Delete):
            case (EntryType.File, EntryAction.Change, EntryType.File, EntryAction.Delete):
            case (EntryType.File, EntryAction.Delete, EntryType.File, EntryAction.Rename):
            case (EntryType.File, EntryAction.Delete, EntryType.File, EntryAction.Change):
            case (EntryType.File, EntryAction.Delete, EntryType.File, EntryAction.Delete):
                Console.WriteLine("No conflict.");
                break;

            case (EntryType.Directory, EntryAction.Change, EntryType.Directory, EntryAction.Rename):
            case (EntryType.Directory, EntryAction.Change, EntryType.Directory, EntryAction.Change):
            case (EntryType.Directory, EntryAction.Change, EntryType.File, EntryAction.Create):
            case (EntryType.File, EntryAction.Change, EntryType.Directory, EntryAction.Create):
            case (EntryType.File, EntryAction.Change, EntryType.File, EntryAction.Rename):
            case (EntryType.File, EntryAction.Change, EntryType.File, EntryAction.Change):
                if (sourceChange.RenameProperties is null || targetChange.RenameProperties is null)
                {
                    Console.WriteLine("No conflict.");
                    break;
                }

                // TODO: resolve
                break;


            case (EntryType.Directory, EntryAction.Rename, EntryType.Directory, EntryAction.Rename):
            case (EntryType.Directory, EntryAction.Rename, EntryType.Directory, EntryAction.Change):
            case (EntryType.Directory, EntryAction.Create, EntryType.File, EntryAction.Rename):
            case (EntryType.Directory, EntryAction.Create, EntryType.File, EntryAction.Change):
            case (EntryType.Directory, EntryAction.Rename, EntryType.File, EntryAction.Create):
            case (EntryType.Directory, EntryAction.Delete, EntryType.File, EntryAction.Create):
            case (EntryType.File, EntryAction.Create, EntryType.Directory, EntryAction.Rename):
            case (EntryType.File, EntryAction.Create, EntryType.Directory, EntryAction.Change):
            case (EntryType.File, EntryAction.Rename, EntryType.Directory, EntryAction.Create):
            case (EntryType.File, EntryAction.Delete, EntryType.Directory, EntryAction.Create):
            case (EntryType.File, EntryAction.Rename, EntryType.File, EntryAction.Rename):
            case (EntryType.File, EntryAction.Rename, EntryType.File, EntryAction.Change):
                // TODO: resolve
                break;
        }
    }

    protected sealed override void ResolveWithOptions(params Option[] options)
    {
        base.ResolveWithOptions(options);
    }
}
