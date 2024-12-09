using DatagentMonitor.FileSystem;

namespace DatagentMonitor.Synchronization.Conflicts;

internal partial class ContentsConflict
{
    internal static class Options
    {
        public static Option DeleteSource => new("Delete source entry", DeleteEntry);
        public static Option DeleteTarget => new("Delete target entry", args => DeleteEntry(args.Swap()));
        private static void DeleteEntry(ResolveConflictArgs args)
        {
            args.SourceToTarget.Add(new EntryChange(
                args.SourceNode.Value.Timestamp, args.SourceNode.Path,
                args.TargetNode.Type, EntryAction.Delete,
                null, null));

            args.TargetNode.ClearSubtree();
        }

        public static Option CopySourceToTarget => new("Copy source entry to the target",
            args => CopyEntry(args));
        public static Option CopySourceToTargetRecursive => new("Copy source entry to the target w/ all subentries",
            args => CopyEntry(args, recursive: true));
        public static Option CopySourceToTargetOverwrite => new("Copy source entry to the target, overwriting the existing entry",
            args => CopyEntry(args, overwrite: true));
        public static Option CopyTargetToSource => new("Copy source entry to the target",
            args => CopyEntry(args.Swap()));
        public static Option CopyTargetToSourceOverwrite => new("Copy target entry to the source, overwriting the existing entry",
            args => CopyEntry(args.Swap(), overwrite: true));
        public static Option CopyTargetToSourceRecursive => new("Copy target entry to the source w/ all subentries",
            args => CopyEntry(args.Swap(), recursive: true));
        private static void CopyEntry(ResolveConflictArgs args, bool overwrite = false, bool recursive = false)
        {
            var sourceChange = args.SourceNode.Value!;
            if (overwrite)
            {
                args.SourceToTarget.Add(new EntryChange(
                    sourceChange.Timestamp, args.TargetNode.Path,
                    args.TargetNode.Type, EntryAction.Change,
                    sourceChange.RenameProperties, sourceChange.ChangeProperties));
            }
            else
            {
                if (args.TargetNode.Value?.Action is not EntryAction.Delete)
                    args.SourceToTarget.Add(new EntryChange(
                        sourceChange.Timestamp, args.TargetNode.Path,
                        args.TargetNode.Type, EntryAction.Delete,
                        null, null));

                switch (sourceChange.Type)
                {
                    case EntryType.Directory:
                        if (recursive)
                        {
                            // Only remove the subtree; the node itself will get removed later
                            args.SourceToTarget.AddRange(args.SourceManager.EnumerateCreatedDirectory(
                                new DirectoryInfo(Path.Combine(args.SourceManager.Root, args.SourceNode.Path))));

                            args.SourceNode.ClearSubtree();
                        }
                        else
                        {
                            args.SourceToTarget.Add(new EntryChange(
                                sourceChange.Timestamp, args.SourceNode.Path,
                                args.SourceNode.Type, EntryAction.Create,
                                null, sourceChange.ChangeProperties));
                        }
                        break;

                    case EntryType.File:
                        args.SourceToTarget.Add(new EntryChange(
                            sourceChange.Timestamp, args.SourceNode.Path,
                            args.SourceNode.Type, EntryAction.Create,
                            null, sourceChange.ChangeProperties ?? new ChangeProperties(
                                new FileInfo(Path.Combine(args.SourceManager.Root, args.SourceNode.Path)))));
                        break;
                }

                args.TargetNode.ClearSubtree();
            }
        }

    }
}
