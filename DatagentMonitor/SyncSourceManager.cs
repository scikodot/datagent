using DatagentMonitor.FileSystem;
using DatagentShared;

namespace DatagentMonitor;

internal class SyncSourceManager : SourceManager
{
    private readonly SourceIndex _index;
    public SourceIndex Index => _index;

    private SyncDatabase? _syncDatabase;
    public SyncDatabase SyncDatabase => _syncDatabase ??= new SyncDatabase(FolderPath);

    public SyncSourceManager(string root) : base(root)
    {
        _index = new SourceIndex(root, _folderName, d => !IsServiceLocation(d.FullName));
    }

    public async Task OnCreated(FileSystemEventArgs e)
    {
        // Ignore service files creation
        if (IsServiceLocation(e.FullPath))
            return;

        var timestamp = DateTime.Now;
        var subpath = GetSubpath(e.FullPath);
        if (Directory.Exists(e.FullPath))
        {
            var directory = new DirectoryInfo(e.FullPath);
            _index.Root.Create(timestamp, subpath, new CustomDirectoryInfo(directory));
            foreach (var change in EnumerateCreatedDirectory(directory, timestamp))
                await SyncDatabase.AddEvent(change);
        }
        else
        {
            // TODO: consider switching to CreateProps w/ CreationTime property
            var file = new FileInfo(e.FullPath);
            _index.Root.Create(timestamp, subpath, new CustomFileInfo(file));
            await SyncDatabase.AddEvent(new EntryChange(
                timestamp, subpath, 
                EntryType.File, EntryAction.Create, 
                null, new ChangeProperties
                {
                    LastWriteTime = file.LastWriteTime,
                    Length = file.Length
                }));
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
                        null, new ChangeProperties
                        {
                            LastWriteTime = directory.LastWriteTime
                        });
                    break;

                case FileInfo file:
                    yield return new EntryChange(
                        timestamp ?? file.LastWriteTime, GetSubpath(file.FullName), 
                        EntryType.File, EntryAction.Create, 
                        null, new ChangeProperties
                        {
                            LastWriteTime = file.LastWriteTime,  // TODO: TrimMicroseconds()?
                            Length = file.Length
                        });
                    break;
            }
        }
    }

    public async Task OnRenamed(RenamedEventArgs e)
    {
        if (IsServiceLocation(e.OldFullPath))
        {
            // TODO: renaming service files may have unexpected consequences;
            // revert and/or throw an exception/notification
            return;
        }

        var timestamp = DateTime.Now;
        var subpath = GetSubpath(e.OldFullPath);
        var renameProps = new RenameProperties(e.Name);
        _index.Root.Rename(timestamp, subpath, renameProps, out var entry);
        await SyncDatabase.AddEvent(new EntryChange(
            timestamp, subpath, 
            entry.Type, EntryAction.Rename, 
            renameProps, null));
    }

    public async Task OnChanged(FileSystemEventArgs e)
    {
        // Ignore service files changes; we cannot distinguish user-made changes from software ones 
        if (IsServiceLocation(e.FullPath))
            return;

        // Track changes to files only; directory changes are not essential
        if (Directory.Exists(e.FullPath))
            return;

        var timestamp = DateTime.Now;
        var subpath = GetSubpath(e.FullPath);
        var file = new FileInfo(e.FullPath);
        var changeProps = new ChangeProperties
        {
            LastWriteTime = file.LastWriteTime,
            Length = file.Length
        };
        _index.Root.Change(timestamp, subpath, changeProps, out var entry);
        await SyncDatabase.AddEvent(new EntryChange(
            timestamp, subpath, 
            entry.Type, EntryAction.Change, 
            null, changeProps));
    }

    public async Task OnDeleted(FileSystemEventArgs e)
    {
        if (IsServiceLocation(e.FullPath))
        {
            // TODO: deleting service files may have unexpected consequences,
            // and deleting the database means losing the track of all events up to the moment;
            // revert and/or throw an exception/notification
            return;
        }

        var timestamp = DateTime.Now;
        var subpath = GetSubpath(e.FullPath);
        _index.Root.Delete(timestamp, subpath, out var entry);
        await SyncDatabase.AddEvent(new EntryChange(
            timestamp, subpath, 
            entry.Type, EntryAction.Delete, 
            null, null));
    }
}
