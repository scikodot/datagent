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
        //bool running = true;
        //Console.CancelKeyPress += (sender, e) =>
        //{
        //    e.Cancel = true;
        //    running = false;
        //    Console.WriteLine("Shutdown received, exiting...");
        //};

        if (args.Length > 0)
        {
            switch (args[0])
            {
                case "monitor":
                    if (args.Length < 2)
                    {
                        // Display help, etc.
                        Console.WriteLine("Monitor called, but nothing followed...");
                        return;
                    }

                    switch (args[1])
                    {
                        case "up":
                            // Spawn monitor process; it is background, so no window is needed
                            new Process
                            {
                                StartInfo = new ProcessStartInfo
                                {
                                    FileName = $"DatagentMonitor/bin/Debug/net6.0/{_monitorProcName}.exe",
                                    Arguments = string.Join(" ", args[1..]),
                                    CreateNoWindow = true
                                }
                            }.Start();
                            break;
                        case "down":
                            // Close monitor process; all listeners are to be closed automatically
                            var processes = Process.GetProcessesByName(_monitorProcName);
                            Console.WriteLine($"Processes IDs: [{string.Join(",", processes.Select(p => p.Id))}]");
                            var monitor = processes.MinBy(p => p.StartTime);
                            if (monitor is null)
                            {
                                Console.WriteLine("No active monitor to close.");
                                return;
                            }

                            Console.WriteLine($"Monitor process ID: {monitor.Id}");
                            monitor.Close();
                            Console.WriteLine("Monitor closed successfully.");
                            break;
                        default:
                            new Process
                            {
                                StartInfo = new ProcessStartInfo
                                {
                                    FileName = $"DatagentMonitor/bin/Debug/net6.0/{_monitorProcName}.exe",
                                    Arguments = string.Join(" ", args[1..]),
                                    UseShellExecute = true
                                }
                            }.Start();
                            break;
                    }
                    return;
                default:
                    throw new ArgumentException($"Unknown argument: {args[0]}");
            }
        }
        else
        {
            // Launch GUI
            BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
        }
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
