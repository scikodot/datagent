using DatagentMonitor.FileSystem;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace DatagentMonitor.Collections;

internal partial class FileSystemTrie
{
    private interface INodeExposure
    {
        string OldName { set; }
        string Name { set; }
        EntryChange Value { set; }
    }

    public class Node : INodeExposure
    {
        private string? _oldName;
        public string? OldName
        {
            get => _oldName;
            // Let triplet (_oldName, _name, value) ~ (x, y, z)
            private set
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
                    if (!_parent._nodesByNames.Remove(_name!))
                        throw new KeyNotFoundException(_name);

                    _parent._nodesByNames.Add(_name = value, this);
                }
                // (x, y, y)
                else if (_name == value)
                {
                    if (!_parent!._nodesByOldNames.Remove(_oldName!))
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

        string INodeExposure.OldName { set => OldName = value; }

        private string? _name;
        public string? Name
        {
            get => _name;
            // Let triplet (_oldName, _name, value) ~ (x, y, z)
            private set
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
                        _parent._nodesByOldNames.Add(_oldName, this);

                    // (x, y, x)
                    if (_oldName == value)
                        _parent._nodesByOldNames.Remove(_oldName);

                    // Both previous and (x, y, z)
                    if (!_parent._nodesByNames.Remove(_name!))
                        throw new KeyNotFoundException(_name);

                    _parent._nodesByNames.Add(value, this);
                }

                _name = value;
            }
        }

        string INodeExposure.Name { set => Name = value; }

        private EntryType _type = EntryType.Directory;
        public EntryType Type
        {
            get => _type;
            private set
            {
                if (_value is not null && _type != value ||
                    _value is null && _nodesByNames.Count > 0 && value is EntryType.File)
                    throw new InvalidOperationException("Cannot change type of an existing node.");

                _type = value;
            }
        }

        private Node? _parent;
        public Node? Parent
        {
            get => _parent;
            private set
            {
                if (value == _parent)
                    return;

                if (_parent is not null)
                {
                    if (!_parent._nodesByNames.Remove(_name!))
                        throw new KeyNotFoundException(_name);

                    if (_oldName != _name && !_parent._nodesByOldNames.Remove(_oldName!))
                        throw new KeyNotFoundException(_oldName);
                }

                if (value is not null)
                {
                    if (_name is null)
                        throw new InvalidOperationException("Cannot add a child without a name.");

                    if (value.Type is EntryType.File)
                        throw new InvalidOperationException("Cannot add a child to a file node.");

                    value._nodesByNames.Add(_name, this);
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
            private set
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

        EntryChange INodeExposure.Value { set => Value = value; }

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
                    while (curr.Value is null && curr.NodesByNames.Count == 0 && prev is not null)
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
                            var p2 = curr.NodesByNames.Values.Max(v => v.PriorityValue);
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

        private readonly Dictionary<string, Node> _nodesByOldNames = new();
        public IReadOnlyDictionary<string, Node> NodesByOldNames => _nodesByOldNames;

        private readonly Dictionary<string, Node> _nodesByNames = new();
        public IReadOnlyDictionary<string, Node> NodesByNames => _nodesByNames;

        public string OldPath => ConstructPath(SelectAlongBranch(n => n == this ? n.OldName! : n.Name!));
        public string Path => ConstructPath(SelectAlongBranch(n => n.Name!));

        public Node() { }

        public Node(Node parent, string name)
        {
            Name = name;
            Parent = parent;
        }

        public Node(Node parent, string name, EntryChange value) : this(parent, name)
        {
            Value = value;
            Parent!.Count += 1;
        }

        private static string ConstructPath(IEnumerable<string> names) =>
            new StringBuilder().AppendJoin(System.IO.Path.DirectorySeparatorChar, names.Reverse()).ToString();

        private IEnumerable<T> SelectAlongBranch<T>(Func<Node, T> selector)
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

            PriorityValue = _nodesByNames.Values.Max(v => v.PriorityValue);
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
            var queue = new Queue<Node>();
            queue.Enqueue(this);
            while (queue.Count > 0)
            {
                int count = queue.Count;
                for (int i = 0; i < count; i++)
                {
                    var node = queue.Dequeue();
                    var subnodes = new List<Node>(node.NodesByNames.Values);
                    foreach (var subnode in subnodes)
                    {
                        subnode.Parent = null;
                        subnode.Clear();
                    }
                }
            }
        }

        public bool TryGetNode(string name, [MaybeNullWhen(false)] out Node node) =>
            NodesByOldNames.TryGetValue(name, out node) || NodesByNames.TryGetValue(name, out node);
    }
}
