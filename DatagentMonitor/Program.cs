using DatagentMonitor.FileSystem;
using System.Collections.Concurrent;
using DatagentMonitor.Synchronization;

namespace DatagentMonitor;

public class Program
{
    private static SyncSourceManager _sourceManager;
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

        DateTimeStaticProvider.Initialize(DateTimeProviderFactory.FromDefault());

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
            var sourceRoot = Path.Combine("D:", "_source");
            var targetRoot = Path.Combine("D:", "_target");
            _sourceManager = new SyncSourceManager(sourceRoot);

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

            AppDomain.CurrentDomain.ProcessExit += (s, e) =>
            {
                PipeServer.Close();
                // TODO: log status
            };

            bool up = true;
            while (up)
            {
                // Remove completed tasks up until the first uncompleted
                while (_tasks.TryPeek(out var task) && task.IsCompleted)
                    _tasks.TryDequeue(out _);

                // Wait for connection for some time, continue with the main loop if no response
                var input = await PipeServer.ReadInput();
                if (input is null)
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

                        await new Synchronizer(_sourceManager, targetRoot).Run();
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
            await _sourceManager.OnCreated(e);
            await PipeServer.WriteOutput($"[{nameof(EntryAction.Create)}] {e.FullPath}");
        }));
    }

    private static void OnRenamed(object sender, RenamedEventArgs e)
    {
        _tasks.Enqueue(new Task(async () =>
        {
            await _sourceManager.OnRenamed(e);
            await PipeServer.WriteOutput($"[{nameof(EntryAction.Rename)}] {e.OldFullPath} -> {e.Name}");
        }));
    }

    private static void OnChanged(object sender, FileSystemEventArgs e)
    {
        _tasks.Enqueue(new Task(async () =>
        {
            await _sourceManager.OnChanged(e);
            await PipeServer.WriteOutput($"[{nameof(EntryAction.Change)}] {e.FullPath}");
        }));
    }

    private static void OnDeleted(object sender, FileSystemEventArgs e)
    {
        _tasks.Enqueue(new Task(async () =>
        {
            await _sourceManager.OnDeleted(e);
            await PipeServer.WriteOutput($"[{nameof(EntryAction.Delete)}] {e.FullPath}");
        }));
    }

    private static void OnError(object sender, ErrorEventArgs e)
    {
        Task.Run(async () =>
        {
            var ex = e.GetException();
            await PipeServer.WriteOutput($"Message: {ex.Message}\nStacktrace: {ex.StackTrace}\n");
        });
    }
}