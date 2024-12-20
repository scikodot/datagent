using DatagentMonitor.FileSystem;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace DatagentMonitor.Collections;

internal partial class FileSystemCommandTrie
{
    private interface INodeExposure
    {
        string SourceName { set; }
        string TargetName { set; }
        EntryCommand Value { set; }
    }

    public class Node : INodeExposure
    {
        private string? _sourceName;
        public string? SourceName
        {
            get => _sourceName;
            private set
            {
                if (_parent is null)
                    throw new InvalidOperationException("Cannot set source name for the root or a detached node.");

                if (value is null)
                    throw new ArgumentNullException(nameof(value));

                if (value == _sourceName)
                    return;

                if (!_parent._nodesBySourceNames.Remove(_sourceName!))
                    throw new KeyNotFoundException(_sourceName);

                _parent._nodesBySourceNames.Add(value, this);

                _sourceName = value;
            }
        }

        string INodeExposure.SourceName { set => SourceName = value; }

        private string? _targetName;
        public string? TargetName
        {
            get => _targetName;
            private set
            {
                if (_parent is null)
                    throw new InvalidOperationException("Cannot set target name for the root or a detached node.");

                if (value is null)
                    throw new ArgumentNullException(nameof(value));

                if (value == _targetName)
                    return;

                if (!_parent._nodesByTargetNames.Remove(_targetName!))
                    throw new KeyNotFoundException(_targetName);

                _targetName = value;
            }
        }

        string INodeExposure.TargetName { set => TargetName = value; }

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
                    if (!_parent._nodesBySourceNames.Remove(_sourceName!))
                        throw new KeyNotFoundException(_sourceName);

                    if (!_parent._nodesByTargetNames.Remove(_targetName!))
                        throw new KeyNotFoundException(_targetName);
                }

                if (value is not null)
                {
                    if (_sourceName is null || _targetName is null)
                        throw new InvalidOperationException("Cannot add a child without both source and target names initialized.");

                    value._nodesBySourceNames.Add(_sourceName, this);
                }

                _parent = value;
            }
        }

        private EntryCommand? _value;
        [DisallowNull]
        public EntryCommand? Value
        {
            get => _value is null ? null : new EntryCommand(
                TargetPath, _value.Action,
                SourceName == TargetName ? null : new RenameProperties(SourceName!));
            private set
            {
                if (value is null)
                    throw new ArgumentNullException(nameof(value),
                        "Setting null value for a node directly is disallowed.");

                if (_parent is null)
                    throw new InvalidOperationException("Cannot set value for the root node.");

                _value = value;
            }
        }

        EntryCommand INodeExposure.Value { set => Value = value; }

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

        private readonly Dictionary<string, Node> _nodesBySourceNames = new();
        public IReadOnlyDictionary<string, Node> NodesBySourceNames => _nodesBySourceNames;

        private readonly Dictionary<string, Node> _nodesByTargetNames = new();
        public IReadOnlyDictionary<string, Node> NodesByTargetNames => _nodesByTargetNames;

        public string SourcePath => ConstructPath(SelectAlongBranch(n => n.SourceName!));
        public string TargetPath => ConstructPath(SelectAlongBranch(n => n.TargetName!));

        public Node() { }

        private Node(Node parent, string sourceName, string targetName)
        {
            SourceName = sourceName;
            TargetName = targetName;
            Parent = parent;
        }

        public Node(Node parent, string targetName) : this(parent, targetName, targetName)
        {

        }

        public Node(Node parent, string targetName, EntryCommand value) : this(parent, value.RenameProperties?.Name ?? targetName, targetName)
        {
            Value = value;
            Parent!.Count += 1;
        }

        private static string ConstructPath(IEnumerable<string> names) =>
            new StringBuilder().AppendJoin(Path.DirectorySeparatorChar, names.Reverse()).ToString();

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
                SourceName = _targetName;
        }

        public void ClearSubtree()
        {
            ClearSubtreeInternal();
            Count = 0;
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
                    var subnodes = new List<Node>(node.NodesByTargetNames.Values);
                    foreach (var subnode in subnodes)
                    {
                        subnode.Parent = null;
                        subnode.Clear();
                    }
                }
            }
        }

        //public bool TryGetNode(string name, [MaybeNullWhen(false)] out Node node) =>
        //    NodesByOldNames.TryGetValue(name, out node) || NodesByNames.TryGetValue(name, out node);
    }
}
