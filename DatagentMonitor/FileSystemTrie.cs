using DatagentMonitor.FileSystem;
using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace DatagentMonitor;

internal class FileSystemTrie : IEnumerable<FileSystemEntryChange>
{
    private readonly FileSystemTrieNode _root = new();
    public FileSystemTrieNode Root => _root;

    private readonly LinkedList<FileSystemTrieNode> _values = new();

    public int Count => _values.Count;

    // Nit TODO: trim dangling (empty) paths
    public void Add(FileSystemEntryChange change, bool stack = true)
    {
        var parts = Path.TrimEndingDirectorySeparator(change.Path).Split(Path.DirectorySeparatorChar);
        var parent = _root;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (!parent.Children.TryGetValue(parts[i], out var next))
            {
                next = new FileSystemTrieNode(parent);
                parent.Children.Add(parts[i], next);
            }

            parent = next;
        }

        if (!parent.Children.TryGetValue(parts[^1], out var child))
        {
            child = new FileSystemTrieNode(parent, change);
            child.Container = _values.AddLast(child);
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
                    child.Container = _values.AddLast(child);
                    child.Value = change;

                    // Re-attach the node to the parent with the new name
                    parent.Children.Remove(parts[^1]);
                    parent.Children.Add(change.Properties.RenameProps!.Name, child);
                    break;

                case FileSystemEntryAction.Delete:
                    child.Container = _values.AddLast(child);
                    child.Value = change;

                    // Remove all contents' changes, if any
                    RemoveSubtree(child);
                    break;
            }
        }
        else
        {
            if (!stack)
                return;

            var actionOld = child.Value.Action;
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
                            {
                                _values.Remove(child.Container!);
                                child.Container = null;
                                child.Value = null;
                            }
                            else
                            {
                                child.Value.Action = FileSystemEntryAction.Change;
                                child.Value.Timestamp = change.Timestamp;
                                child.Value.Properties.ChangeProps = change.Properties.ChangeProps!;
                            }
                            break;
                    }
                    break;

                case FileSystemEntryAction.Rename:
                    var properties = change.Properties.RenameProps!;
                    switch (actionOld)
                    {
                        // Rename after Create -> ok, but keep the previous action
                        // and use the new path instead of storing the new name in RenameProps
                        case FileSystemEntryAction.Create:
                            child.Value.Path = CustomFileSystemInfo.ReplaceEntryName(change.Path, properties.Name);
                            child.Value.Timestamp = change.Timestamp;
                            parent.Children.Remove(parts[^1]);
                            parent.Children.Add(properties.Name, child);
                            break;

                        // Rename after Rename or Change -> ok, but keep the previous action
                        case FileSystemEntryAction.Rename:
                        case FileSystemEntryAction.Change:
                            child.Value.Timestamp = change.Timestamp;
                            child.Value.Properties.RenameProps = properties;
                            parent.Children.Remove(parts[^1]);
                            parent.Children.Add(properties.Name, child);
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
                            child.Value.Timestamp = change.Timestamp;
                            child.Value.Properties.ChangeProps = change.Properties.ChangeProps!;
                            break;

                        // Change after Rename or Change -> ok
                        case FileSystemEntryAction.Rename:
                        case FileSystemEntryAction.Change:
                            child.Value.Action = FileSystemEntryAction.Change;
                            child.Value.Timestamp = change.Timestamp;
                            child.Value.Properties.ChangeProps = change.Properties.ChangeProps!;
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
                            RemoveSubtree(child, removeRoot: true);
                            parent.Children.Remove(parts[^1]);
                            child.Parent = null;
                            break;

                        // Delete after Rename or Change -> ok
                        case FileSystemEntryAction.Rename:
                        case FileSystemEntryAction.Change:
                            child.Value.Action = FileSystemEntryAction.Delete;
                            child.Value.Properties.RenameProps = null;
                            child.Value.Properties.ChangeProps = null;
                            RemoveSubtree(child);
                            break;

                        // Delete again -> impossible
                        case FileSystemEntryAction.Delete:
                            throw new InvalidActionSequenceException(actionOld, actionNew);
                    }
                    break;
            }
        }
    }

    public bool Remove(string path, [MaybeNullWhen(false)] out FileSystemEntryChange change)
    {
        change = null;
        var parts = Path.TrimEndingDirectorySeparator(path).Split(Path.DirectorySeparatorChar);

        // Search for the change among the renamed entries
        var curr = _root;
        foreach (var part in parts)
        {
            if (!curr.ChildrenRenamed.TryGetValue(part, out var next))
            {
                curr = _root;
                break;
            }

            curr = next;
        }

        // If failed, search for the change among all other changes
        if (curr == _root)
        {
            foreach (var part in parts)
            {
                if (!curr.Children.TryGetValue(part, out var next))
                    return false;

                curr = next;
            }
        }

        if (curr.Value != null)
        {
            change = curr.Value;
            _values.Remove(curr.Container!);
            curr.Container = null;
            curr.Value = null;
        }

        return change != null;
    }

    public void RemoveSubtree(string path)
    {
        var parts = Path.TrimEndingDirectorySeparator(path).Split(Path.DirectorySeparatorChar);
        var curr = _root;
        foreach (var part in parts)
        {
            if (!curr.Children.TryGetValue(part, out var next) && 
                !curr.ChildrenRenamed.TryGetValue(part, out next))
                throw new ArgumentException($"No node found for the given path: {path}");

            curr = next;
        }

        RemoveSubtree(curr);
    }

    private void RemoveSubtree(FileSystemTrieNode root, bool removeRoot = false)
    {
        foreach (var child in root.Children)
            RemoveSubtree(child.Value, removeRoot: true);

        foreach (var child in root.ChildrenRenamed)
            RemoveSubtree(child.Value, removeRoot: true);

        root.Children.Clear();
        root.ChildrenRenamed.Clear();

        if (removeRoot && root.Value != null)
        {
            _values.Remove(root.Container!);
            root.Container = null;
            root.Value = null;
        }
    }

    public void Close()
    {
        // Flip all renamed entries to their old names
        foreach (var node in _values)
        {
            if (node.Value!.Properties.RenameProps != null)
            {
                node.Parent!.Children.Remove(node.Value.Properties.RenameProps!.Name);
                node.Parent!.ChildrenRenamed.Add(node.Value.OldName, node);
            }
        }
    }

    public IEnumerator<FileSystemEntryChange> GetEnumerator() => _values.Select(n => n.Value!).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal class FileSystemTrieNode
{
    // This is a reference to this node in the linked list.
    // The following properties must hold:
    // Container is null <=> Value is null
    public LinkedListNode<FileSystemTrieNode>? Container { get; set; }

    private FileSystemTrieNode? _parent;
    public FileSystemTrieNode? Parent
    {
        get => _parent;
        set => _parent = value;
    }

    private FileSystemEntryChange? _value;
    public FileSystemEntryChange? Value
    {
        get => _value;
        set => _value = value;
    }

    private readonly Dictionary<string, FileSystemTrieNode> _children = new();
    public Dictionary<string, FileSystemTrieNode> Children => _children;

    private readonly Dictionary<string, FileSystemTrieNode> _childrenRenamed = new();
    public Dictionary<string, FileSystemTrieNode> ChildrenRenamed => _childrenRenamed;

    public FileSystemTrieNode() { }

    public FileSystemTrieNode(FileSystemTrieNode parent)
    {
        _parent = parent;
    }

    public FileSystemTrieNode(FileSystemTrieNode parent, FileSystemEntryChange value)
    {
        _parent = parent;
        _value = value;
    }
}
