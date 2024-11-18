using DatagentMonitor.FileSystem;
using DatagentShared;

namespace DatagentMonitor.Synchronization;

internal class SyncSourceManager : SourceManager
{
    private readonly SyncDatabase _database;
    public SyncDatabase Database => _database;

    private readonly SourceIndex _index;
    public SourceIndex Index => _index;

    private readonly SourceFilter _filter;
    public SourceFilter Filter => _filter;

    public SyncSourceManager(string root) : base(root)
    {
        _database = new SyncDatabase(root);
        _index = new SourceIndex(root);
        _filter = new SourceFilter(root);
    }

    public async Task OnCreated(FileSystemEventArgs e)
    {
        var type = Directory.Exists(e.FullPath) ? EntryType.Directory : EntryType.File;

        // Ignore service files creation
        if (_filter.ServiceExcludes(e.FullPath))
            return;

        if (_filter.UserExcludes(e.FullPath, type))
            return;

        var timestamp = DateTimeStaticProvider.Now;
        var subpath = GetSubpath(e.FullPath);
        switch (type)
        {
            case EntryType.Directory:
                var directory = new DirectoryInfo(e.FullPath);
                _index.RootImage.Create(timestamp, subpath, new CustomDirectoryInfo(directory));
                foreach (var change in EnumerateCreatedDirectory(directory, timestamp))
                    await Database.AddEvent(change);
                break;

            case EntryType.File:
                // TODO: consider switching to CreateProps w/ CreationTime property
                var file = new FileInfo(e.FullPath);
                _index.RootImage.Create(timestamp, subpath, new CustomFileInfo(file));
                await Database.AddEvent(new EntryChange(
                    timestamp, subpath,
                    EntryType.File, EntryAction.Create,
                    null, new ChangeProperties(file)));
                break;
        }
    }

    public IEnumerable<EntryChange> EnumerateCreatedDirectory(DirectoryInfo root, DateTime? timestamp = null)
    {
        var stack = new Stack<FileSystemInfo>();
        stack.Push(root);
        while (stack.TryPop(out var entry))
        {
            switch (entry)
            {
                case DirectoryInfo directory:
                    foreach (var file in directory.EnumerateFiles())
                        stack.Push(file);
                    foreach (var subdir in directory.EnumerateDirectories())
                        stack.Push(subdir);
                    yield return new EntryChange(
                        timestamp ?? directory.LastWriteTime, GetSubpath(directory.FullName),
                        EntryType.Directory, EntryAction.Create,
                        null, new ChangeProperties(directory));
                    break;

                case FileInfo file:
                    yield return new EntryChange(
                        timestamp ?? file.LastWriteTime, GetSubpath(file.FullName),
                        EntryType.File, EntryAction.Create,
                        null, new ChangeProperties(file));
                    break;
            }
        }
    }

    public async Task OnRenamed(RenamedEventArgs e)
    {
        var type = Directory.Exists(e.FullPath) ? EntryType.Directory : EntryType.File;

        if (_filter.ServiceExcludes(e.FullPath))
        {
            // TODO: renaming service files may have unexpected consequences;
            // revert and/or throw an exception/notification
            return;
        }

        if (_filter.UserExcludes(e.FullPath, type))
            return;

        var timestamp = DateTimeStaticProvider.Now;
        var subpath = GetSubpath(e.OldFullPath);
        var renameProps = new RenameProperties(e.Name);
        _index.RootImage.Rename(timestamp, subpath, renameProps, out var entry);
        await Database.AddEvent(new EntryChange(
            timestamp, subpath,
            entry.Type, EntryAction.Rename,
            renameProps, null));
    }

    public async Task OnChanged(FileSystemEventArgs e)
    {
        // Track changes to files only; directory changes are not essential
        if (Directory.Exists(e.FullPath))
            return;

        // Ignore service files changes; we cannot distinguish user-made changes from software ones 
        if (_filter.ServiceExcludes(e.FullPath))
            return;

        if (_filter.UserExcludes(e.FullPath, EntryType.File))
            return;

        var timestamp = DateTimeStaticProvider.Now;
        var subpath = GetSubpath(e.FullPath);
        var changeProps = new ChangeProperties(new FileInfo(e.FullPath));
        _index.RootImage.Change(timestamp, subpath, changeProps, out var entry);
        await Database.AddEvent(new EntryChange(
            timestamp, subpath,
            entry.Type, EntryAction.Change,
            null, changeProps));
    }

    public async Task OnDeleted(FileSystemEventArgs e)
    {
        if (_filter.ServiceExcludes(e.FullPath))
        {
            // TODO: deleting service files may have unexpected consequences,
            // and deleting the database means losing the track of all events up to the moment;
            // revert and/or throw an exception/notification
            return;
        }

        var timestamp = DateTimeStaticProvider.Now;
        var subpath = GetSubpath(e.FullPath);
        _index.RootImage.Delete(timestamp, subpath, out var entry);

        if (_filter.UserExcludes(e.FullPath, entry.Type))
            return;

        // Add info about the deleted file or directory (and its contents) to the database
        var stack = new Stack<(CustomFileSystemInfo, string, bool)>();
        stack.Push((entry, subpath, entry is CustomFileInfo));
        while (stack.Count > 0)
        {
            var (info, path, visited) = stack.Pop();
            if (!visited)
            {
                stack.Push((info, path, true));

                var dir = (CustomDirectoryInfo)info;
                foreach (var file in dir.Entries.Files.Reverse())
                    stack.Push((file, Path.Combine(path, file.Name), true));  // no use visiting files twice, so visited = true
                foreach (var subdir in dir.Entries.Directories.Reverse())
                    stack.Push((subdir, Path.Combine(path, subdir.Name), false));
            }
            else
            {
                await Database.AddEvent(new EntryChange(
                    timestamp, path,
                    info.Type, EntryAction.Delete,
                    null, null));
            }
        }
    }
}
