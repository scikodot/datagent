﻿using DatagentMonitor.FileSystem;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace DatagentMonitor.Collections;

internal class FileSystemTrie : ICollection<EntryChange>
{
    private readonly bool _stack;

    private readonly FileSystemTrieNode _root = new();
    public FileSystemTrieNode Root => _root;

    private int _levels;
    /* Since most of the tries are dense due to the intermediary directories having changes, 
     * and those are rarely non-conflicting, enumerating the whole trie is practically equivalent 
     * to enumerating only its non-empty nodes, which is the ultimate goal.
     */
    public IEnumerable<IEnumerable<FileSystemTrieNode>> Levels
    {
        get
        {
            var level = new List<FileSystemTrieNode> { _root } as IEnumerable<FileSystemTrieNode>;
            for (int i = 0; i < _levels; i++)  
            {
                level = level.SelectMany(n => n.Names.Values);
                yield return level.Where(n => n.Value is not null);
            }
        }
    }

    public int Count => _root.Count;

    public bool IsReadOnly => false;

    public IEnumerable<EntryChange> Values => Levels.SelectMany(l => l.Select(n => n.Value!));

    public FileSystemTrie(bool stack = true)
    {
        _stack = stack;
    }

    public FileSystemTrie(IEnumerable<EntryChange> changes, bool stack = true) : this(stack)
    {
        AddRange(changes);
    }

    public void Add(EntryChange change)
    {
        var parent = _root;
        var parts = change.OldPath.Split(Path.DirectorySeparatorChar);
        var level = parts.Length;
        for (int i = 0; i < level - 1; i++)
        {
            if (parent.Names.TryGetValue(parts[i], out var next))
            {
                if (_stack)
                {
                    var value = next.Value;
                    switch (value.Action, change.Action)
                    {
                        case (EntryAction.Create, not EntryAction.Create):
                        case (EntryAction.Delete, _):
                            throw new InvalidActionSequenceException(value.Action, change.Action);
                    }

                    // TODO: if a directory is created by Ctrl+X from another place, 
                    // the LastWriteTime's of it and its subtree should not update; fix it
                    if (value.Action is EntryAction.Rename || change.Timestamp > value.Timestamp)
                    {
                        next.Value = new EntryChange(
                            change.Timestamp, value.OldPath,
                            value.Type, value.Action is EntryAction.Rename ? EntryAction.Change : value.Action,
                            value.RenameProperties, new ChangeProperties
                            {
                                LastWriteTime = change.Timestamp.Value
                            });
                    }
                }
            }
            else
            {
                next = _stack ? 
                    new FileSystemTrieNode(parent, parts[i],
                        new EntryChange(
                            change.Timestamp, string.Concat(parts[..(i + 1)]),
                            EntryType.Directory, EntryAction.Change,
                            null, new ChangeProperties
                            {
                                LastWriteTime = change.Timestamp.Value
                            })) : 
                    new FileSystemTrieNode(parent, parts[i]);
            }

            parent = next;
        }

        _levels = Math.Max(_levels, level);

        // TODO: perhaps some nodes must not be added; 
        // for example, if the change is a rename to the same name;
        // determine if such changes can be generated and accepted
        //
        // TODO: another example is adding a new non-Created node to a Created directory node;
        // that must not happen, because a Created directory can only contain Created entries
        if (!parent.Names.TryGetValue(parts[^1], out var node))
        {
            node = new FileSystemTrieNode(parent, parts[^1], change);
            if (change.RenameProperties is not null)
                node.Name = change.RenameProperties!.Value.Name;
        }
        else
        {
            if (!_stack)
                throw new ArgumentException($"A change for {change.OldPath} is already present, and stacking is disallowed.");

            if (change.Type != node.Type && change.Action is not EntryAction.Create)
                throw new ArgumentException($"Got a change for an existing entry but of a different type: {change}");

            var value = node.Value;
            switch (change.Action, value.Action)
            {
                // Create after Delete -> 2 options:
                // 1. The same entry has got restored
                // 2. Another entry has been created with the same name
                case (EntryAction.Create, EntryAction.Delete):
                    switch (change.Type, value.Type)
                    {
                        // The new directory entry is effectively the same, only its contents can differ
                        // TODO: add directory contents to database on delete!
                        // If a directory is deleted and then created with the same name
                        // but different contents, those contents changes won't be displayed in delta.
                        // TODO: add test
                        case (EntryType.Directory, EntryType.Directory):
                            node.Clear();
                            break;

                        // Since changing an existing node's type is prohibited, 
                        // the only way to replace it is to remove it (with its subtree) 
                        // and try to add the change again (it must not meet any conflicts now)
                        // TODO: add test
                        case (EntryType.Directory, EntryType.File):
                        case (EntryType.File, EntryType.Directory):
                            node.Clear(recursive: true);
                            Add(change);
                            break;

                        // Instead of checking files equality, simply treat the original file as changed
                        case (EntryType.File, EntryType.File):
                            node.Value = new EntryChange(
                                change.Timestamp, value.Path, 
                                value.Type, EntryAction.Change, 
                                value.RenameProperties, change.ChangeProperties);
                            break;
                    }
                    break;

                // Rename after Create -> ok, but keep the previous action
                // and set the old name to the new one
                case (EntryAction.Rename, EntryAction.Create):
                    node.OldName = change.RenameProperties!.Value.Name;
                    node.Value = value with
                    {
                        Timestamp = change.Timestamp
                    };
                    break;

                // Rename after Rename -> ok; remove the change if reverted to the old name
                case (EntryAction.Rename, EntryAction.Rename):
                    // TODO: if a rename gets reverted and (!) does not get cleared manually afterwards,
                    // the getter output becomes broken; see if that can be fixed
                    node.Name = change.RenameProperties!.Value.Name;
                    if (node.Name == node.OldName)
                    {
                        node.Clear();
                    }
                    else
                    {
                        node.Value = value with
                        {
                            Timestamp = change.Timestamp
                        };
                    }
                    break;

                // Rename after Change -> ok, but keep the previous action
                case (EntryAction.Rename, EntryAction.Change):
                    node.Name = change.RenameProperties!.Value.Name;
                    node.Value = value with
                    {
                        Timestamp = change.Timestamp
                    };
                    break;

                // Change after Create -> ok, but keep the previous action
                case (EntryAction.Change, EntryAction.Create):
                    node.Value = new EntryChange(
                        change.Timestamp, value.Path, 
                        value.Type, value.Action,
                        value.RenameProperties, change.ChangeProperties);
                    break;

                // Change after Rename or Change -> ok
                case (EntryAction.Change, EntryAction.Rename):
                case (EntryAction.Change, EntryAction.Change):
                    node.Value = new EntryChange(
                        change.Timestamp, value.Path, 
                        value.Type, change.Action, 
                        value.RenameProperties, change.ChangeProperties);
                    break;

                // Delete after Create -> a temporary entry, no need to track it
                case (EntryAction.Delete, EntryAction.Create):
                    node.Clear(recursive: true);
                    break;

                // Delete after Rename or Change -> ok
                case (EntryAction.Delete, EntryAction.Rename):
                case (EntryAction.Delete, EntryAction.Change):
                    node.Name = node.OldName;
                    node.Value = new EntryChange(
                        change.Timestamp, value.Path, 
                        value.Type, change.Action, 
                        value.RenameProperties, change.ChangeProperties);
                    node.ClearSubtree();
                    break;

                // Create after Create or Rename or Change -> impossible
                // Rename or Change or Delete after Delete -> impossible
                case (EntryAction.Create, _):
                case (_, EntryAction.Delete):
                    throw new InvalidActionSequenceException(node.Value.Action, change.Action);
            }
        }
    }

