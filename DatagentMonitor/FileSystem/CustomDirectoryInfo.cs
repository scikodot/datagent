namespace DatagentMonitor.FileSystem;

public class CustomDirectoryInfo : CustomFileSystemInfo
{
    public override EntryType Type => EntryType.Directory;

    public CustomFileSystemInfoCollection Entries { get; init; } = new();

    public CustomDirectoryInfo(string name, DateTime lastWriteTime) : base(name, lastWriteTime) { }

    public CustomDirectoryInfo(DirectoryInfo info, Func<FileSystemInfo, bool>? filter = null) : base(info)
    {
        var entries = info.EnumerateFileSystemInfos();
        if (filter is not null)
            entries = entries.Where(filter);

        foreach (var entry in entries)
            Entries.Add(entry switch
            {
                DirectoryInfo directory => new CustomDirectoryInfo(directory, filter),
                FileInfo file => new CustomFileInfo(file)
            });
    }

    public void Create(DateTime timestamp, string path, CustomFileSystemInfo entry)
    {
        var parents = GetParents(path);
        if (parents[^1].Entries.Remove(entry.Name, out var existing) && entry.Type == existing.Type)
            throw new ArgumentException($"Attempted to replace an existing change of the same type: {entry}");

        parents[^1].Entries.Add(entry);

        UpdateLastWriteTimes(parents, timestamp);
    }

    public void Rename(DateTime timestamp, string path, RenameProperties properties, out CustomFileSystemInfo entry)
    {
        var parents = GetParents(path);
        var name = Path.GetFileName(path);
        if (!parents[^1].Entries.TryGetValue(name, out entry))
            throw new KeyNotFoundException(name);

        entry.Name = properties.Name;

        UpdateLastWriteTimes(parents, timestamp);
    }

    public void Change(DateTime timestamp, string path, ChangeProperties properties, out CustomFileSystemInfo entry)
    {
        var parents = GetParents(path);
        var name = Path.GetFileName(path);
        if (!parents[^1].Entries.TryGetValue(name, out entry))
            throw new KeyNotFoundException(name);

        switch (entry)
        {
            case CustomDirectoryInfo directory:
                directory.LastWriteTime = properties.LastWriteTime;
                break;

            case CustomFileInfo file:
                file.LastWriteTime = properties.LastWriteTime;
                file.Length = properties.Length;
                break;
        }

        UpdateLastWriteTimes(parents, timestamp);
    }

    public void Delete(DateTime timestamp, string path, out CustomFileSystemInfo entry)
    {
        var parents = GetParents(path);
        var name = Path.GetFileName(path);
        if (!parents[^1].Entries.Remove(name, out entry))
            throw new KeyNotFoundException(name);

        UpdateLastWriteTimes(parents, timestamp);
    }

    private List<CustomDirectoryInfo> GetParents(string path)
    {
        var parents = new List<CustomDirectoryInfo> { this };
        var parent = this;
        foreach (var name in path.Split(Path.DirectorySeparatorChar).SkipLast(1))
        {
            if (!parent.Entries.TryGetValue(name, out var entry) || entry is not CustomDirectoryInfo)
                throw new ArgumentException($"Directory '{name}' not found for path '{path}'");

            parents.Add(parent = (CustomDirectoryInfo)entry);
        }

        return parents;
    }

    private static void UpdateLastWriteTimes(IEnumerable<CustomDirectoryInfo> parents, DateTime timestamp)
    {
        foreach (var parent in parents.Reverse())
        {
            if (parent.LastWriteTime < timestamp)
                parent.LastWriteTime = timestamp;
        }
    }

    public override string ToString() => $"{Name}: {LastWriteTime.Serialize()}";
}
