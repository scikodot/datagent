using DatagentMonitor.FileSystem;
using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace DatagentMonitor;

internal class FileSystemTrie : ICollection<FileSystemEntryChange>
{
    private readonly FileSystemTrieNode _root = new();
    public FileSystemTrieNode Root => _root;

    private readonly List<LinkedList<FileSystemTrieNode>> _levels = new();
    public List<LinkedList<FileSystemTrieNode>> Levels => _levels;

    public int Count => _levels.Sum(x => x.Count);

    public bool IsReadOnly => false;

    public IEnumerable<FileSystemEntryChange> Values => _levels.SelectMany(l => l.Select(n => n.Value!));

    public void Add(FileSystemEntryChange change) => Add(change, stack: true);

    // TODO: trim dangling (empty) paths
    // TODO: remove stack arg (?)
    public void Add(FileSystemEntryChange change, bool stack)
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
            child = new FileSystemTrieNode(parent, change);
            child.Container = _levels[level].AddLast(child);
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
                    child.Container = _levels[level].AddLast(child);
                    child.Value = change;

                    // Re-attach the node to the parent with the new name
                    parent.Children.Remove(parts[^1]);
                    parent.Children.Add(change.Properties.RenameProps!.Name, child);
                    parent.Names.Add(change.OldName, child);
                    break;

                case FileSystemEntryAction.Delete:
                    child.Container = _levels[level].AddLast(child);
                    child.Value = change;
                    child.ClearSubtree();
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
                                child.Clear();
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
                            parent.Names.TryAdd(child.Value.OldName, child);
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
                            parent.Children.Remove(parts[^1]);
                            child.Clear(recursive: true);
                            break;

                        // Delete after Rename or Change -> ok
                        case FileSystemEntryAction.Rename:
                        case FileSystemEntryAction.Change:
                            child.Value.Action = FileSystemEntryAction.Delete;
                            child.Value.Properties.RenameProps = null;
                            child.Value.Properties.ChangeProps = null;
                            child.ClearSubtree();

                            parent.Children.Remove(parts[^1]);
                            parent.Children.Add(child.Value.OldName, child);
                            parent.Names.Remove(child.Value.OldName);
                            break;

                        // Delete again -> impossible
                        case FileSystemEntryAction.Delete:
                            throw new InvalidActionSequenceException(actionOld, actionNew);
                    }
                    break;
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
    private LinkedListNode<FileSystemTrieNode>? _container;
    public LinkedListNode<FileSystemTrieNode>? Container
    {
        get => _container;
        set => _container = value;
    }

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

    private readonly Dictionary<string, FileSystemTrieNode> _names = new();
    public Dictionary<string, FileSystemTrieNode> Names => _names;

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

    public FileSystemEntryChange GetPriority()
    {
        throw new NotImplementedException();
    }

    public void Clear(bool recursive = false)
    {
        _container?.List?.Remove(_container);
        _container = null;
        _value = null;

        if (recursive)
            ClearSubtree();
    }

    public void ClearSubtree()
    {
        foreach (var (_, child) in _children)
            child.Clear(recursive: true);

        _children.Clear();
        _names.Clear();
    }

    public void MoveTo(string name)
    {
        if (_value == null)
            throw new ArgumentException("Cannot move an empty node. Use the corresponding trie instead.");

        if (!_parent!.Children.Remove(_value.OldName))
            throw new KeyNotFoundException(_value.Path);

        _parent.Children.Add(name, this);
    }
}
