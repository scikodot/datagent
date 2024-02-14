using Avalonia;
using Avalonia.ReactiveUI;
using Datagent.Extensions;
using System;
using System.Diagnostics;
using System.Linq;

namespace Datagent;

class Program
{
    private static string _monitorProcName = "DatagentMonitor";

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        Console.WriteLine("Launching...");
        bool running = true;
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            running = false;
            Console.WriteLine("Shutdown received, exiting...");
        };

        if (args.Length > 0 && args[0] == "monitor")
        {
            if (args.Length < 2)
                // Display help, etc.
                return;

            ProcessStartInfo processInfo;
            Process process;
            switch (args[1])
            {
                case "up":
                    // Spawn monitor process
                    processInfo = new ProcessStartInfo
                    {
                        FileName = $"DatagentMonitor/bin/Debug/net6.0/{_monitorProcName}.exe",
                        Arguments = string.Join(" ", args[1..]),
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    process = new Process
                    {
                        StartInfo = processInfo
                    };
                    process.Start();
                    break;
                case "listen":
                    // Listen to the output of the spawned monitor
                    processInfo = new ProcessStartInfo
                    {
                        FileName = $"DatagentMonitor/bin/Debug/net6.0/{_monitorProcName}.exe",
                        Arguments = string.Join(" ", args[1..])
                    };

                    process = new Process
                    {
                        StartInfo = processInfo
                    };
                    process.Start();
                    break;
            }

            return;
        }

        BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseDynamicBinding()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI();
}
