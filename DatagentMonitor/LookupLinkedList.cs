using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace DatagentMonitor;

// Linked list with a lookup, i. e. with O(1) time for Contains/Remove
public class LookupLinkedList<TKey, TValue> : ICollection<TValue>, IEnumerable<TValue> where TKey : notnull
{
    private readonly LinkedList<TValue> _list = new();
    private readonly Dictionary<TKey, LinkedListNode<TValue>> _lookup = new();
    private readonly Func<TValue, TKey> _keySelector;

    public LookupLinkedList(Func<TValue, TKey> keySelector)
    {
        _keySelector = keySelector;
    }

    public TValue this[TKey key] => _lookup[key].Value;

    public int Count => _lookup.Count;

    public bool IsReadOnly => false;

    public void Add(TValue value)
    {
        var node = new LinkedListNode<TValue>(value);
        _lookup.Add(_keySelector(value), node);
        _list.AddLast(node);
    }

    public void Clear()
    {
        _lookup.Clear();
        _list.Clear();
    }

    public bool Contains(TValue value)
    {
        if (!_lookup.TryGetValue(_keySelector(value), out var node))
            return false;

        return EqualityComparer<TValue>.Default.Equals(node.Value, value);
    }

    public void CopyTo(TValue[] array, int arrayIndex) => _list.CopyTo(array, arrayIndex);

    public IEnumerator<TValue> GetEnumerator() => _list.GetEnumerator();

    IEnumerator<TValue> IEnumerable<TValue>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Remove(TValue value)
    {
        if (_lookup.Remove(_keySelector(value), out var node))
        {
            _list.Remove(node);
            return true;
        }

        return false;
    }

    public bool Remove(TKey key)
    {
        if (_lookup.Remove(key, out var node))
        {
            _list.Remove(node);
            return true;
        }

        return false;
    }

    public bool Remove(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        if (_lookup.Remove(key, out var node))
        {
            value = node.Value;
            _list.Remove(node);
            return true;
        }

        value = default;
        return false;
    }

    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        if (_lookup.TryGetValue(key, out var node))
        {
            value = node.Value;
            return true;
        }

        value = default;
        return false;
    }
}
