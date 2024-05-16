using DatagentMonitor.FileSystem;
using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace DatagentMonitor;

internal class FileSystemTrie : ICollection<FileSystemEntryChange>
{
    private readonly bool _stack;

    private readonly FileSystemTrieNode _root = new();
    public FileSystemTrieNode Root => _root;

    private readonly List<LinkedList<FileSystemTrieNode>> _levels = new();
    public List<LinkedList<FileSystemTrieNode>> Levels => _levels;

    public int Count => _levels.Sum(x => x.Count);

    public bool IsReadOnly => false;

    public IEnumerable<FileSystemEntryChange> Values => _levels.SelectMany(l => l.Select(n => n.Value!));

    public FileSystemTrie(bool stack = true)
    {
        _stack = stack;
    }

    public void Add(FileSystemEntryChange change)
    {
        var parent = _root;
        var parts = Path.TrimEndingDirectorySeparator(change.Path).Split(Path.DirectorySeparatorChar);
        var level = parts.Length - 1;
        for (int i = 0; i < level; i++)
        {
            if (!parent.Children.TryGetValue(parts[i], out var next))
            {
                next = new FileSystemTrieNode(parent);
                parent.Children.Add(parts[i], next);
            }

            parent = next;
        }

        for (int i = 0; i <= level - _levels.Count; i++)
            _levels.Add(new());

        if (!parent.Children.TryGetValue(parts[^1], out var child))
        {
            child = new FileSystemTrieNode(parent, _levels[level], change);
            switch (change.Action)
            {
                case FileSystemEntryAction.Rename:
                    parent.Children.Add(change.Properties.RenameProps!.Name, child);
                    parent.Names.Add(change.OldName, child);
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
                    // Re-attach the node to the parent with the new name
                    parent.Children.Remove(parts[^1]);
                    parent.Children.Add(change.Properties.RenameProps!.Name, child);
                    parent.Names.Add(change.OldName, child);
                    break;

                case FileSystemEntryAction.Delete:
                    child.ClearSubtree();
                    break;
            }

            child.Initialize(_levels[level], change);
        }
        else
        {
            if (!_stack)
                throw new ArgumentException($"A change for {change.Path} is already present, and stacking is disallowed.");

            switch (change.Action, child.Value.Action)
            {
                // Create after Delete -> 2 options:
                // 1. The same entry has got restored
                // 2. Another entry has been created with the same name
                // 
                // For directories, the two entries are effectively the same, only their contents can differ.
                // For files, instead of checking their equality, we simply treat the entry as being changed.
                case (FileSystemEntryAction.Create, FileSystemEntryAction.Delete):
                    // TODO: add directory contents to database on delete!
                    // If a directory is deleted and then created with the same name
                    // but different contents, those contents changes won't be displayed in delta.
                    if (CustomFileSystemInfo.IsDirectory(change.Path))
                    {
                        child.Clear();
                    }
                    else
                    {
                        child.Value = new FileSystemEntryChange
                        {
                            Path = child.Value.Path,
                            Action = FileSystemEntryAction.Change,
                            Timestamp = change.Timestamp,
                            Properties = change.Properties
                        };
                    }
                    break;

                // Rename after Create -> ok, but keep the previous action
                // and use the new path instead of storing the new name in RenameProps
                case (FileSystemEntryAction.Rename, FileSystemEntryAction.Create):
                    child.Value = new FileSystemEntryChange
                    {
                        Path = CustomFileSystemInfo.ReplaceEntryName(change.Path, change.Properties.RenameProps!.Name),
                        Action = child.Value.Action,
                        Timestamp = change.Timestamp
                    };
                    child.MoveTo(change.Properties.RenameProps!.Name);
                    break;

                // Rename after Rename or Change -> ok, but keep the previous action
                case (FileSystemEntryAction.Rename, FileSystemEntryAction.Rename):
                case (FileSystemEntryAction.Rename, FileSystemEntryAction.Change):
                    child.Value = new FileSystemEntryChange
                    {
                        Path = child.Value.Path, 
                        Action = child.Value.Action, 
                        Timestamp = change.Timestamp, 
                        Properties = new FileSystemEntryChangeProperties
                        {
                            RenameProps = change.Properties.RenameProps!, 
                            ChangeProps = child.Value.Properties.ChangeProps
                        }
                    };
                    child.MoveTo(change.Properties.RenameProps!.Name);
                    parent.Names.TryAdd(child.Value.OldName, child);
                    break;

                // Change after Create -> ok, but keep the previous action
                case (FileSystemEntryAction.Change, FileSystemEntryAction.Create):
                    child.Value = new FileSystemEntryChange
                    {
                        Path = child.Value.Path,
                        Action = child.Value.Action,
                        Timestamp = change.Timestamp,
                        Properties = change.Properties
                    };
                    break;

                // Change after Rename or Change -> ok
                case (FileSystemEntryAction.Change, FileSystemEntryAction.Rename):
                case (FileSystemEntryAction.Change, FileSystemEntryAction.Change):
                    child.Value = new FileSystemEntryChange
                    {
                        Path = child.Value.Path, 
                        Action = FileSystemEntryAction.Change, 
                        Timestamp = change.Timestamp, 
                        Properties = new FileSystemEntryChangeProperties
                        {
                            RenameProps = child.Value.Properties.RenameProps,
                            ChangeProps = change.Properties.ChangeProps!
                        }
                    };
                    break;

                // Delete after Create -> a temporary entry, no need to track it
                case (FileSystemEntryAction.Delete, FileSystemEntryAction.Create):
                    child.Clear(recursive: true);
                    break;

                // Delete after Rename or Change -> ok
                case (FileSystemEntryAction.Delete, FileSystemEntryAction.Rename):
                case (FileSystemEntryAction.Delete, FileSystemEntryAction.Change):
                    child.Value = new FileSystemEntryChange
                    {
                        Path = child.Value.Path,
                        Action = FileSystemEntryAction.Delete,
                        Timestamp = change.Timestamp
                    };
                    child.ClearSubtree();
                    child.MoveTo(child.Value.OldName);
                    parent.Names.Remove(child.Value.OldName);
                    break;

                // Create after Create or Rename or Change -> impossible
                // Rename or Change or Delete after Delete -> impossible
                case (FileSystemEntryAction.Create, _):
                case (_, FileSystemEntryAction.Delete):
                    throw new InvalidActionSequenceException(child.Value.Action, change.Action);
            }
        }
    }

