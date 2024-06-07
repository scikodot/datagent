using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace DatagentMonitor.Collections;

public class LinkedListGroup<T> : IEnumerable<T>
{
    private readonly string _type;

    public LinkedList<T> List { get; init; }
    public LinkedListNode<T>? First { get; private set; }
    public LinkedListNode<T>? Last { get; private set; }
    public int Count { get; private set; }

    public LinkedListGroup(LinkedList<T> list, LinkedListNode<T> node)
    {
        if (node.Value is null)
            throw new ArgumentException("The node is empty.");

        _type = node.Value.GetType().Name;
        List = list;
        List.AddLast(node);
        First = Last = node;
        Count++;
    }

    public void Add(LinkedListNode<T> node)
    {
        if (Count == 0)
            throw new InvalidOperationException("The group is in an invalidated state.");

        if (node.Value?.GetType().Name != _type)
            throw new ArgumentException("The node value type does not match group type.");

        List.AddAfter(Last!, node);
        Last = node;
        Count++;
    }

    public void Remove(LinkedListNode<T> node)
    {
        if (Count == 0)
            throw new InvalidOperationException("The group is in an invalidated state.");

        if (node.Value?.GetType().Name != _type)
            throw new ArgumentException("The node value type does not match group type.");

        if (First == Last)
        {
            First = Last = null;
            Count = 0;
            return;
        }

        if (node == First)
            First = node.Next;
        else if (node == Last)
            Last = node.Previous;

        List.Remove(node);
        Count--;
    }

    public IEnumerator<T> GetEnumerator()
    {
        if (Count == 0)
            yield break;

        var curr = First;
        while (curr != Last)
        {
            yield return curr!.Value;
            curr = curr.Next;
        }
        yield return Last!.Value;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/* Linked list with a lookup and grouping
 * 
 * Key features:
 * - Collection of non-intersecting groups of elements, grouped by their runtime type
 * - Common lookup guarantees keys' uniqueness throughout the collection (useful for files + directories)
 * - Abstract GetKey method used to extract keys for the lookup
 * - O(1) for Add, Contains, Remove
 * - Group-wise enumeration w/ maintained insertion order
 */
public abstract class GroupedLookupLinkedList<TKey, TValue> : ICollection<TValue>, IEnumerable<TValue>
    where TKey : notnull
    where TValue : notnull
{
    protected readonly LinkedList<TValue> _list = new();
    protected readonly Dictionary<TKey, LinkedListNode<TValue>> _lookup = new();
    protected readonly Dictionary<string, LinkedListGroup<TValue>> _groups = new();

    public TValue this[TKey key] => _lookup[key].Value;
    public int Count => _list.Count;
    public bool IsReadOnly => false;

    public virtual void Add(TValue value)
    {
        var node = new LinkedListNode<TValue>(value);
        _lookup.Add(GetKey(value), node);

        var type = value.GetType().Name;
        if (!_groups.TryGetValue(type, out var group))
            _groups.Add(type, new LinkedListGroup<TValue>(_list, node));
        else
            group.Add(node);
    }

    public void Clear()
    {
        var curr = _list.First;
        var next = curr?.Next;
        while (curr != null)
        {
            Remove(curr);
            curr = next;
            next = next?.Next;
        }

        _lookup.Clear();
    }

    public bool Contains(TValue value)
    {
        if (!_lookup.TryGetValue(GetKey(value), out var node))
            return false;

        return EqualityComparer<TValue>.Default.Equals(node.Value, value);
    }

    public void CopyTo(TValue[] array, int arrayIndex) => _list.CopyTo(array, arrayIndex);

    public IEnumerable<TGroup> EnumerateGroup<TGroup>() where TGroup : TValue
    {
        if (!_groups.TryGetValue(typeof(TGroup).Name, out var group))
            yield break;

        foreach (var item in group.Select(i => (TGroup)i))
            yield return item;
    }

    public IEnumerator<TValue> GetEnumerator() => _list.GetEnumerator();

    IEnumerator<TValue> IEnumerable<TValue>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    protected abstract TKey GetKey(TValue value);

    public bool Remove(TValue value)
    {
        if (_lookup.Remove(GetKey(value), out var node))
        {
            Remove(node);
            return true;
        }

        return false;
    }

    public bool Remove(TKey key)
    {
        if (_lookup.Remove(key, out var node))
        {
            Remove(node);
            return true;
        }

        return false;
    }

    public bool Remove(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        if (_lookup.Remove(key, out var node))
        {
            Remove(node);
            value = node.Value;
            return true;
        }

        value = default;
        return false;
    }

    protected virtual void Remove(LinkedListNode<TValue> node)
    {
        var type = node.Value.GetType().Name;
        var group = _groups[type];
        group.Remove(node);
        if (group.Count == 0)
            _groups.Remove(type);
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
