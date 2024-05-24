using DatagentMonitor.FileSystem;
using DatagentShared;

namespace DatagentMonitor;

internal class SyncSourceManager : SourceManager
{
    private readonly Index _index;
    public Index Index => _index;

    private SyncDatabase? _syncDatabase;
    public SyncDatabase SyncDatabase => _syncDatabase ??= new SyncDatabase(FolderPath);

    public SyncSourceManager(string root) : base(root)
    {
        _index = new Index(root, _folderName, d => !IsServiceLocation(d.FullName));
    }

    public async Task OnCreated(FileSystemEventArgs e)
    {
        // Ignore service files creation
        if (IsServiceLocation(e.FullPath))
            return;

        var subpath = GetSubpath(e.FullPath);
        var parent = _index.Root.GetParent(subpath);
        if (Directory.Exists(e.FullPath))
        {
            var directory = new DirectoryInfo(e.FullPath);
            parent.Directories.Add(new CustomDirectoryInfo(directory));
            foreach (var change in EnumerateCreatedDirectory(directory, DateTime.Now))
                await SyncDatabase.AddEvent(change);
        }
        else
        {
            // TODO: consider switching to CreateProps w/ CreationTime property
            var file = new FileInfo(e.FullPath);
            parent.Files.Add(new CustomFileInfo(file));
            await SyncDatabase.AddEvent(new NamedEntryChange(
                subpath, 
                FileSystemEntryType.File, 
                FileSystemEntryAction.Create)
            {
                Timestamp = DateTime.Now,
                ChangeProperties = new ChangeProperties
                {
                    LastWriteTime = file.LastWriteTime,
                    Length = file.Length
                }
            });
        }
    }

    public IEnumerable<NamedEntryChange> EnumerateCreatedDirectory(DirectoryInfo root, DateTime? timestamp = null)
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
                    yield return new NamedEntryChange(
                        GetSubpath(directory.FullName), 
                        FileSystemEntryType.Directory, 
                        FileSystemEntryAction.Create)
                    {
                        Timestamp = timestamp ?? directory.LastWriteTime
                    };
                    break;

                case FileInfo file:
                    yield return new NamedEntryChange(
                        GetSubpath(file.FullName), 
                        FileSystemEntryType.File, 
                        FileSystemEntryAction.Create)
                    {
                        Timestamp = timestamp ?? file.LastWriteTime,
                        ChangeProperties = new ChangeProperties
                        {
                            LastWriteTime = file.LastWriteTime,  // TODO: TrimMicroseconds()?
                            Length = file.Length
                        }
                    };
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

        var subpath = GetSubpath(e.OldFullPath);
        var parent = _index.Root.GetParent(subpath);
        if (!parent.Remove(e.OldName, out var entry))
            throw new KeyNotFoundException(e.OldName);

        entry.Name = e.Name;
        parent.Add(entry);

        await SyncDatabase.AddEvent(new NamedEntryChange(
            subpath, 
            entry is CustomDirectoryInfo ? FileSystemEntryType.Directory : FileSystemEntryType.File, 
            FileSystemEntryAction.Rename)
        {
            RenameProperties = new RenameProperties
            {
                Name = e.Name
            }
        });
    }

    public async Task OnChanged(FileSystemEventArgs e)
    {
        // Ignore service files changes; we cannot distinguish user-made changes from software ones 
        if (IsServiceLocation(e.FullPath))
            return;

        // Track changes to files only; directory changes are not essential
        if (Directory.Exists(e.FullPath))
            return;

        var subpath = GetSubpath(e.FullPath);
        var parent = _index.Root.GetParent(subpath);
        var oldFile = parent.Files[e.Name];
        var newFile = new FileInfo(e.FullPath);
        oldFile.LastWriteTime = newFile.LastWriteTime;
        oldFile.Length = newFile.Length;

        await SyncDatabase.AddEvent(new NamedEntryChange(
            subpath, 
            FileSystemEntryType.File, 
            FileSystemEntryAction.Change)
        {
            ChangeProperties = new ChangeProperties
            {
                LastWriteTime = newFile.LastWriteTime,
                Length = newFile.Length
            }
        });
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
        
        var subpath = GetSubpath(e.FullPath);
        var parent = _index.Root.GetParent(subpath);
        parent.Remove(e.Name, out var entry);

        await SyncDatabase.AddEvent(new NamedEntryChange(
            subpath, 
            entry is CustomDirectoryInfo ? FileSystemEntryType.Directory : FileSystemEntryType.File,
            FileSystemEntryAction.Delete));
    }
}
