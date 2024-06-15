using DatagentMonitor.FileSystem;

namespace DatagentMonitor;

internal class InvalidActionSequenceException : Exception
{
    public InvalidActionSequenceException(EntryAction actionFirst, EntryAction actionSecond) : 
        base($"Invalid action sequence: {actionSecond} after {actionFirst}.") { }
}

internal class InvalidConflictException : Exception
{
    public InvalidConflictException(
        EntryType sourceType, EntryAction? sourceAction, 
        EntryType targetType, EntryAction? targetAction) :
        base($"Invalid conflict.\n" +
        $"Source: {sourceType} {sourceAction?.ToString() ?? "<none>"}\n" +
        $"Target: {targetType} {targetAction?.ToString() ?? "<none>"}") { }
}

internal class InvalidIndexFormatException : Exception
{
    public InvalidIndexFormatException(int line, string msg = "") : 
        base($"Invalid index format at line {line}. {msg}") { }
}

internal class FutureTimestampException : Exception
{
    public FutureTimestampException(string paramName) :
        base($"{paramName} cannot be in the future.") { }
}
