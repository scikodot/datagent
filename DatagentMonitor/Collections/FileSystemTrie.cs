using DatagentMonitor.FileSystem;
using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace DatagentMonitor.Collections;

internal partial class FileSystemTrie : ICollection<EntryChange>
{
    private readonly bool _stack;

    private readonly Node _root = new();
    public Node Root => _root;

    private int _levels;
    /* Since most of the tries are dense due to the intermediary directories having changes, 
     * and those are rarely non-conflicting, enumerating the whole trie is practically equivalent 
     * to enumerating only its non-empty nodes, which is the ultimate goal.
     */
    public IEnumerable<IEnumerable<Node>> Levels
    {
        get
        {
            var level = new List<Node> { _root } as IEnumerable<Node>;
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
                        (next as INodeExposure).Value = new EntryChange(
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
                    new Node(parent, parts[i],
                        new EntryChange(
                            change.Timestamp, string.Concat(parts[..(i + 1)]),
                            EntryType.Directory, EntryAction.Change,
                            null, new ChangeProperties
                            {
                                LastWriteTime = change.Timestamp.Value
                            })) : 
                    new Node(parent, parts[i]);
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
        // 
        // TODO: another example is deleting an entry after its parent folder has got deleted; 
        // that must not happen, because all contents have to be deleted prior to deleting the containing folder.
        if (!parent.Names.TryGetValue(parts[^1], out var node))
        {
            node = new Node(parent, parts[^1], change);
            if (change.RenameProperties is not null)
                (node as INodeExposure).Name = change.RenameProperties!.Value.Name;
        }
        else
        {
            if (!_stack)
                throw new ArgumentException($"A change for {change.OldPath} is already present, and stacking is disallowed.");

            if (change.Type != node.Type && change.Action is not EntryAction.Create)
                throw new ArgumentException($"Got a change for an existing entry but of a different type: {change}");

            var value = node.Value;
            var nodeExposed = node as INodeExposure;
            switch (change.Action, value.Action)
            {
                // Create after Delete -> 2 options:
                // 1. The same entry has got restored
                // 2. Another entry has been created with the same name
                case (EntryAction.Create, EntryAction.Delete):
                    switch (change.Type, value.Type)
                    {
                        // Treat the old entry as Changed
                        // TODO: consider comparing files' contents to determine if it's really changed
                        case (EntryType.Directory, EntryType.Directory):
                        case (EntryType.File, EntryType.File):
                            nodeExposed.Value = new EntryChange(
                                change.Timestamp, value.Path, 
                                value.Type, EntryAction.Change, 
                                value.RenameProperties, change.ChangeProperties);
                            break;

                        // Since changing an existing node's type is prohibited, 
                        // the only way to replace it is to remove it (with its subtree) 
                        // and try to add the change again (it must not meet any conflicts now)
                        case (EntryType.Directory, EntryType.File):
                        case (EntryType.File, EntryType.Directory):
                            node.Clear(recursive: true);
                            Add(change);
                            break;
                    }
                    break;

                // Rename after Create -> ok, but keep the previous action
                // and set the old name to the new one
                case (EntryAction.Rename, EntryAction.Create):
                    nodeExposed.OldName = change.RenameProperties!.Value.Name;
                    nodeExposed.Value = value with
                    {
                        Timestamp = change.Timestamp
                    };
                    break;

                // Rename after Rename -> ok; remove the change if reverted to the old name
                case (EntryAction.Rename, EntryAction.Rename):
                    // TODO: if a rename gets reverted and (!) does not get cleared manually afterwards,
                    // the getter output becomes broken; see if that can be fixed
                    nodeExposed.Name = change.RenameProperties!.Value.Name;
                    if (node.Name == node.OldName)
                    {
                        node.Clear();
                    }
                    else
                    {
                        nodeExposed.Value = value with
                        {
                            Timestamp = change.Timestamp
                        };
                    }
                    break;

                // Rename after Change -> ok, but keep the previous action
                case (EntryAction.Rename, EntryAction.Change):
                    nodeExposed.Name = change.RenameProperties!.Value.Name;
                    nodeExposed.Value = value with
                    {
                        Timestamp = change.Timestamp
                    };
                    break;

                // Change after Create -> ok, but keep the previous action
                case (EntryAction.Change, EntryAction.Create):
                    nodeExposed.Value = new EntryChange(
                        change.Timestamp, value.Path, 
                        value.Type, value.Action,
                        value.RenameProperties, change.ChangeProperties);
                    break;

                // Change after Rename or Change -> ok
                case (EntryAction.Change, EntryAction.Rename):
                case (EntryAction.Change, EntryAction.Change):
                    nodeExposed.Value = new EntryChange(
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
                    nodeExposed.Name = node.OldName;
                    nodeExposed.Value = new EntryChange(
                        change.Timestamp, value.Path, 
                        value.Type, change.Action, 
                        value.RenameProperties, change.ChangeProperties);
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

    public bool TryGetNode(string path, out Node node)
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
