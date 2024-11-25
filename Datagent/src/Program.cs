using Avalonia;
using Avalonia.ReactiveUI;
using Datagent.Extensions;
using System;
using System.CommandLine;
using System.Diagnostics;
using System.IO.Pipes;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using DatagentShared;

namespace Datagent;

class Program
{
    class CommandLine
    {
        private static readonly string _description =
            "Data management tool that provides multiple capabilities " +
            "and serves as an interface for subservices.";

        private static readonly Argument<string> _processNameArg = 
            new Argument<string>("name", "Name of a running subservice.").FromAmong(
                DatagentMonitor.Program.ProcessName
            );

        private static Command RunCommand
        {
            get
            {
                var command = new Command("run",
                    "Launches a new instance of the specified subservice.")
                {
                    _processNameArg
                };
                command.SetHandler(name => RunSubservice(name), _processNameArg);
                return command;
            }
        }

        private static Command ListenCommand
        {
            get
            {
                var command = new Command("listen",
                    "Enables capturing the output provided by the specified subservice.")
                {
                    _processNameArg
                };
                command.SetHandler(name => ListenSubservice(name), _processNameArg);
                return command;
            }
        }

        private static Command DropCommand
        {
            get
            {
                var command = new Command("drop",
                    "Communicates to the specified subservice that it needs to stop.")
                {
                    _processNameArg
                };
                command.SetHandler(name => DropSubservice(name), _processNameArg);
                return command;
            }
        }

        internal static RootCommand RootCommand
        {
            get
            {
                var command = new RootCommand(_description)
                {
                    RunCommand,
                    ListenCommand,
                    DropCommand,
                    DatagentMonitor.Program.CommandLine.Command
                };
                command.SetHandler(() => RunGUI());
                return command;
            }
        }
    }

    private static readonly int _connectionTimeout = 10000;

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        return await CommandLine.RootCommand.InvokeAsync(args);
    }

    private static string[] GetCommandLineArgs() => Environment.GetCommandLineArgs()[1..];

    private static Process? GetSubservice(string name) => Process.GetProcessesByName(name).SingleOrDefault();

    private static void RunGUI()
    {
        BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(GetCommandLineArgs());
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    private static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseDynamicBinding()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI();

    private static void RunSubservice(string name)
    {
        using var process = GetSubservice(name);
        if (process is not null)
        {
            Console.WriteLine($"Service '{name}' (ID = {process.Id}) is already running.");
            return;
        }
        
        using var processNew = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = $"{name}.exe",
                Arguments = string.Join(" ", GetCommandLineArgs()),
                CreateNoWindow = true
            }
        };
        processNew.Start();
    }

    private static void RegisterPosixInterruptSignals(Action<PosixSignalContext> action)
    {
        //PosixSignalRegistration.Create(PosixSignal.SIGTSTP, interrupt);  // TODO: use on Unix only
        PosixSignalRegistration.Create(PosixSignal.SIGTERM, action);
        PosixSignalRegistration.Create(PosixSignal.SIGINT, action);
    }

    private static NamedPipeClientStream? ConnectSubservice(Process process, PipeDirection pipeDirection)
    {
        var configReader = new ConfigurationReader(process.ProcessName);
        var pipeClient = new NamedPipeClientStream(".", configReader.GetValue("pipe_names", pipeDirection switch
            {
                PipeDirection.In => "out",
                PipeDirection.Out => "in"
            })!,
            pipeDirection, PipeOptions.CurrentUserOnly);
        Console.Write($"Connecting to service '{process.ProcessName}' (ID = {process.Id})... ");
        try
        {
            pipeClient.Connect(_connectionTimeout);
        }
        catch (Exception e) when (e is TimeoutException or IOException)
        {
            Console.WriteLine("Failed.");
            Console.WriteLine(e);
            return null;
        }

        Console.WriteLine("Success!");
        return pipeClient;
    }

    private static void ListenSubservice(string name)
    {
        using var process = GetSubservice(name);
        if (process is null)
        {
            Console.WriteLine($"No active service '{name}' to listen.");
            return;
        }

        using var pipeClient = ConnectSubservice(process, PipeDirection.In);
        if (pipeClient is null)
        {
            Console.WriteLine($"Could not establish connection to service '{name}' to listen.");
            Console.WriteLine("Listen failed.");
            return;
        }

        bool up = true;
        RegisterPosixInterruptSignals(ctx =>
        {
            up = false;
            Console.WriteLine($"Received {ctx.Signal}, exiting...");
        });

        while (up)
        {
            try
            {
                Console.WriteLine(pipeClient.ReadString());
            }
            catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException or IOException)
            {
                Console.WriteLine("The receiving process has got closed.");
                break;
            }
        }
    }

    private static void DropSubservice(string name) 
    {
        using var process = GetSubservice(name);
        if (process is null)
        {
            Console.WriteLine($"No active service '{name}' to drop.");
            return;
        }

        using var pipeClient = ConnectSubservice(process, PipeDirection.Out);
        if (pipeClient is null)
        {
            Console.WriteLine($"Could not establish connection to service '{name}' to drop.");
            Console.WriteLine("Drop failed.");
            return;
        }

        Console.Write("Dropping... ");
        pipeClient.WriteString("drop");
        if (!process.WaitForExit(_connectionTimeout))
        {
            Console.WriteLine("No response. Killing...");
            process.Kill();
        }

        Console.WriteLine("Success!");
    }
}
