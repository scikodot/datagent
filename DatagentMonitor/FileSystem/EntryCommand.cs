namespace DatagentMonitor.FileSystem;

public enum CommandAction
{
    Copy = 0,
    CopyWithOverwrite = 1, 
    Rename = 2, 
    Delete = 3
}

//public readonly record struct CopyProperties(string SourceName);

public record class EntryCommand
{
    public string Path { get; private init; }

    public CommandAction Action { get; private init; }

    public RenameProperties? RenameProperties { get; private init; }
    //public CopyProperties? CopyProperties { get; private init; }

    public EntryCommand(
        string path, CommandAction action,
        RenameProperties? renameProps)
    {
        var actionName = EnumExtensions.GetNameEx(action);

        string ExceptionMessage(string msg) => $"{actionName}: {msg}";

        if (string.IsNullOrEmpty(path))
            throw new ArgumentException("Path was null or empty.");

        switch (action, renameProps)
        {
            // Rename properties must not be present
            case (CommandAction.Copy or CommandAction.Delete, not null):
                throw new ArgumentException(ExceptionMessage("Rename properties must not be present."));

            // Rename properties must be present
            case (CommandAction.Rename, null):
                throw new ArgumentException(ExceptionMessage("Rename properties must be present."));
        }

        Path = path;
        Action = action;
        RenameProperties = renameProps;
    }

    public override string ToString() => $"{Action} {Path}";
}
