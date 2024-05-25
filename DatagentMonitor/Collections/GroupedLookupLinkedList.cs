using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace DatagentMonitor.Collections;

// Linked list with a lookup, i. e. with O(1) time for Contains/Remove
// 
// This implementation further enhances LookupLinkedList via grouping.
// For example, a 2-grouped linked list can be split into two parts:
// (d_1 -> ... -> d_m) -> (f_1 -> ... f_n)
// 
// Then we can store only 1 node (d_m) that is the last node of the first group;
// in a general case, k-1 nodes for each of k-1 first groups of a k-grouped collection.
// 
// Meanwhile, the lookup dict itself stays the same and encompasses all the entries, 
// say, both files and directories, but they reside in their respective groups.
// This consequently guarantees that all the names are unique throughout the whole list and across the groups.
public abstract class GroupedLookupLinkedList<TKey, TValue> : ICollection<TValue>, IEnumerable<TValue>
    where TKey : notnull
    where TValue : notnull
{
    private readonly LinkedList<TValue> _list = new();
    private readonly Dictionary<TKey, LinkedListNode<TValue>> _lookup = new();
    private readonly Dictionary<string, LinkedListNode<TValue>> _groups = new();

    public TValue this[TKey key] => _lookup[key].Value;
    public int Count => _list.Count;
    public bool IsReadOnly => false;

    public abstract TKey GetKey(TValue value);

    public void Add(TValue value)
    {
        var node = new LinkedListNode<TValue>(value);
        _lookup.Add(GetKey(value), node);

        var type = value.GetType().Name;
        if (!_groups.TryGetValue(type, out var last))
            _list.AddLast(node);
        else
            _list.AddAfter(last, node);

        _groups[type] = node;
    }

    public void Clear()
    {
        _groups.Clear();
        _lookup.Clear();
        _list.Clear();
    }

    public bool Contains(TValue value)
    {
        if (!_lookup.TryGetValue(GetKey(value), out var node))
            return false;

        return EqualityComparer<TValue>.Default.Equals(node.Value, value);
    }

    public void CopyTo(TValue[] array, int arrayIndex) => _list.CopyTo(array, arrayIndex);

    public IEnumerator<TValue> GetEnumerator() => _list.GetEnumerator();

    IEnumerator<TValue> IEnumerable<TValue>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IEnumerable<TValue> GetGroup(Type type)
    {
        if (!_groups.TryGetValue(type.Name, out var curr))
            yield break;

        while (curr?.Value.GetType().Name == type.Name)
        {
            yield return curr.Value;
            curr = curr.Previous;
        }
    }

    public bool Remove(TValue value)
    {
        if (_lookup.Remove(GetKey(value), out var node))
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
            OnRemove(node);
            return true;
        }

        return false;
    }

    public bool Remove(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        if (_lookup.Remove(key, out var node))
        {
            OnRemove(node);
            value = node.Value;
            return true;
        }

        value = default;
        return false;
    }

    private void OnRemove(LinkedListNode<TValue> node)
    {
        var type = node.Value.GetType().Name;
        if (_groups[type] == node)
        {
            if (node.Previous?.Value.GetType().Name == type)
                _groups[type] = node.Previous;
            else
                _groups.Remove(type);
        }
        _list.Remove(node);
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
