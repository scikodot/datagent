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
                    child.Initialize(_levels[level], change);
                    
                    // Re-attach the node to the parent with the new name
                    parent.Children.Remove(parts[^1]);
                    parent.Children.Add(change.Properties.RenameProps!.Name, child);
                    parent.Names.Add(change.OldName, child);
                    break;

                case FileSystemEntryAction.Delete:
                    child.Initialize(_levels[level], change);
                    child.ClearSubtree();
                    break;
            }
        }
        else
        {
            if (!_stack)
                throw new ArgumentException($"A change for {change.Path} is already present, and stacking is disallowed.");

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
                            child.MoveTo(properties.Name);
                            break;

                        // Rename after Rename or Change -> ok, but keep the previous action
                        case FileSystemEntryAction.Rename:
                        case FileSystemEntryAction.Change:
                            child.Value.Timestamp = change.Timestamp;
                            child.Value.Properties.RenameProps = properties;
                            child.MoveTo(properties.Name);
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
                            child.Clear(recursive: true);
                            break;

                        // Delete after Rename or Change -> ok
                        case FileSystemEntryAction.Rename:
                        case FileSystemEntryAction.Change:
                            child.Value.Action = FileSystemEntryAction.Delete;
                            child.Value.Properties.RenameProps = null;
                            child.Value.Properties.ChangeProps = null;
                            child.ClearSubtree();
                            child.MoveTo(child.Value.OldName);
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
    private readonly FileSystemTrieNode? _parent;
    public FileSystemTrieNode? Parent => _parent;

    private LinkedListNode<FileSystemTrieNode>? _container;
    public LinkedListNode<FileSystemTrieNode>? Container => _container;

    private FileSystemEntryChange? _value;
    public FileSystemEntryChange? Value => _value;

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
        _value = value;
    }

    public FileSystemEntryChange GetPriority()
    {
        throw new NotImplementedException();
    }

    public void Clear(bool recursive = false)
    {
        if (recursive)
            ClearSubtree();

        // No value and empty subtree -> dangling path
        if (_children.Count == 0)
            TrimDanglingPath();

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

    public void MoveTo(string name)
    {
        if (_value == null)
            throw new InvalidOperationException("Cannot move an empty node. Use the corresponding trie instead.");

        if (!_parent!.Children.Remove(_value.Properties.RenameProps?.Name ?? _value.OldName))
            throw new KeyNotFoundException(_value.Path);

        _parent.Children.Add(name, this);
    }

    private void TrimDanglingPath()
    {
        if (_value == null)
            throw new InvalidOperationException("Cannot trim an empty node. Use the corresponding trie instead.");

        if (_value.Properties.RenameProps != null)
        {
            _parent!.Names.Remove(_value.OldName);
            _parent.Children.Remove(_value.Properties.RenameProps.Name);
        }
        else
        {
            _parent!.Children.Remove(_value.OldName);
        }

        var curr = _parent!;
        var parts = Path.TrimEndingDirectorySeparator(_value.Path).Split(Path.DirectorySeparatorChar);
        for (int i = parts.Length - 2; i >= 0; i--)
        {
            if (curr.Value != null || curr.Children.Count > 0 || curr.Parent == null)
                break;

            curr.Parent.Children.Remove(parts[i]);
            curr = curr.Parent;
        }
    }
}
