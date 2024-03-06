using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace DatagentMonitor.Utils
{
    public class StringStream
    {
        private Stream ioStream;
        private UnicodeEncoding streamEncoding;

        public StringStream(Stream ioStream)
        {
            this.ioStream = ioStream;
            streamEncoding = new UnicodeEncoding();
        }

        public string ReadString()
        {
            int first = ioStream.ReadByte();
            if (first == -1)
                throw new IOException("End of stream.");

            int len;
            len = first * 256;
            len |= ioStream.ReadByte();
            var inBuffer = new byte[len];
            ioStream.Read(inBuffer, 0, len);

            return streamEncoding.GetString(inBuffer);
        }

        public async Task<string> ReadStringAsync()
        {
            // ReadAsync can read bytes faster than they are written to the stream;
            // continue reading until target count is reached
            int len = 2;
            var head = new byte[len];
            while (len > 0)
                len -= await ioStream.ReadAsync(head, head.Length - len, len);

            len = (head[0] * 256) | head[1];
            var inBuffer = new byte[len];
            while (len > 0)
                len -= await ioStream.ReadAsync(inBuffer, inBuffer.Length - len, len);

            return streamEncoding.GetString(inBuffer);
        }

        public int WriteString(string outString)
        {
            byte[] outBuffer = streamEncoding.GetBytes(outString);
            int len = outBuffer.Length;
            if (len > UInt16.MaxValue)
            {
                len = (int)UInt16.MaxValue;
            }
            ioStream.WriteByte((byte)(len / 256));
            ioStream.WriteByte((byte)(len & 255));
            ioStream.Write(outBuffer, 0, len);
            ioStream.Flush();

            return outBuffer.Length + 2;
        }
    }

    public static class MonitorUtils
    {
        private static string _processName = "DatagentMonitor";
        private static int _timeout = 10000;

        private static void RegisterPosixInterruptSignals(Action<PosixSignalContext> action)
        {
            //PosixSignalRegistration.Create(PosixSignal.SIGTSTP, interrupt);  // TODO: use on Unix only
            PosixSignalRegistration.Create(PosixSignal.SIGTERM, action);
            PosixSignalRegistration.Create(PosixSignal.SIGINT, action);
        }

        public static Process? GetMonitorProcess()
        {
            int affine = Process.GetCurrentProcess().ProcessName == _processName ? 1 : 0;
            var processes = Process.GetProcessesByName(_processName);
            if (processes.Length == affine)
                return null;
            if (processes.Length > affine + 1)
                throw new Exception("Multiple active monitor instances.");

            return processes.MinBy(p => p.StartTime);
        }

        public static void Launch(string[] args)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = $"{_processName}.exe",
                Arguments = string.Join(" ", args),
                CreateNoWindow = true
            };
            new Process
            {
                StartInfo = startInfo,
            }.Start();
        }

        public static void Listen()
        {
            var monitor = GetMonitorProcess();
            if (monitor is null)
            {
                Console.WriteLine("No active monitor to listen.");
                return;
            }

            bool up = true;
            RegisterPosixInterruptSignals(ctx =>
            {
                up = false;
                Console.WriteLine($"{ctx.Signal} received, shutting down...");
            });

            var pipeClient = new NamedPipeClientStream(".", "datamon-out", PipeDirection.In, PipeOptions.CurrentUserOnly);
            Console.Write("Connecting to monitor... ");
            try
            {
                pipeClient.Connect(_timeout);
            }
            catch (Exception e) when (e is TimeoutException or IOException)
            {
                Console.WriteLine("Failed.");
                Console.WriteLine("Connection timed out. Possible reasons are:\n1. Another listener is already up\n2. Monitor is not available");
                return;
            }

            Console.WriteLine("Done!");

            AppDomain.CurrentDomain.ProcessExit += (s, e) =>
            {
                pipeClient.Close();
            };

            var stream = new StringStream(pipeClient);
            while (up)
            {
                try
                {
                    Console.WriteLine(stream.ReadString());
                }
                catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException or IOException)
                {
                    Console.WriteLine("Monitor closed.");
                    break;
                }
            }
        }

        public static void Drop()
        {
            var monitor = GetMonitorProcess();
            if (monitor is null)
            {
                Console.WriteLine("No active monitor to drop.");
                return;
            }
            Console.WriteLine($"Monitor process ID: {monitor.Id}");

            var pipeClient = new NamedPipeClientStream(".", "datamon-in", PipeDirection.Out, PipeOptions.CurrentUserOnly);
            Console.Write("Connecting to monitor... ");
            pipeClient.Connect();
            Console.WriteLine("Done!");

            AppDomain.CurrentDomain.ProcessExit += (s, e) =>
            {
                pipeClient.Close();
            };

            Thread.Sleep(10000);

            var stream = new StringStream(pipeClient);
            Console.Write("Dropping... ");
            stream.WriteString("DROP");
            if (!monitor.WaitForExit(10000))
            {
                Console.WriteLine("No response. Killing...");
                monitor.Kill();
            }
            Console.WriteLine("Done!");
        }
    }
}
