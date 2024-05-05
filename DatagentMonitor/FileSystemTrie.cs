using DatagentMonitor.FileSystem;

namespace DatagentMonitor;

internal class FileSystemTrie
{
    private readonly FileSystemTrieNode _root = new();
    public FileSystemTrieNode Root => _root;

    public void Add(FileSystemEntryChange change)
    {
        var parts = Path.TrimEndingDirectorySeparator(change.Path).Split(Path.DirectorySeparatorChar);
        var parent = _root;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (!parent.Children.TryGetValue(parts[i], out var next))
            {
                next = new FileSystemTrieNode();
                parent.Children.Add(parts[i], next);
            }

            parent = next;
        }

        if (!parent.Children.TryGetValue(parts[^1], out var child))
        {
            child = new FileSystemTrieNode(change);
            switch (change.Action)
            {
                case FileSystemEntryAction.Rename:
                    parent.Children.Add(change.Properties.RenameProps!.Name, child);
                    break;
                case FileSystemEntryAction.Create:
                case FileSystemEntryAction.Change:
                case FileSystemEntryAction.Delete:
                    parent.Children.Add(change.OldName, child);
                    break;
            }
        }
        else if (child.Value == null)
        {
            // Empty nodes' changes are only available for directories
            if (!CustomFileSystemInfo.IsDirectory(change.Path))
                throw new InvalidOperationException($"Attempt to alter an existing node with a file change: {change.Path}");

            switch (change.Action)
            {
                // Create is only available for new nodes
                case FileSystemEntryAction.Create:
                    throw new InvalidOperationException($"Attempt to create an already existing node: {change.Path}");

                case FileSystemEntryAction.Rename:
                    child.Value = change;

                    // Re-attach the node to the parent with the new name
                    parent.Children.Remove(parts[^1]);
                    parent.Children.Add(change.Properties.RenameProps!.Name, child);
                    break;

                case FileSystemEntryAction.Delete:
                    child.Value = change;

                    // Remove all contents' changes, if any
                    child.Children.Clear();
                    break;
            }
        }
        else
        {
            var actionOld = child.Value!.Action;
            var actionNew = change.Action;
            switch (actionNew)
            {
                case FileSystemEntryAction.Create:
                    switch (actionOld)
                    {
                        // Create after Create or Rename or Change -> impossible
                        case FileSystemEntryAction.Create:
                        case FileSystemEntryAction.Rename:
                        case FileSystemEntryAction.Change:
                            throw new InvalidActionSequenceException(actionOld, actionNew);

                        // Create after Delete -> 2 options:
                        // 1. The same entry has got restored
                        // 2. Another entry has been created with the same name
                        // 
                        // For directories, the two entries are effectively the same, only their contents can differ.
                        // For files, instead of checking their equality, we simply treat the entry as being changed.
                        case FileSystemEntryAction.Delete:
                            // TODO: add directory contents to database on delete!
                            // If a directory is deleted and then created with the same name
                            // but different contents, those contents changes won't be displayed in delta.
                            if (CustomFileSystemInfo.IsDirectory(change.Path))
                                child.Value = null;
                            else
                                change.Action = FileSystemEntryAction.Change;
                            break;
                    }
                    break;

                case FileSystemEntryAction.Rename:
                    switch (actionOld)
                    {
                        // Rename after Create -> ok, but keep the previous action
                        // and use the new path instead of storing the new name in RenameProps
                        case FileSystemEntryAction.Create:
                            child.Value!.Path = change.Path;
                            child.Value!.Timestamp = change.Timestamp;
                            parent.Children.Remove(parts[^1]);
                            parent.Children.Add(change.Properties.RenameProps!.Name, child);
                            break;

                        // Rename after Rename or Change -> ok, but keep the previous action
                        case FileSystemEntryAction.Rename:
                        case FileSystemEntryAction.Change:
                            child.Value!.Timestamp = change.Timestamp;
                            child.Value!.Properties.RenameProps = change.Properties.RenameProps!;
                            parent.Children.Remove(parts[^1]);
                            parent.Children.Add(change.Properties.RenameProps!.Name, child);
                            break;

                        // Rename after Delete -> impossible
                        case FileSystemEntryAction.Delete:
                            throw new InvalidActionSequenceException(actionOld, actionNew);
                    }
                    break;

                case FileSystemEntryAction.Change:
                    switch (actionOld)
                    {
                        // Change after Create -> ok, but keep the previous action
                        case FileSystemEntryAction.Create:
                            child.Value!.Timestamp = change.Timestamp;
                            child.Value!.Properties.ChangeProps = change.Properties.ChangeProps!;
                            break;

                        // Change after Rename or Change -> ok
                        case FileSystemEntryAction.Rename:
                        case FileSystemEntryAction.Change:
                            child.Value!.Action = FileSystemEntryAction.Change;
                            child.Value!.Timestamp = change.Timestamp;
                            child.Value!.Properties.ChangeProps = change.Properties.ChangeProps!;
                            break;

                        // Change after Delete -> impossible
                        case FileSystemEntryAction.Delete:
                            throw new InvalidActionSequenceException(actionOld, actionNew);
                    }
                    break;
                case FileSystemEntryAction.Delete:
                    switch (actionOld)
                    {
                        // Delete after Create -> a temporary entry, no need to track it
                        case FileSystemEntryAction.Create:
                            parent.Children.Remove(parts[^1]);
                            break;

                        // Delete after Rename or Change -> ok
                        case FileSystemEntryAction.Rename:
                        case FileSystemEntryAction.Change:
                            child.Value!.Action = FileSystemEntryAction.Delete;
                            child.Value!.Properties.RenameProps = null;
                            child.Value!.Properties.ChangeProps = null;
                            child.Children.Clear();
                            break;

                        // Delete again -> impossible
                        case FileSystemEntryAction.Delete:
                            throw new InvalidActionSequenceException(actionOld, actionNew);
                    }
                    break;
            }
        }
    }
}

internal class FileSystemTrieNode
{
    private FileSystemEntryChange? _value;
    public FileSystemEntryChange? Value
    {
        get => _value;
        set => _value = value;
    }

    private readonly Dictionary<string, FileSystemTrieNode> _children = new();
    public Dictionary<string, FileSystemTrieNode> Children => _children;

    public FileSystemTrieNode() { }

    public FileSystemTrieNode(FileSystemEntryChange? value)
    {
        _value = value;
    }
}
