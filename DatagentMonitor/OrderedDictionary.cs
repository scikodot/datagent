using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace DatagentMonitor;

public class OrderedDictionary<TKey, TValue> : IDictionary<TKey, TValue> where TKey : notnull
{
    private readonly LinkedList<KeyValuePair<TKey, TValue>> _list = new();
    private readonly Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>> _map = new();
    private ValueCollection? _values;

    public TValue this[TKey key]
    {
        get => _map[key].Value.Value;
        set
        {
            var item = new KeyValuePair<TKey, TValue>(key, value);
            if (_map.ContainsKey(key))
                _map[key].Value = item;
            else
                Add(item);
        }
    }

    public int Count => _map.Count;

    public bool IsReadOnly => false;

    public ICollection<TKey> Keys => _map.Keys;

    public ICollection<TValue> Values => _values ??= new ValueCollection(_list);

    public void Add(KeyValuePair<TKey, TValue> item)
    {
        var node = new LinkedListNode<KeyValuePair<TKey, TValue>>(item);
        _map.Add(item.Key, node);
        _list.AddLast(node);
    }

    public void Add(TKey key, TValue value) => Add(new KeyValuePair<TKey, TValue>(key, value));

    public void Clear()
    {
        _map.Clear();
        _list.Clear();
    }

    public bool Contains(KeyValuePair<TKey, TValue> item)
    {
        if (!_map.TryGetValue(item.Key, out var node))
            return false;

        return EqualityComparer<TValue>.Default.Equals(node.Value.Value, item.Value);
    }

    public bool ContainsKey(TKey key) => _map.ContainsKey(key);

    public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex) => _list.CopyTo(array, arrayIndex);

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => _list.GetEnumerator();

    IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Remove(TKey key)
    {
        if (_map.Remove(key, out var node))
        {
            _list.Remove(node);
            return true;
        }
            
        return false;
    }

    public bool Remove(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        if (_map.Remove(key, out var node))
        {
            value = node.Value.Value;
            _list.Remove(node);
            return true;
        }

        value = default;
        return false;
    }

    public bool Remove(KeyValuePair<TKey, TValue> item)
    {
        if (!_map.TryGetValue(item.Key, out var node))
            return false;

        if (EqualityComparer<TValue>.Default.Equals(node.Value.Value, item.Value))
        {
            _map.Remove(item.Key);
            _list.Remove(node);
            return true;
        }

        return false;
    }

    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        if (_map.TryGetValue(key, out var node))
        {
            value = node.Value.Value;
            return true;
        }

        value = default;
        return false;
    }

    [DebuggerDisplay("Count = {Count}")]
    public sealed class ValueCollection : ICollection<TValue>, IReadOnlyCollection<TValue>
    {
        private readonly LinkedList<KeyValuePair<TKey, TValue>> _list;

        public ValueCollection(LinkedList<KeyValuePair<TKey, TValue>> list)
        {
            _list = list ?? throw new ArgumentNullException(nameof(list));
        }

        public int Count => _list.Count;

        bool ICollection<TValue>.IsReadOnly => true;

        void ICollection<TValue>.Add(TValue item) => throw new NotSupportedException();

        void ICollection<TValue>.Clear() => throw new NotSupportedException();

        bool ICollection<TValue>.Contains(TValue item) => _list.Select(kvp => kvp.Value).Contains(item);

        public void CopyTo(TValue[] array, int index)
        {
            if (array == null)
                throw new ArgumentNullException(nameof(array));

            if ((uint)index > array.Length)
                throw new ArgumentOutOfRangeException(nameof(index));

            if (array.Length - index < _list.Count)
                throw new ArgumentException("Array segment too small.");

            var current = _list.First;
            while (current != null)
            {
                array[index++] = current.Value.Value;
                current = current.Next;
            }
        }

        public IEnumerator<TValue> GetEnumerator() => _list.Select(kvp => kvp.Value).GetEnumerator();

        IEnumerator<TValue> IEnumerable<TValue>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        bool ICollection<TValue>.Remove(TValue item) => throw new NotSupportedException();
    }
}