    public void Clear() => _root.Clear(recursive: true);

    public bool Contains(FileSystemEntryChange change) => TryGetValue(change.Path, out var found) && found == change;

    public void CopyTo(FileSystemEntryChange[] array, int arrayIndex) => Values.ToArray().CopyTo(array, arrayIndex);

    public IEnumerator<FileSystemEntryChange> GetEnumerator() => Values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Remove(FileSystemEntryChange change)
    {
        if (!TryGetNode(change.Path, out var node) || node.Value != change)
            return false;

        node.Clear();
        return true;
    }

    public bool TryGetNode(string path, out FileSystemTrieNode node)
    {
        node = _root;
        var parts = Path.TrimEndingDirectorySeparator(path).Split(Path.DirectorySeparatorChar);
        var level = parts.Length - 1;
        if (level >= _levels.Count || _levels[level].Count == 0)
            return false;

        foreach (var part in parts)
        {
            if (!node.Names.TryGetValue(part, out var next) &&
                !node.Children.TryGetValue(part, out next))
                return false;

            node = next;
        }

        return true;
    }

    public IEnumerable<FileSystemTrieNode> TryPopLevel(int level)
    {
        if (level < _levels.Count)
            yield break;

        var listNode = _levels[level].First;
        while (listNode != null)
        {
            var trieNode = listNode.Value;
            yield return trieNode;
            listNode = listNode.Next;
            trieNode.Clear();
        }
    }

    public bool TryGetValue(string path, [MaybeNullWhen(false)] out FileSystemEntryChange change)
    {
        if (!TryGetNode(path, out var node))
        {
            change = null;
            return false;
        }

        change = node.Value;
        return change != null;
    }
}

internal class FileSystemTrieNode
{
    private readonly FileSystemTrieNode? _parent;
    public FileSystemTrieNode? Parent => _parent;

