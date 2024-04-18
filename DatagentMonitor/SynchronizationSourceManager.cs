using DatagentMonitor.FileSystem;
using DatagentShared;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DatagentMonitor;

internal class SynchronizationSourceManager : SourceManager
{
    private static readonly string _eventsDatabaseName = "events.db";
    public static string EventsDatabaseName => _eventsDatabaseName;

    private static readonly string _indexName = "index.txt";
    public static string IndexName => _indexName;

    private readonly CustomDirectoryInfo _rootImage;
    public CustomDirectoryInfo RootImage => _rootImage;

    private Database? _eventsDatabase;
    public Database EventsDatabase
    {
        get
        {
            if (_eventsDatabase == null)
            {
                _eventsDatabase = new Database(EventsDatabasePath);
                using var command = new SqliteCommand(
                    "CREATE TABLE IF NOT EXISTS events (time TEXT, path TEXT, type TEXT, prop TEXT)");
                _eventsDatabase.ExecuteNonQuery(command);
            }

            return _eventsDatabase;
        }
    }

    public string EventsDatabasePath => Path.Combine(_root, _folderName, _eventsDatabaseName);
    public string IndexPath => Path.Combine(_root, _folderName, _indexName);

    public SynchronizationSourceManager(string root) : base(root)
    {
        _rootImage = new CustomDirectoryInfo(_root, d => !IsServiceLocation(d.FullName));

        // Ensure the index is initialized
        if (!File.Exists(IndexPath))
            SerializeIndex(_rootImage);
    }

    public void SerializeIndex(CustomDirectoryInfo info)
    {
        using var writer = new StreamWriter(IndexPath, append: false, encoding: Encoding.UTF8);
        writer.Write(CustomDirectoryInfoSerializer.Serialize(info));
    }

    public CustomDirectoryInfo DeserializeIndex()
    {
        using var reader = new StreamReader(IndexPath, encoding: Encoding.UTF8);
        return CustomDirectoryInfoSerializer.Deserialize(reader);
    }

    public async Task OnCreated(FileSystemEventArgs e)
    {
        // Ignore service files creation
        if (IsServiceLocation(e.FullPath))
            return;

        var subpath = GetSubpath(e.FullPath);
        var parent = _rootImage.GetParent(subpath);
        if (Directory.Exists(e.FullPath))
        {
            var directory = new DirectoryInfo(e.FullPath);
            parent.Directories.Add(new CustomDirectoryInfo(directory));
            await OnDirectoryCreated(directory, new StringBuilder(subpath + Path.DirectorySeparatorChar));
        }
        else
        {
            // TODO: consider switching to CreateProps w/ CreationTime property
            var file = new FileInfo(e.FullPath);
            parent.Files.Add(new CustomFileInfo(file));
            await InsertEventEntry(subpath, FileSystemEntryAction.Create, properties: new ChangeProperties
            {
                LastWriteTime = file.LastWriteTime,
                Length = file.Length
            });
        }
    }

    private async Task OnDirectoryCreated(DirectoryInfo root, StringBuilder builder)
    {
        await InsertEventEntry(builder.ToString(), FileSystemEntryAction.Create);

        // Using a separator in the end of a directory name helps distinguishing file creation VS directory creation
        foreach (var directory in builder.Wrap(root.EnumerateDirectories(), d => d.Name + Path.DirectorySeparatorChar))
        {
            await OnDirectoryCreated(directory, builder);
        }

        foreach (var file in builder.Wrap(root.EnumerateFiles(), f => f.Name))
        {
            await InsertEventEntry(builder.ToString(), FileSystemEntryAction.Create, properties: new ChangeProperties
            {
                LastWriteTime = file.LastWriteTime,
                Length = file.Length
            });
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
        var parent = _rootImage.GetParent(subpath);
        if (!parent.Remove(e.OldName, out var entry))
            throw new KeyNotFoundException(e.OldName);

        entry.Name = e.Name;
        parent.Add(entry);

        if (entry is CustomDirectoryInfo)
            subpath += Path.DirectorySeparatorChar;

        await InsertEventEntry(subpath, FileSystemEntryAction.Rename, properties: new RenameProperties
        {
            Name = e.Name
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
        var parent = _rootImage.GetParent(subpath);
        var oldFile = parent.Files[e.Name];
        var newFile = new FileInfo(e.FullPath);
        oldFile.LastWriteTime = newFile.LastWriteTime;
        oldFile.Length = newFile.Length;

        await InsertEventEntry(subpath, FileSystemEntryAction.Change, properties: new ChangeProperties
        {
            LastWriteTime = newFile.LastWriteTime,
            Length = newFile.Length
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
        var parent = _rootImage.GetParent(subpath);
        parent.Remove(e.Name, out var entry);
        if (entry is CustomDirectoryInfo)
            subpath += Path.DirectorySeparatorChar;

        await InsertEventEntry(subpath, FileSystemEntryAction.Delete);
    }

    // TODO: make this method a memeber of EventsDatabase
    public async Task InsertEventEntry(string subpath, FileSystemEntryAction action, DateTime? timestamp = null, ActionProperties? properties = null)
    {
        if (action == FileSystemEntryAction.Change && CustomFileSystemInfo.IsDirectory(subpath))
            throw new DirectoryChangeActionNotAllowed();

        using var command = new SqliteCommand("INSERT INTO events VALUES (:time, :path, :type, :prop)");
        command.Parameters.AddWithValue(":time", (timestamp ?? DateTime.Now).ToString(CustomFileInfo.DateTimeFormat));
        command.Parameters.AddWithValue(":path", subpath);
        command.Parameters.AddWithValue(":type", FileSystemEntryActionExtensions.ActionToString(action));
        command.Parameters.AddWithValue(":prop", properties != null ? ActionProperties.Serialize(properties) : DBNull.Value);
        EventsDatabase.ExecuteNonQuery(command);  // TODO: use async
    }
}
