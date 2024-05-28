﻿using DatagentMonitor.FileSystem;
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

        var subpath = GetSubpath(e.FullPath);
        if (Directory.Exists(e.FullPath))
        {
            var directory = new DirectoryInfo(e.FullPath);
            _index.Root.Create(subpath, new CustomDirectoryInfo(directory));
            foreach (var change in EnumerateCreatedDirectory(directory, DateTime.Now))
                await SyncDatabase.AddEvent(change);
        }
        else
        {
            // TODO: consider switching to CreateProps w/ CreationTime property
            var file = new FileInfo(e.FullPath);
            _index.Root.Create(subpath, new CustomFileInfo(file));
            await SyncDatabase.AddEvent(new EntryChange(
                DateTime.Now, subpath, 
                FileSystemEntryType.File, FileSystemEntryAction.Create, 
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
                        FileSystemEntryType.Directory, FileSystemEntryAction.Create, 
                        null, null);
                    break;

                case FileInfo file:
                    yield return new EntryChange(
                        timestamp ?? file.LastWriteTime, GetSubpath(file.FullName), 
                        FileSystemEntryType.File, FileSystemEntryAction.Create, 
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

        var subpath = GetSubpath(e.OldFullPath);
        var renameProps = new RenameProperties(e.Name);
        _index.Root.Rename(subpath, renameProps, out var entry);
        await SyncDatabase.AddEvent(new EntryChange(
            DateTime.Now, subpath, 
            entry.Type, FileSystemEntryAction.Rename, 
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

        var subpath = GetSubpath(e.FullPath);
        var file = new FileInfo(e.FullPath);
        var changeProps = new ChangeProperties
        {
            LastWriteTime = file.LastWriteTime,
            Length = file.Length
        };
        _index.Root.Change(subpath, changeProps, out var entry);
        await SyncDatabase.AddEvent(new EntryChange(
            DateTime.Now, subpath, 
            entry.Type, FileSystemEntryAction.Change, 
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
        
        var subpath = GetSubpath(e.FullPath);
        _index.Root.Delete(subpath, out var entry);
        await SyncDatabase.AddEvent(new EntryChange(
            DateTime.Now, subpath, 
            entry.Type, FileSystemEntryAction.Delete, 
            null, null));
    }
}
