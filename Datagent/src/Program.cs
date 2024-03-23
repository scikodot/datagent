using Avalonia;
using Avalonia.ReactiveUI;
using Datagent.Extensions;
using DatagentMonitor.Utils;
using System;
using System.Diagnostics;
using System.Linq;

namespace Datagent;

class Program
{
    private static string _monitorAssemblyName = "DatagentMonitor";

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0)
        {
            try
            {
                switch (args[0])
                {
                    case "monitor":
                        if (args.Length < 2)
                        {
                            // TODO: display help, etc.
                            Console.WriteLine("Monitor called, but nothing followed...");
                            return;
                        }

                        switch (args[1])
                        {
                            case "up":
                                MonitorUtils.Launch(args[1..]);
                                break;
                            case "listen":
                                MonitorUtils.Listen();
                                break;
                            case "sync":
                                MonitorUtils.Sync();
                                break;
                            case "down":
                                MonitorUtils.Drop();
                                break;
                            default:
                                throw new ArgumentException("Unknown argument value.");
                        }
                        return;
                    default:
                        throw new ArgumentException($"Unknown argument: {args[0]}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                Console.WriteLine($"Args: {string.Join(" ", args)}");
            }

            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();
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
