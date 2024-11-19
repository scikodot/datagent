using System.IO.Pipes;

namespace DatagentMonitor;

internal static class PipeServer
{
    private static NamedPipeServerStream? _streamIn;
    private static NamedPipeServerStream? _streamOut;
    private static int _streamInTimeout = 5000;
    private static int _streamOutTimeout = 5000;

    private static InvalidOperationException NotInitializedEx => new("The pipe server is not initialized.");

    public static void Initialize()
    {
        _streamIn = new NamedPipeServerStream(Launcher.InputPipeServerName, PipeDirection.In, 1,
            PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly | PipeOptions.WriteThrough | PipeOptions.Asynchronous);
        _streamOut = new NamedPipeServerStream(Launcher.OutputPipeServerName, PipeDirection.Out, 1,
            PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly | PipeOptions.WriteThrough | PipeOptions.Asynchronous);
    }

    public static async Task<string?> ReadInput()
    {
        if (_streamIn is null)
            throw NotInitializedEx;

        await _streamIn.WaitForConnectionSafeAsync(milliseconds: _streamInTimeout);
        return await _streamIn.ReadStringSafeAsync();
    }

    public static async Task WriteOutput(string message)
    {
        if (_streamOut is null)
            throw NotInitializedEx;

#if DEBUG
        Console.WriteLine(message);
#endif
        await _streamOut.WaitForConnectionSafeAsync(milliseconds: _streamOutTimeout);
        await _streamOut.WriteStringSafeAsync(message);
    }

    public static void Close()
    {
        _streamIn?.Close();
        _streamOut?.Close();
    }
}
