using DatagentMonitor.FileSystem;

namespace DatagentMonitor.Synchronization.Conflicts;

internal class NamesConflict : ConflictBase
{
    private static InvalidOperationException DirectResolveDisallowedEx => new(
        "Resolving name conflicts directly is not allowed."
    );

    public NamesConflict(ResolveConflictArgs args) : base(args)
    {

    }

    public sealed override void Resolve() => throw DirectResolveDisallowedEx;

    protected sealed override void ResolveWithOptions(params Option[] options) => throw DirectResolveDisallowedEx;

    public bool IsConflict()
    {
        base.Resolve();

        var sourceChange = SourceNode.Value;
        var targetChange = TargetNode.Value;
        return (SourceNode.Type, sourceChange.Action, TargetNode.Type, targetChange.Action) switch
        {
            // No names conflict
            (EntryType.Directory, EntryAction.Create, EntryType.Directory, EntryAction.Create) or 
            (EntryType.Directory, EntryAction.Rename, EntryType.Directory, EntryAction.Delete) or 
            (EntryType.Directory, EntryAction.Change, EntryType.Directory, EntryAction.Delete) or 
            (EntryType.Directory, EntryAction.Delete, EntryType.Directory, EntryAction.Rename) or 
            (EntryType.Directory, EntryAction.Delete, EntryType.Directory, EntryAction.Change) or 
            (EntryType.Directory, EntryAction.Delete, EntryType.Directory, EntryAction.Delete) or 
            (EntryType.Directory, EntryAction.Create, EntryType.File, EntryAction.Create) or 
            (EntryType.Directory, EntryAction.Create, EntryType.File, EntryAction.Delete) or 
            (EntryType.File, EntryAction.Create, EntryType.Directory, EntryAction.Create) or 
            (EntryType.File, EntryAction.Create, EntryType.Directory, EntryAction.Delete) or 
            (EntryType.File, EntryAction.Create, EntryType.File, EntryAction.Create) or 
            (EntryType.File, EntryAction.Rename, EntryType.File, EntryAction.Delete) or 
            (EntryType.File, EntryAction.Change, EntryType.File, EntryAction.Delete) or 
            (EntryType.File, EntryAction.Delete, EntryType.File, EntryAction.Rename) or 
            (EntryType.File, EntryAction.Delete, EntryType.File, EntryAction.Change) or 
            (EntryType.File, EntryAction.Delete, EntryType.File, EntryAction.Delete) 
                => false,

            // TODO: add test for equal current names
            // Names conflict iff both entries are renamed
            // and have either the same old names or the same current names
            (EntryType.Directory, EntryAction.Change, EntryType.Directory, EntryAction.Rename) or 
            (EntryType.Directory, EntryAction.Change, EntryType.Directory, EntryAction.Change) or 
            (EntryType.Directory, EntryAction.Change, EntryType.File, EntryAction.Create) or 
            (EntryType.File, EntryAction.Change, EntryType.Directory, EntryAction.Create) or 
            (EntryType.File, EntryAction.Change, EntryType.File, EntryAction.Rename) or
            (EntryType.File, EntryAction.Change, EntryType.File, EntryAction.Change) 
                => sourceChange.RenameProperties is not null && targetChange.RenameProperties is not null
                && ((SourceNode.OldName == TargetNode.OldName && SourceNode.Name != TargetNode.Name)
                    || (SourceNode.OldName != TargetNode.OldName && SourceNode.Name == TargetNode.Name)),

            // Everything else is considered a conflict
            _ => true,
        };
    }
}
