using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace DatagentMonitor;

public class OrderedDictionary<TKey, TValue> : IDictionary<TKey, TValue> where TKey : notnull
{
    private readonly LinkedList<KeyValuePair<TKey, TValue>> _values = new();
    private readonly Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>> _map = new();

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

    public ICollection<TKey> Keys => _map.Keys;

    // TODO: ?
    public ICollection<TValue> Values => (ICollection<TValue>)_map.Values.Select(x => x.Value.Value);

    public int Count => _map.Count;

    public bool IsReadOnly => false;

    public void Add(TKey key, TValue value) => Add(new KeyValuePair<TKey, TValue>(key, value));

    public void Add(KeyValuePair<TKey, TValue> item)
    {
        var node = new LinkedListNode<KeyValuePair<TKey, TValue>>(item);
        _map.Add(item.Key, node);
        _values.AddLast(node);
    }

    public void Clear()
    {
        _map.Clear();
        _values.Clear();
    }

    public bool Contains(KeyValuePair<TKey, TValue> item)
    {
        if (!_map.TryGetValue(item.Key, out var node))
            return false;

        return EqualityComparer<TValue>.Default.Equals(node.Value.Value, item.Value);
    }

    public bool ContainsKey(TKey key) => _map.ContainsKey(key);

    public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex) => _values.CopyTo(array, arrayIndex);

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => _values.GetEnumerator();

    public bool Remove(TKey key)
    {
        if (_map.Remove(key, out var node))
        {
            _values.Remove(node);
            return true;
        }
            
        return false;
    }

    public bool Remove(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        if (_map.Remove(key, out var node))
        {
            value = node.Value.Value;
            _values.Remove(node);
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
            _values.Remove(node);
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

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
