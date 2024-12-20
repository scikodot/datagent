using DatagentMonitor.FileSystem;
using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace DatagentMonitor.Collections;

internal partial class FileSystemCommandTrie : ICollection<EntryCommand>
{
    private readonly Node _root = new();
    public Node Root => _root;

    private int _levels;
    // TODO: optimize, as not all nodes have non-null values
    public IEnumerable<IEnumerable<Node>> Levels
    {
        get
        {
            var level = new List<Node> { _root } as IEnumerable<Node>;
            for (int i = 0; i < _levels; i++)
            {
                level = level.SelectMany(n => n.NodesByTargetNames.Values);
                yield return level.Where(n => n.Value is not null);
            }
        }
    }

    public int Count => _root.Count;

    public bool IsReadOnly => false;

    public IEnumerable<EntryCommand> Values => Levels.SelectMany(l => l.Select(n => n.Value!));

    public FileSystemCommandTrie()
    {
        
    }

    public FileSystemCommandTrie(IEnumerable<EntryCommand> changes)
    {
        AddRange(changes);
    }

    public void Add(EntryCommand command)
    {
        var parent = _root;
        var parts = command.Path.Split(Path.DirectorySeparatorChar);
        var level = parts.Length;
        for (int i = 0; i < level - 1; i++)
        {
            if (!parent.NodesByTargetNames.TryGetValue(parts[i], out var next))
                next = new Node(parent, parts[i]);

            parent = next;
        }

        _levels = Math.Max(_levels, level);

        if (!parent.NodesByTargetNames.TryGetValue(parts[^1], out _))
        {
            _ = new Node(parent, parts[^1], command);
        }
        else
        {
            // TODO: implement stacking
            throw new NotImplementedException();
        }
    }

    public void AddRange(IEnumerable<EntryCommand> changes)
    {
        foreach (var change in changes)
            Add(change);
    }

    public async Task AddRange(IAsyncEnumerable<EntryCommand> changes)
    {
        await foreach (var change in changes)
            Add(change);
    }

    public void Clear()
    {
        _root.Clear(recursive: true);
    }

    public bool Contains(EntryCommand command) => TryGetValue(command.Path, out var found) && found == command;

    public void CopyTo(EntryCommand[] array, int arrayIndex) => Values.ToArray().CopyTo(array, arrayIndex);

    public IEnumerator<EntryCommand> GetEnumerator() => Values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Remove(EntryCommand command)
    {
        if (!TryGetNode(command.Path, out var node) || node.Value != command)
            return false;

        node.Clear();
        return true;
    }

    public bool TryGetNode(string path, out Node node)
    {
        node = _root;
        var parts = path.Split(Path.DirectorySeparatorChar);

        // Search by target path
        var foundByTargetPath = true;
        foreach (var part in parts)
        {
            if (!node.NodesByTargetNames.TryGetValue(part, out var next))
            {
                foundByTargetPath = false;
                break;
            }

            node = next;
        }

        // If not found, search by source path
        if (!foundByTargetPath)
        {
            foreach (var part in parts)
            {
                if (!node.NodesBySourceNames.TryGetValue(part, out var next))
                    return false;

                node = next;
            }
        }

        return true;
    }

    public bool TryGetValue(string path, [MaybeNullWhen(false)] out EntryCommand command)
    {
        if (!TryGetNode(path, out var node))
        {
            command = null;
            return false;
        }

        command = node.Value;
        return command != null;
    }
}