    private LinkedListNode<FileSystemTrieNode>? _container;
    public LinkedListNode<FileSystemTrieNode>? Container => _container;

    private FileSystemEntryChange? _value;
    public FileSystemEntryChange? Value
    {
        get => _value;
        set
        {
            if (_container is null)
                throw new InvalidOperationException("Cannot set value without a container. Use Initialize method to set the container.");

            if (value is null)
                throw new InvalidOperationException("Cannot set null value. Use Clear method to untrack the node.");

            SetPriority(_value = value);
        }
    }

    private FileSystemEntryChange? _priorityValue;
    public FileSystemEntryChange? PriorityValue
    {
        get => _priorityValue;
        private set => _priorityValue = value;
    }

    private readonly Dictionary<string, FileSystemTrieNode> _children = new();
    public Dictionary<string, FileSystemTrieNode> Children => _children;

    private readonly Dictionary<string, FileSystemTrieNode> _names = new();
    public Dictionary<string, FileSystemTrieNode> Names => _names;

    public FileSystemTrieNode() { }

    public FileSystemTrieNode(FileSystemTrieNode parent)
    {
        _parent = parent;
    }

    public FileSystemTrieNode(FileSystemTrieNode parent, LinkedList<FileSystemTrieNode> level, FileSystemEntryChange value)
    {
        _parent = parent;
        Initialize(level, value);
    }

    public void Initialize(LinkedList<FileSystemTrieNode> level, FileSystemEntryChange value)
    {
        _container = level.AddLast(this);
        Value = value;
    }

    private void SetPriority(FileSystemEntryChange value)
    {
        var curr = this;
        while (curr != null)
        {
            if (curr.PriorityValue != null && value.Timestamp <= curr.PriorityValue.Timestamp)
                break;

            curr.PriorityValue = value;
            curr = curr.Parent;
        }
    }

    private FileSystemEntryChange? GetChildrenPriority() => _children.Count > 0 ? 
        _children.MaxBy(kvp => kvp.Value.PriorityValue!.Timestamp).Value.PriorityValue! : null;

    public void Clear(bool recursive = false)
    {
        if (recursive)
            ClearSubtree();

        _priorityValue = null;
        if (_children.Count == 0)
            TrimDanglingPath();
        else
            SetPriority(GetChildrenPriority()!);

        _container?.List?.Remove(_container);
        _container = null;
        _value = null;
    }

    public void ClearSubtree()
    {
        foreach (var (_, child) in _children)
        {
            child.Clear();
            child.ClearSubtree();
        }

        _children.Clear();
        _names.Clear();
    }

    private void TrimDanglingPath()
    {
        if (_value == null)
            throw new InvalidOperationException("Cannot trim an empty node. Use the corresponding trie instead.");

        if (_parent!.Names.Remove(_value.OldName))
        {
            _parent.Children.Remove(_value.Properties.RenameProps!.Name);
        }
        else
        {
            _parent.Children.Remove(_value.OldName);
        }

        var curr = _parent!;
        var parts = Path.TrimEndingDirectorySeparator(_value.Path).Split(Path.DirectorySeparatorChar);
        for (int i = parts.Length - 2; i >= 0; i--)
        {
            if (curr.Value != null || curr.Children.Count > 0 || curr.Parent == null)
                break;

            curr.PriorityValue = null;
            curr.Parent.Children.Remove(parts[i]);
            curr = curr.Parent;
        }

        if (curr.PriorityValue != _priorityValue)
            return;

        curr.PriorityValue = null;
        var priority = (curr.Value, curr.GetChildrenPriority()) switch
        {
            (null, null) => null,
            (null, var children) => children,
            (var parent, null) => parent,
            (var parent, var children) => parent.Timestamp >= children.Timestamp ? parent : children
        };
        if (priority is not null)
            SetPriority(priority);
    }

    public void MoveTo(string name)
    {
        if (_value == null)
            throw new InvalidOperationException("Cannot move an empty node. Use the corresponding trie instead.");

        if (!_parent!.Children.Remove(_value.Properties.RenameProps?.Name ?? _value.OldName))
            throw new KeyNotFoundException(_value.Path);

        _parent.Children.Add(name, this);
    }
}
