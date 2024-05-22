using DatagentMonitor.FileSystem;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Text;

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

    public FileSystemTrie(IEnumerable<FileSystemEntryChange> changes, bool stack = true) : this(stack)
    {
        foreach (var change in changes)
            Add(change);
    }

    public void Add(FileSystemEntryChange change)
    {
        var parent = _root;
        var parts = Path.TrimEndingDirectorySeparator(change.Path).Split(Path.DirectorySeparatorChar);
        var level = parts.Length - 1;
        for (int i = 0; i < level; i++)
        {
            if (!parent.Children.TryGetValue(parts[i], out var next))
                next = new FileSystemTrieNode(parent, parts[i]);

            parent = next;
        }

        while (_levels.Count <= level)
            _levels.Add(new());

        // TODO: perhaps some nodes must not be added; 
        // for example, if the change is a rename to the same name;
        // determine if such changes can be generated and accepted
        if (!parent.Children.TryGetValue(parts[^1], out var child))
        {
            child = new FileSystemTrieNode(parent, parts[^1], _levels[level], change);
            if (change.Action is FileSystemEntryAction.Rename)
                child.Name = change.Properties.RenameProps!.Name;
        }
        else if (child.Value == null)
        {
            // Empty nodes' changes are only available for directories
            if (child.Type is FileSystemEntryType.File)
                throw new InvalidOperationException($"Attempt to alter an existing node with a file change: {change.Path}");

            switch (change.Action)
            {
                // Create is only available for new nodes
                case FileSystemEntryAction.Create:
                    throw new InvalidOperationException($"Attempt to create an already existing node: {change.Path}");

                case FileSystemEntryAction.Rename:
                    child.Name = change.Properties.RenameProps!.Name;
                    break;

                case FileSystemEntryAction.Delete:
                    child.ClearSubtree();
                    break;
            }

            child.Container = _levels[level].AddLast(child);
            child.Value = change;
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
                    switch (CustomFileSystemInfo.GetEntryType(change.Path))
                    {
                        case FileSystemEntryType.Directory:
                            child.Clear();
                            break;

                        case FileSystemEntryType.File:
                            child.Value = new FileSystemEntryChange
                            {
                                Path = child.Value.Path,
                                Action = FileSystemEntryAction.Change,
                                Timestamp = change.Timestamp,
                                Properties = change.Properties
                            };
                            break;
                    }
                    break;

                // Rename after Create -> ok, but keep the previous action
                // and use the new path instead of storing the new name in RenameProps
                case (FileSystemEntryAction.Rename, FileSystemEntryAction.Create):
                    child.OldName = change.Properties.RenameProps!.Name;
                    child.Value = new FileSystemEntryChange
                    {
                        Path = CustomFileSystemInfo.ReplaceEntryName(change.Path, change.Properties.RenameProps!.Name),
                        Action = child.Value.Action,
                        Timestamp = change.Timestamp,
                        Properties = child.Value.Properties
                    };
                    break;

                // Rename after Rename -> ok; reset rename if reverted to the old name
                case (FileSystemEntryAction.Rename, FileSystemEntryAction.Rename):
                    child.Name = change.Properties.RenameProps!.Name;
                    child.Value = child.Name == child.OldName ? null : new FileSystemEntryChange
                    {
                        Path = child.Value.Path,
                        Action = child.Value.Action,
                        Timestamp = change.Timestamp,
                        Properties = change.Properties
                    };
                    break;

                // Rename after Change -> ok, but keep the previous action;
                // reset rename if reverted to the old name
                case (FileSystemEntryAction.Rename, FileSystemEntryAction.Change):
                    child.Name = change.Properties.RenameProps!.Name;
                    child.Value = new FileSystemEntryChange
                    {
                        Path = child.Value.Path,
                        Action = child.Value.Action,
                        Timestamp = change.Timestamp,
                        Properties = new FileSystemEntryChangeProperties
                        {
                            RenameProps = child.Name == child.OldName ? null : change.Properties.RenameProps!,
                            ChangeProps = child.Value.Properties.ChangeProps
                        }
                    };
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
                    child.Name = child.OldName;
                    child.Value = new FileSystemEntryChange
                    {
                        Path = child.Value.Path,
                        Action = FileSystemEntryAction.Delete,
                        Timestamp = change.Timestamp
                    };
                    child.ClearSubtree();
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

    public IEnumerable<FileSystemTrieNode> TryGetLevel(int level)
    {
        if (level >= _levels.Count)
            yield break;

        foreach (var node in _levels[level])
            yield return node;
    }

    public IEnumerable<FileSystemTrieNode> TryPopLevel(int level)
    {
        if (level >= _levels.Count)
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

    public bool TryGetNode(string path, out FileSystemTrieNode node)
    {
        node = _root;
        var parts = Path.TrimEndingDirectorySeparator(path).Split(Path.DirectorySeparatorChar);
        foreach (var part in parts)
        {
            if (!node.Names.TryGetValue(part, out var next) &&
                !node.Children.TryGetValue(part, out next))
                return false;

            node = next;
        }

        return true;
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
    private string? _oldName;
    public string? OldName
    {
        get => _oldName;
        // Let triplet (_oldName, _name, value) ~ (x, y, z)
        set
        {
            if (_parent is null)
                throw new InvalidOperationException("Cannot set old name for the root or a detached node.");

            if (value is null)
                throw new ArgumentNullException(nameof(value));

            // No-op cases:
            // (x, x, x)
            // (x, y, x)
            if (value == _oldName)
                return;

            // (x, x, y)
            if (_oldName == _name)
            {
                if (!_parent._children.Remove(_name!))
                    throw new KeyNotFoundException(_name);

                _parent._children.Add(_name = value, this);
            }
            // (x, y, y)
            else if (_name == value)
            {
                if (!_parent!._names.Remove(_oldName!))
                    throw new KeyNotFoundException(_oldName);
            }
            // (x, y, z)
            else
            {
                throw new InvalidOperationException("Cannot set old name for a renamed node.");
            }

            _oldName = value;
        }
    }

    private string? _name;
    public string? Name
    {
        get => _name;
        // Let triplet (_oldName, _name, value) ~ (x, y, z)
        set
        {
            if (value is null)
                throw new ArgumentNullException(nameof(value));

            // No-op cases:
            // (x, x, x)
            // (x, y, y)
            if (value == _name)
                return;

            // (null, null, x)
            if (_oldName is null)
            {
                _oldName = value;
            }
            // (_, _, x)
            else
            {
                if (_parent is null)
                    throw new InvalidOperationException("Cannot set name for the root or a detached node.");

                // (x, x, y)
                if (_oldName == _name)
                    _parent._names.Add(_oldName, this);

                // (x, y, x)
                if (_oldName == value)
                    _parent._names.Remove(_oldName);
                
                // Both previous and (x, y, z)
                if (!_parent._children.Remove(_name!))
                    throw new KeyNotFoundException(_name);

                _parent._children.Add(value, this);
            }

            _name = value;
        }
    }

    private FileSystemEntryType _type = FileSystemEntryType.Directory;
    public FileSystemEntryType Type
    {
        get => _type;
        private set
        {
            if ((_value is not null && _type != value) ||
                (_value is null && _children.Count > 0 && value is FileSystemEntryType.File))
                throw new InvalidOperationException("Cannot change type of an existing node.");

            _type = value;
        }
    }

    private FileSystemTrieNode? _parent;
    public FileSystemTrieNode? Parent
    {
        get => _parent;
        private set
        {
            if (value == _parent)
                return;

            if (_parent is not null)
            {
                if (!_parent._children.Remove(_name!))
                    throw new KeyNotFoundException(_name);

                if (_oldName != _name && !_parent._names.Remove(_oldName!))
                    throw new KeyNotFoundException(_oldName);
            }

            if (value is not null)
            {
                if (_name is null)
                    throw new InvalidOperationException("Cannot add a child without a name.");

                if (value.Type is FileSystemEntryType.File)
                    throw new InvalidOperationException("Cannot add a child to a file node.");

                value._children.Add(_name, this);
            }

            _parent = value;
        }
    }

    private LinkedListNode<FileSystemTrieNode>? _container;
    public LinkedListNode<FileSystemTrieNode>? Container
    {
        get => _container;
        set
        {
            _container?.List?.Remove(_container);
            _container = value;
        }
    }

    private FileSystemEntryChange? _value;
    public FileSystemEntryChange? Value
    {
        get => _value;
        set
        {
            if (value is null)
            {
                if ((value = _value) is null)
                    return;

                _value = null;
                Container = null;

                if (_parent is not null)
                    OldName = Name;

                PriorityValue = Children.Values.MaxBy(v => v.PriorityValue)?.PriorityValue;
            }
            else
            {
                if (_parent is null)
                    throw new InvalidOperationException("Cannot set value for the root node.");

                if (_container is null)
                    throw new InvalidOperationException("Cannot set value without an initialized container.");

                Type = CustomFileSystemInfo.GetEntryType(value.Path);
                PriorityValue = _value = value;
            }
        }
    }

    private FileSystemEntryChange? _priorityValue;
    public FileSystemEntryChange? PriorityValue
    {
        get => _priorityValue;
        private set
        {
            if (value is null)
            {
                var prev = _parent;
                var curr = this;
                while (curr.Value is null && curr.Children.Count == 0 && prev is not null)
                {
                    curr._priorityValue = null;
                    curr.Parent = null;
                    curr = prev;
                    prev = prev.Parent;
                }

                if (curr.PriorityValue == _priorityValue)
                {
                    if (curr.Parent is null)
                    {
                        curr._priorityValue = null;
                    }
                    else
                    {
                        var p1 = curr.Value;
                        var p2 = Children.Values.MaxBy(v => v.PriorityValue)?.PriorityValue;
                        curr.PriorityValue = p1 >= p2 ? p1 : p2;
                    }
                }
            }
            else
            {
                var curr = this;
                while (curr is not null && value > curr._priorityValue)
                {
                    curr._priorityValue = value;
                    curr = curr.Parent;
                }
            }
        }
    }

    private readonly Dictionary<string, FileSystemTrieNode> _children = new();
    public IReadOnlyDictionary<string, FileSystemTrieNode> Children => _children;

    private readonly Dictionary<string, FileSystemTrieNode> _names = new();
    public IReadOnlyDictionary<string, FileSystemTrieNode> Names => _names;

    public string OldPath => ConstructPath(SelectAlongBranch(n => n == this ? n.OldName! : n.Name!));

    public string Path => ConstructPath(SelectAlongBranch(n => n.Name!));

    public FileSystemTrieNode() { }

    public FileSystemTrieNode(FileSystemTrieNode parent, string name)
    {
        Name = name;
        Parent = parent;
    }

    public FileSystemTrieNode(FileSystemTrieNode parent, string name,
        LinkedList<FileSystemTrieNode> level, FileSystemEntryChange value) : this(parent, name)
    {
        Container = level.AddLast(this);
        Value = value;
    }

    private string ConstructPath(IEnumerable<string> names)
    {
        var builder = new StringBuilder().AppendJoin(System.IO.Path.DirectorySeparatorChar, names.Reverse());
        if (Type is FileSystemEntryType.Directory)
            builder.Append(System.IO.Path.DirectorySeparatorChar);
        return builder.ToString();
    }

    private IEnumerable<T> SelectAlongBranch<T>(Func<FileSystemTrieNode, T> selector)
    {
        if (Parent is null)
            yield break;

        var curr = this;
        while (curr.Parent != null)
        {
            yield return selector(curr);
            curr = curr.Parent;
        }
    }

    public void Clear(bool recursive = false)
    {
        if (recursive)
            ClearSubtreeInternal();

        Value = null;
    }

    public void ClearSubtree()
    {
        ClearSubtreeInternal();

        if (PriorityValue != Value)
            PriorityValue = Value;
    }

    private void ClearSubtreeInternal()
    {
        var children = new List<FileSystemTrieNode>(_children.Values);
        foreach (var child in children)
        {
            child.Parent = null;
            child.ClearSubtree();
            child.Clear();
        }
    }
}
