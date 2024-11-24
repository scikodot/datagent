using Avalonia;
using Avalonia.ReactiveUI;
using Datagent.Extensions;
using System;
using System.CommandLine;
using System.Threading.Tasks;

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
                command.SetHandler(() => RunGUI(Environment.GetCommandLineArgs()));
                return command;
            }
        }
    }

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        return await CommandLine.RootCommand.InvokeAsync(args);
    }

    private static void RunGUI(string[] args)
    {
        BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);
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
        throw new NotImplementedException();
    }

    private static void ListenSubservice(string name)
    {
        throw new NotImplementedException();
    }

    private static void DropSubservice(string name) 
    {
        throw new NotImplementedException();
    }
}