    public void AddRange(IEnumerable<EntryChange> changes)
    {
        foreach (var change in changes)
            Add(change);
    }

    public void Clear()
    {
        _root.Clear(recursive: true);
    }

    public bool Contains(EntryChange change) => TryGetValue(change.OldPath, out var found) && found == change;

    public void CopyTo(EntryChange[] array, int arrayIndex) => Values.ToArray().CopyTo(array, arrayIndex);

    public IEnumerator<EntryChange> GetEnumerator() => Values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Remove(EntryChange change)
    {
        if (!TryGetNode(change.OldPath, out var node) || node.Value != change)
            return false;

        node.Clear();
        return true;
    }

    public bool TryGetNode(string path, out FileSystemTrieNode node)
    {
        node = _root;
        var parts = path.Split(Path.DirectorySeparatorChar);
        foreach (var part in parts)
        {
            if (!node.OldNames.TryGetValue(part, out var next) &&
                !node.Names.TryGetValue(part, out next))
                return false;

            node = next;
        }

        return true;
    }

    public bool TryGetValue(string path, [MaybeNullWhen(false)] out EntryChange change)
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
                if (!_parent._names.Remove(_name!))
                    throw new KeyNotFoundException(_name);

                _parent._names.Add(_name = value, this);
            }
            // (x, y, y)
            else if (_name == value)
            {
                if (!_parent!._oldNames.Remove(_oldName!))
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
                    _parent._oldNames.Add(_oldName, this);

                // (x, y, x)
                if (_oldName == value)
                    _parent._oldNames.Remove(_oldName);

                // Both previous and (x, y, z)
                if (!_parent._names.Remove(_name!))
                    throw new KeyNotFoundException(_name);

                _parent._names.Add(value, this);
            }

            _name = value;
        }
    }

    private EntryType _type = EntryType.Directory;
    public EntryType Type
    {
        get => _type;
        private set
        {
            if (_value is not null && _type != value ||
                _value is null && _names.Count > 0 && value is EntryType.File)
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
                if (!_parent._names.Remove(_name!))
                    throw new KeyNotFoundException(_name);

                if (_oldName != _name && !_parent._oldNames.Remove(_oldName!))
                    throw new KeyNotFoundException(_oldName);
            }

            if (value is not null)
            {
                if (_name is null)
                    throw new InvalidOperationException("Cannot add a child without a name.");

                if (value.Type is EntryType.File)
                    throw new InvalidOperationException("Cannot add a child to a file node.");

                value._names.Add(_name, this);
            }

            _parent = value;
        }
    }

    private EntryChange? _value;
    [DisallowNull]
    public EntryChange? Value
    {
        get => _value is null ? null : new EntryChange(
            _value.Timestamp, OldPath, 
            Type, _value.Action, 
            Name == OldName ? null : new RenameProperties(Name!), _value.ChangeProperties);
        set
        {
            if (value is null)
                throw new ArgumentNullException(nameof(value), 
                    "Setting null value for a node directly is disallowed. " +
                    $"Consider using {nameof(Clear)} method instead.");

            if (_parent is null)
                throw new InvalidOperationException("Cannot set value for the root node.");

            Type = value.Type;
            PriorityValue = _value = value;
        }
    }

    private EntryChange? _priorityValue;
    public EntryChange? PriorityValue
    {
        get => _priorityValue;
        private set
        {
            if (value is null)
            {
                var prev = _parent;
                var curr = this;
                while (curr.Value is null && curr.Names.Count == 0 && prev is not null)
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
                        var p2 = curr.Names.Values.Max(v => v.PriorityValue);
                        curr.PriorityValue = p1 >= p2 ? p1 : p2;
                    }
                }
            }
            else
            {
                var curr = this;
                while (curr is not null && (curr._priorityValue is null || value > curr._priorityValue))
                {
                    curr._priorityValue = value;
                    curr = curr.Parent;
                }
            }
        }
    }
    
    // TODO: add tests (or a check to the existing tests)
    // that validate Count; it is quite prone to errors
    private int _count;
    public int Count
    {
        get => _count;
        private set
        {
            int diff = value - _count;
            if (diff == 0)
                return;

            var curr = this;
            while (curr is not null)
            {
                int count = curr._count + diff;
                if (count < 0)
                    throw new InvalidOperationException($"{nameof(Count)} cannot be less than zero.");

                curr._count = count;
                curr = curr.Parent;
            }
        }
    }

    private readonly Dictionary<string, FileSystemTrieNode> _oldNames = new();
    public IReadOnlyDictionary<string, FileSystemTrieNode> OldNames => _oldNames;

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

    public FileSystemTrieNode(FileSystemTrieNode parent, string name, EntryChange value) : this(parent, name)
    {
        Value = value;
        Parent!.Count += 1;
    }

    private static string ConstructPath(IEnumerable<string> names) =>
        new StringBuilder().AppendJoin(System.IO.Path.DirectorySeparatorChar, names.Reverse()).ToString();

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
        if (!recursive && (_parent is null || _value is null))
            return;

        if (recursive)
        {
            ClearSubtreeInternal();
            if (_parent is null)
            {
                Count = 0;
            }
            else
            {
                _parent.Count -= _count + (_value is null ? 0 : 1);
                _count = 0;
            }
        }
        else
        {
            // Here, _parent is not null && _value is not null
            _parent!.Count -= 1;
        }

        _value = null;
        if (_parent is not null)
            OldName = _name;

        PriorityValue = _names.Values.Max(v => v.PriorityValue);
    }

    public void ClearSubtree()
    {
        ClearSubtreeInternal();
        Count = 0;
        if (PriorityValue != Value)
            PriorityValue = Value;
    }

    private void ClearSubtreeInternal()
    {
        var queue = new Queue<FileSystemTrieNode>();
        queue.Enqueue(this);
        while (queue.Count > 0)
        {
            int count = queue.Count;
            for (int i = 0; i < count; i++)
            {
                var node = queue.Dequeue();
                var subnodes = new List<FileSystemTrieNode>(node.Names.Values);
                foreach (var subnode in subnodes)
                {
                    subnode.Parent = null;
                    subnode.Clear();
                }
            }
        }
    }

    public bool TryGetNode(string name, [MaybeNullWhen(false)] out FileSystemTrieNode node) =>
        OldNames.TryGetValue(name, out node) || Names.TryGetValue(name, out node);
}
