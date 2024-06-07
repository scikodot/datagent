using System.IO.Pipes;
using DatagentMonitor.FileSystem;
using System.Collections.Concurrent;
using DatagentMonitor.Synchronization;

namespace DatagentMonitor;

public class Program
{
    private static SyncSourceManager _sourceManager;
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

                        new Synchronizer(_sourceManager, targetRoot).Run(out _, out _, out _, out _);
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
            await WriteOutput($"[{nameof(EntryAction.Create)}] {e.FullPath}");
        }));
    }

    private static void OnRenamed(object sender, RenamedEventArgs e)
    {
        _tasks.Enqueue(new Task(async () =>
        {
            await _sourceManager.OnRenamed(e);
            await WriteOutput($"[{nameof(EntryAction.Rename)}] {e.OldFullPath} -> {e.Name}");
        }));
    }

    private static void OnChanged(object sender, FileSystemEventArgs e)
    {
        _tasks.Enqueue(new Task(async () =>
        {
            await _sourceManager.OnChanged(e);
            await WriteOutput($"[{nameof(EntryAction.Change)}] {e.FullPath}");
        }));
    }

    private static void OnDeleted(object sender, FileSystemEventArgs e)
    {
        _tasks.Enqueue(new Task(async () =>
        {
            await _sourceManager.OnDeleted(e);
            await WriteOutput($"[{nameof(EntryAction.Delete)}] {e.FullPath}");
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