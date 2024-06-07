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

public delegate void CustomRenameEventHandler(object sender, CustomRenameEventArgs e);

public record class CustomRenameEventArgs(string OldName, string Name);

public abstract class CustomFileSystemInfo
{
    public event CustomRenameEventHandler? NamePropertyChanged;

    public abstract EntryType Type { get; }

    protected string _name;
    public string Name
    {
        get => _name;
        set
        {
            if (value != _name)
            {
                NamePropertyChanged?.Invoke(this, new CustomRenameEventArgs(_name, value));
                _name = value;
            }
        }
    }

    private DateTime _lastWriteTime;
    public DateTime LastWriteTime
    {
        get => _lastWriteTime;
        set => _lastWriteTime = value;
    }

    protected CustomFileSystemInfo(string name, DateTime lastWriteTime)
    {
        _name = name;
        _lastWriteTime = lastWriteTime;
    }

    protected CustomFileSystemInfo(FileSystemInfo info)
    {
        if (!info.Exists)
            throw info switch
            {
                DirectoryInfo => new DirectoryNotFoundException(info.FullName),
                FileInfo => new FileNotFoundException(info.FullName)
            };

        _name = info.Name;
        _lastWriteTime = info.LastWriteTime;
    }

    public static CustomFileSystemInfo Parse(string entry)
    {
        var split = entry!.Split(new char[] { ':', ',' }, StringSplitOptions.TrimEntries);
        var name = split[0];
        return split.Length switch
        {
            1 => throw new ArgumentException($"Could not parse the entry: '{entry}'"),
            2 => new CustomDirectoryInfo(name, DateTimeExtensions.Parse(split[1])),
            _ => new CustomFileInfo(name, DateTimeExtensions.Parse(split[1]), long.Parse(split[2]))
        };
    }
}
