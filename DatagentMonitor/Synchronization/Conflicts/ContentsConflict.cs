using DatagentMonitor.FileSystem;

namespace DatagentMonitor.Synchronization.Conflicts;

internal partial class ContentsConflict : ConflictBase
{
    public ContentsConflict(ResolveConflictArgs args) : base(args)
    {

    }

    public sealed override void Resolve()
    {
        base.Resolve();

        var sourceChange = SourceNode.Value;
        var targetChange = TargetNode.Value;
        switch (SourceNode.Type, sourceChange.Action, TargetNode.Type, targetChange.Action)
        {
            // No contents conflict
            case (EntryType.File, EntryAction.Rename, EntryType.File, EntryAction.Rename):
            case (EntryType.File, EntryAction.Delete, EntryType.File, EntryAction.Delete):
            case (EntryType.Directory, EntryAction.Rename, EntryType.Directory, EntryAction.Rename):
            case (EntryType.Directory, EntryAction.Delete, EntryType.Directory, EntryAction.Delete):
                Console.WriteLine("No conflict.");
                break;

            // Target entry is only renamed, so its contents are not changed -> copy contents to target
            case (EntryType.File, EntryAction.Change, EntryType.File, EntryAction.Rename):
            case (EntryType.Directory, EntryAction.Change, EntryType.Directory, EntryAction.Rename):
                SourceToTarget.Add(new EntryChange(
                    sourceChange.Timestamp,
                    TargetNode.Path,
                    SourceNode.Type, sourceChange.Action,
                    null, sourceChange.ChangeProperties));
                break;

            // Source entry is only renamed, so its contents are not changed -> copy contents to source
            case (EntryType.File, EntryAction.Rename, EntryType.File, EntryAction.Change):
            case (EntryType.Directory, EntryAction.Rename, EntryType.Directory, EntryAction.Change):
                TargetToSource.Add(new EntryChange(
                    targetChange.Timestamp,
                    SourceNode.Path,
                    TargetNode.Type, targetChange.Action,
                    null, targetChange.ChangeProperties));
                break;

            case (EntryType.File, EntryAction.Create, EntryType.Directory, EntryAction.Rename):
            case (EntryType.File, EntryAction.Create, EntryType.Directory, EntryAction.Change):
                ResolveWithOptions(
                    Options.CopySourceToTarget,
                    Options.CopyTargetToSourceRecursive);
                break;

            case (EntryType.Directory, EntryAction.Create, EntryType.Directory, EntryAction.Create):
            case (EntryType.Directory, EntryAction.Change, EntryType.Directory, EntryAction.Change):
            case (EntryType.Directory, EntryAction.Create, EntryType.File, EntryAction.Create):
            case (EntryType.Directory, EntryAction.Create, EntryType.File, EntryAction.Rename):
            case (EntryType.Directory, EntryAction.Create, EntryType.File, EntryAction.Change):
            case (EntryType.File, EntryAction.Create, EntryType.Directory, EntryAction.Create):
            case (EntryType.File, EntryAction.Rename, EntryType.Directory, EntryAction.Create):
            case (EntryType.File, EntryAction.Change, EntryType.Directory, EntryAction.Create):
            case (EntryType.File, EntryAction.Create, EntryType.File, EntryAction.Create):
            case (EntryType.File, EntryAction.Change, EntryType.File, EntryAction.Change):
                ResolveWithOptions(
                    Options.CopySourceToTarget,
                    Options.CopyTargetToSource);
                break;

            case (EntryType.Directory, EntryAction.Rename, EntryType.Directory, EntryAction.Delete):
            case (EntryType.Directory, EntryAction.Change, EntryType.Directory, EntryAction.Delete):
            case (EntryType.Directory, EntryAction.Rename, EntryType.File, EntryAction.Create):
            case (EntryType.Directory, EntryAction.Change, EntryType.File, EntryAction.Create):
                ResolveWithOptions(
                    Options.CopySourceToTargetRecursive,
                    Options.DeleteSource);
                break;

            case (EntryType.Directory, EntryAction.Create, EntryType.File, EntryAction.Delete):
            case (EntryType.File, EntryAction.Create, EntryType.Directory, EntryAction.Delete):
            case (EntryType.File, EntryAction.Rename, EntryType.File, EntryAction.Delete):
            case (EntryType.File, EntryAction.Change, EntryType.File, EntryAction.Delete):
                ResolveWithOptions(
                    Options.CopySourceToTarget,
                    Options.DeleteSource);
                break;

            case (EntryType.Directory, EntryAction.Delete, EntryType.Directory, EntryAction.Rename):
            case (EntryType.Directory, EntryAction.Delete, EntryType.Directory, EntryAction.Change):
                ResolveWithOptions(
                    Options.DeleteTarget,
                    Options.CopyTargetToSourceRecursive);
                break;

            case (EntryType.Directory, EntryAction.Delete, EntryType.File, EntryAction.Create):
            case (EntryType.File, EntryAction.Delete, EntryType.Directory, EntryAction.Create):
            case (EntryType.File, EntryAction.Delete, EntryType.File, EntryAction.Rename):
            case (EntryType.File, EntryAction.Delete, EntryType.File, EntryAction.Change):
                ResolveWithOptions(
                    Options.DeleteTarget,
                    Options.CopyTargetToSource);
                break;
        }
    }

    protected sealed override void ResolveWithOptions(params Option[] options)
    {
        base.ResolveWithOptions(options);
    }
}
