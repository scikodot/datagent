namespace DatagentMonitor.Synchronization.Conflicts;

internal class NamesConflict : ConflictBase
{
    public NamesConflict(ResolveConflictArgs args) : base(args)
    {

    }

    // TODO: no conflict if both renames have the same new name; implement and add test
    public override void Resolve()
    {
        throw new NotImplementedException();
    }

    protected sealed override void ResolveWithOptions(params Option[] options)
    {
        throw new NotImplementedException();
    }
}
