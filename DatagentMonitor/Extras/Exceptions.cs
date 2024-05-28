using DatagentMonitor.FileSystem;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DatagentMonitor;

internal class InvalidActionSequenceException : Exception
{
    public InvalidActionSequenceException(FileSystemEntryAction actionFirst, FileSystemEntryAction actionSecond) : 
        base($"Invalid action sequence: {actionSecond} after {actionFirst}.") { }
}

internal class InvalidConflictException : Exception
{
    public InvalidConflictException(FileSystemEntryAction? sourceAction, FileSystemEntryAction? targetAction) :
        base($"Invalid conflict.\n" +
        $"Source: {sourceAction}\n" +
        $"Target: {targetAction}") { }
}

internal class InvalidIndexFormatException : Exception
{
    public InvalidIndexFormatException() : base("Invalid index format.") { }
}
