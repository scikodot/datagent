using DatagentShared;
using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using DatagentMonitor.FileSystem;
using DatagentMonitor.Utils;
using System.Net.NetworkInformation;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System;
using System.Threading.Tasks;

namespace DatagentMonitor;

public class Program
{
    private static SynchronizationSourceManager _sourceManager;
    private static NamedPipeServerStream _pipeServerIn;
    private static NamedPipeServerStream _pipeServerOut;
    private static readonly ConcurrentQueue<Task> _tasks = new();

    static async Task Main(string[] args)
    {
        var monitor = Launcher.GetMonitorProcess();
        if (monitor is not null)
        {
            Console.WriteLine($"Monitor is already up. Process ID: {monitor.Id}");
            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();
            return;
        }
            
        /*foreach (var arg in args)
        {
            if (arg.Length < 3)
                throw new ArgumentException("No argument name given.");

            var split = arg[2..].Split('=');
            if (split.Length < 2 || split[0] == "" || split[1] == "")
                throw new ArgumentException("No argument name and/or value given.");

            (var argName, var argValue) = (split[0], split[1]);
            switch (argName)
            {
                case "db-path":
                    _dbPath = argValue;
                    break;
                //...
                default:
                    throw new ArgumentException($"Unexpected argument: {argName}");
            }
        }*/

        try
        {
            _sourceManager = new SynchronizationSourceManager(root: Path.Combine("D:", "_source"));

            var targetRoot = Path.Combine("D:", "_target");

            _sourceManager.EventsDatabase.ExecuteNonQuery(
                new SqliteCommand("CREATE TABLE IF NOT EXISTS events (time TEXT, type TEXT, path TEXT, misc TEXT)"));

            var watcher = new FileSystemWatcher(_sourceManager.Root)
            {
                NotifyFilter = NotifyFilters.Attributes
                                | NotifyFilters.CreationTime
                                | NotifyFilters.DirectoryName
                                | NotifyFilters.FileName
                                | NotifyFilters.LastWrite
                                | NotifyFilters.Security
                                | NotifyFilters.Size
            };

            watcher.Created += OnCreated;
            watcher.Renamed += OnRenamed;
            watcher.Changed += OnChanged;
            watcher.Deleted += OnDeleted;
            watcher.Error += OnError;

            watcher.Filter = "*";  // track all files, even with no extension
            watcher.IncludeSubdirectories = true;
            watcher.EnableRaisingEvents = true;

            // TODO: consider moving pipe management to MonitorUtils or somewhere else
            _pipeServerIn = new NamedPipeServerStream(Launcher.InputPipeServerName, PipeDirection.In, 1,
                PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly | PipeOptions.WriteThrough | PipeOptions.Asynchronous);
            _pipeServerOut = new NamedPipeServerStream(Launcher.OutputPipeServerName, PipeDirection.Out, 1,
                PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly | PipeOptions.WriteThrough | PipeOptions.Asynchronous);

            AppDomain.CurrentDomain.ProcessExit += (s, e) =>
            {
                _pipeServerIn.Close();
                _pipeServerOut.Close();
                // TODO: log status
            };

            bool up = true;
            while (up)
            {
                // Remove completed tasks up until the first uncompleted
                while (_tasks.TryPeek(out var task) && task.IsCompleted)
                    _tasks.TryDequeue(out _);

                // Wait for connection for some time, continue with the main loop if no response
                var result = await _pipeServerIn.WaitForConnectionSafeAsync(milliseconds: 15000);
                var input = await _pipeServerIn.ReadStringSafeAsync();
                if (input == null)
                    continue;

                Console.WriteLine($"Received: {input}");
                switch (input)
                {
                    case "SYNC":
                        // Wait for all queued tasks to complete before syncing
                        int tasksCount = _tasks.Count;
                        Console.WriteLine($"Awaiting tasks: {tasksCount}");
                        for (int i = 0; i < tasksCount; i++)
                        {
                            _tasks.TryDequeue(out var task);
                            task!.RunSynchronously();
                        }

                        new Synchronizer(_sourceManager).Run(targetRoot);
                        break;
                    case "DROP":
                        up = false;
                        Console.WriteLine("Shutting down...");
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            // TODO: log ex info somewhere
            Console.WriteLine(ex.ToString());
            Console.WriteLine($"Args: {string.Join(" ", args)}");
        }

        Console.WriteLine("Press any key to continue...");
        Console.ReadKey();
    }

    private static void OnCreated(object sender, FileSystemEventArgs e)
    {
        _tasks.Enqueue(new Task(async () =>
        {
            // Ignore service files creation
            var subpath = _sourceManager.GetRootSubpath(e.FullPath);
            if (SourceManager.IsServiceLocation(subpath))
                return;

            var entry = new FileInfo(e.FullPath);
            if (entry.Attributes.HasFlag(FileAttributes.Directory))
            {
                await OnDirectoryCreated(new DirectoryInfo(e.FullPath), new StringBuilder(subpath + Path.DirectorySeparatorChar));
            }
            else
            {
                // TODO: consider switching to CreateProps w/ CreationTime property
                var properties = new ChangeProperties
                {
                    LastWriteTime = entry.LastWriteTime,
                    Length = entry.Length
                };
                await InsertEventEntry(FileSystemEntryAction.Create, subpath, properties: properties);
            }
        }));
    }

    private static async Task OnDirectoryCreated(DirectoryInfo root, StringBuilder builder)
    {
        await InsertEventEntry(FileSystemEntryAction.Create, builder.ToString());

        // Using a separator in the end of a directory name helps distinguishing file creation VS directory creation
        foreach (var directory in builder.Wrap(root.EnumerateDirectories(), d => d.Name + Path.DirectorySeparatorChar))
        {
            await OnDirectoryCreated(directory, builder);
        }

        foreach (var file in builder.Wrap(root.EnumerateFiles(), f => f.Name))
        {
            var properties = new ChangeProperties
            {
                LastWriteTime = file.LastWriteTime,
                Length = file.Length
            };
            await InsertEventEntry(FileSystemEntryAction.Create, builder.ToString(), properties: properties);
        }
    }

    private static void OnRenamed(object sender, RenamedEventArgs e)
    {
        _tasks.Enqueue(new Task(async () =>
        {
            var subpath = _sourceManager.GetRootSubpath(e.OldFullPath);
            if (SourceManager.IsServiceLocation(subpath))
            {
                // TODO: renaming service files may have unexpected consequences;
                // revert and/or throw an exception/notification
                return;
            }

            var entry = new FileInfo(e.FullPath);
            if (entry.Attributes.HasFlag(FileAttributes.Directory))
                subpath += Path.DirectorySeparatorChar;

            var properties = new RenameProperties
            {
                Name = e.Name
            };
            await InsertEventEntry(FileSystemEntryAction.Rename, subpath, properties: properties);
        }));
    }

    private static void OnChanged(object sender, FileSystemEventArgs e)
    {
        _tasks.Enqueue(new Task(async () =>
        {
            // Ignore service files changes; we cannot distinguish user-made changes from software ones 
            var subpath = _sourceManager.GetRootSubpath(e.FullPath);
            if (SourceManager.IsServiceLocation(subpath))
                return;

            // Track changes to files only; directory changes are not essential
            var file = new FileInfo(e.FullPath);
            if (file.Attributes.HasFlag(FileAttributes.Directory))
                return;

            var properties = new ChangeProperties
            {
                LastWriteTime = file.LastWriteTime,
                Length = file.Length
            };
            await InsertEventEntry(FileSystemEntryAction.Change, subpath, properties: properties);
        }));
    }

    private static void OnDeleted(object sender, FileSystemEventArgs e)
    {
        _tasks.Enqueue(new Task(async () =>
        {
            var subpath = _sourceManager.GetRootSubpath(e.FullPath);
            if (SourceManager.IsServiceLocation(subpath))
            {
                // TODO: deleting service files may have unexpected consequences,
                // and deleting the database means losing the track of all events up to the moment;
                // revert and/or throw an exception/notification
                return;
            }
            
            await InsertEventEntry(FileSystemEntryAction.Delete, subpath);
        }));
    }

    private static void OnError(object sender, ErrorEventArgs e)
    {
        Task.Run(async () =>
        {
            var ex = e.GetException();
            await WriteOutput($"Message: {ex.Message}\nStacktrace: {ex.StackTrace}\n");
        });
    }

    private static async Task InsertEventEntry(FileSystemEntryAction action, string path, DateTime? timestamp = null, ActionProperties? properties = null)
    {
        if (action == FileSystemEntryAction.Change && Path.EndsInDirectorySeparator(path))
            throw new DirectoryChangeActionNotAllowed();

        var actionStr = FileSystemEntryActionExtensions.ActionToString(action);
        var command = new SqliteCommand("INSERT INTO events VALUES (:time, :type, :path, :misc)");
        command.Parameters.AddWithValue(":time", timestamp != null ? timestamp: DateTime.Now.ToString(CustomFileInfo.DateTimeFormat));
        command.Parameters.AddWithValue(":type", actionStr);
        command.Parameters.AddWithValue(":path", path);
        command.Parameters.AddWithValue(":prop", properties != null ? ActionProperties.Serialize(properties) : DBNull.Value);
        _sourceManager.EventsDatabase.ExecuteNonQuery(command);

        await WriteOutput($"[{actionStr}] {path}");
    }

    private static async Task WriteOutput(string message)
    {
#if DEBUG
        Console.WriteLine(message);
#endif
        var tokenSource = new CancellationTokenSource(4000);
        await _pipeServerOut.WaitForConnectionAsync(tokenSource.Token);
        await _pipeServerOut.WriteStringSafeAsync(message);
    }
}