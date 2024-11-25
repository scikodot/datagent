using DatagentMonitor.FileSystem;
using DatagentMonitor.Synchronization;
using DatagentShared;
using System.Collections.Concurrent;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Diagnostics;
using System.Reflection;

namespace DatagentMonitor;

public class Program
{
    public static class CommandLine
    {
        private static readonly string _description = "Tool used to detect changes inside of a directory and backup it.";

        private static readonly Option<string> _sourceOption = new(
            aliases: new string[] { "--source", "-s" },
            isDefault: false,
            parseArgument: result =>
            {
                if (result.Tokens.Count == 0)
                    throw new ArgumentException("No source directory specified.");

                var directory = result.Tokens.Single().Value;
                if (!Directory.Exists(directory))
                    throw new DirectoryNotFoundException($"No such source directory: {directory}");

                return directory;
            }
        )
        { IsRequired = true };

        private static readonly Option<string> _targetOption = new(
            aliases: new string[] { "--target", "-t" },
            isDefault: false,
            parseArgument: result =>
            {
                if (result.Tokens.Count == 0)
                    throw new ArgumentException("No target directory specified.");

                // TODO: if the target dir is absent, it can be created,
                // so this must not be considered an exception
                var directory = result.Tokens.Single().Value;
                if (!Directory.Exists(directory))
                    throw new DirectoryNotFoundException($"No such target directory: {directory}");

                return directory;
            }
        )
        { IsRequired = true };

        private static Command MonitorCommand
        {
            get
            {
                var command = new Command("monitor", 
                    "Monitors changes inside of the specified directory.")
                {
                    _sourceOption
                };
                command.SetHandler(async s => await Monitor(s), _sourceOption);
                return command;
            }
        }

        private static Command SyncCommand
        {
            get
            {
                var command = new Command("sync",
                    "Performs synchronization of the source directory with the specified target directory.")
                {
                    _sourceOption,
                    _targetOption
                };
                command.SetHandler(async (s, t) => await Sync(s, t), _sourceOption, _targetOption);
                return command;
            }
        }

        internal static RootCommand RootCommand => new(_description)
        {
            MonitorCommand,
            SyncCommand
        };

        public static Command Command => new(ProcessName, _description)
        {
            MonitorCommand,
            SyncCommand
        };
    }

    private static readonly ConcurrentQueue<Task> _tasks = new();
    private static readonly Queue<Task> _tasksRun = new();
    private static SyncSourceManager? _sourceManager;
    private static PipeServer? _pipeServer;

    private static string? _processName;
    public static string ProcessName => _processName ??= Assembly.GetExecutingAssembly().GetName().Name!;

    private static ConfigurationReader? _configReader;
    internal static ConfigurationReader ConfigReader => _configReader ??= new ConfigurationReader(ProcessName);

    static async Task<int> Main(string[] args)
    {
        DateTimeStaticProvider.Initialize(DateTimeProviderFactory.FromDefault());
        return await CommandLine.RootCommand.InvokeAsync(args);
    }

    private static async Task Monitor(string sourceRoot)
    {
        var name = Assembly.GetExecutingAssembly().GetName().Name!;
        using var process = Process.GetProcessesByName(name).SingleOrDefault();
        if (process is not null)
        {
            Console.WriteLine($"Service '{name}' (ID = {process.Id}) is already running.");
            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();
            return;
        }

        var config = new ConfigurationReader(Assembly.GetExecutingAssembly().GetName().Name!);

        _sourceManager = new SyncSourceManager(sourceRoot);
        _pipeServer = new PipeServer(
            config.GetValue("pipe_names", "in")!,
            config.GetValue("pipe_names", "out")!);
        {
            using var watcher = new FileSystemWatcher(_sourceManager.Root)
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
                _pipeServer.Close();
                // TODO: log status
            };

            bool up = true;
            while (up)
            {
                // Remove completed tasks up until the first uncompleted
                while (_tasksRun.TryPeek(out var task) && task.IsCompleted)
                    _tasksRun.TryDequeue(out _);

                // Run the new tasks
                RunTasks();

                // Wait for connection for some time, continue with the main loop if no response
                var input = await _pipeServer.ReadInput();
                if (input is not null)
                {
                    Console.WriteLine($"Received: {input}");
                    switch (input)
                    {
                        case "drop":
                            // Ensure no new tasks are left not run
                            RunTasks();

                            // Wait for all running tasks to complete before dropping
                            var tasks = _tasksRun.ToArray();
                            Console.WriteLine($"Awaiting tasks: {tasks.Length}");
                            Task.WaitAll(tasks);
                            Console.WriteLine("Shutting down...");
                            break;
                    }
                }
            }
        }
    }

    private static async Task Sync(string sourceRoot, string targetRoot)
    {
        _sourceManager = new SyncSourceManager(sourceRoot);
        await new Synchronizer(_sourceManager, targetRoot).Run();
    }

    private static void RunTasks()
    {
        var count = _tasks.Count;
        while (count-- > 0)
        {
            if (_tasks.TryDequeue(out var task))
            {
                task.Start();
                _tasksRun.Enqueue(task);
            }
        }
    }

    private static void OnCreated(object sender, FileSystemEventArgs e)
    {
        _tasks.Enqueue(new Task(async () =>
        {
            await _sourceManager!.OnCreated(e);
            await _pipeServer!.WriteOutput($"[{nameof(EntryAction.Create)}] {e.FullPath}");
        }));
    }

    private static void OnRenamed(object sender, RenamedEventArgs e)
    {
        _tasks.Enqueue(new Task(async () =>
        {
            await _sourceManager!.OnRenamed(e);
            await _pipeServer!.WriteOutput($"[{nameof(EntryAction.Rename)}] {e.OldFullPath} -> {e.Name}");
        }));
    }

    private static void OnChanged(object sender, FileSystemEventArgs e)
    {
        _tasks.Enqueue(new Task(async () =>
        {
            await _sourceManager!.OnChanged(e);
            await _pipeServer!.WriteOutput($"[{nameof(EntryAction.Change)}] {e.FullPath}");
        }));
    }

    private static void OnDeleted(object sender, FileSystemEventArgs e)
    {
        _tasks.Enqueue(new Task(async () =>
        {
            await _sourceManager!.OnDeleted(e);
            await _pipeServer!.WriteOutput($"[{nameof(EntryAction.Delete)}] {e.FullPath}");
        }));
    }

    private static void OnError(object sender, ErrorEventArgs e)
    {
        Task.Run(async () =>
        {
            var ex = e.GetException();
            await _pipeServer!.WriteOutput($"Message: {ex.Message}\nStacktrace: {ex.StackTrace}\n");
        });
    }
}