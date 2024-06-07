using DatagentMonitor.Collections;

namespace DatagentMonitor.FileSystem;

public class CustomFileSystemInfoCollection : GroupedLookupLinkedList<string, CustomFileSystemInfo>
{
    public IEnumerable<CustomDirectoryInfo> Directories => EnumerateGroup<CustomDirectoryInfo>();
    public IEnumerable<CustomFileInfo> Files => EnumerateGroup<CustomFileInfo>();

    protected override string GetKey(CustomFileSystemInfo info) => info.Name;

    public override void Add(CustomFileSystemInfo node)
    {
        base.Add(node);

        node.NamePropertyChanged += OnRenamed;
    }

    protected override void Remove(LinkedListNode<CustomFileSystemInfo> node)
    {
        base.Remove(node);

        node.Value.NamePropertyChanged -= OnRenamed;
    }

    // Rename callback that only moves the element to a new key in the lookup; 
    // element's positions in both list and group remain the same
    private void OnRenamed(object? sender, CustomRenameEventArgs e)
    {
        if (!_lookup.Remove(e.OldName, out var node))
            throw new KeyNotFoundException(e.OldName);

        _lookup.Add(e.Name, node);
    }
}
