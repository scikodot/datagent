using System.IO.Pipes;

namespace DatagentShared;

public class PipeServer
{
    private static readonly int _streamInTimeout = 5000;
    private static readonly int _streamOutTimeout = 5000;
    private readonly NamedPipeServerStream _streamIn;
    private readonly NamedPipeServerStream _streamOut;

    public PipeServer(string inputName, string outputName)
    {
        _streamIn = new NamedPipeServerStream(inputName, PipeDirection.In, 1,
            PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly | PipeOptions.WriteThrough | PipeOptions.Asynchronous);
        _streamOut = new NamedPipeServerStream(outputName, PipeDirection.Out, 1,
            PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly | PipeOptions.WriteThrough | PipeOptions.Asynchronous);
    }

    public async Task<string?> ReadInput()
    {
        await _streamIn.WaitForConnectionSafeAsync(milliseconds: _streamInTimeout);
        return await _streamIn.ReadStringSafeAsync();
    }

    public async Task WriteOutput(string message)
    {
#if DEBUG
        Console.WriteLine(message);
#endif
        await _streamOut.WaitForConnectionSafeAsync(milliseconds: _streamOutTimeout);
        await _streamOut.WriteStringSafeAsync(message);
    }

    public void Close()
    {
        _streamIn.Close();
        _streamOut.Close();
    }
}
