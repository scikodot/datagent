using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests.SynchronizerTests;

public class Test4 : TestBase
{
    private static readonly List<FileSystemEntryChange> _changes = new()
    {
        new FileSystemEntryChange
        {
            Timestamp = new DateTime(2024, 4, 26, 19, 40, 0),
            Path = Path.Combine("folder1", "file1"),
            Action = FileSystemEntryAction.Rename,
            Properties = new FileSystemEntryChangeProperties
            {
                RenameProps = new RenameProperties
                {
                    Name = "file1-renamed-source-1"
                }
            }
        },
        new FileSystemEntryChange
        {
            Timestamp = new DateTime(2024, 4, 26, 19, 40, 1),
            Path = Path.Combine("folder1", "file2"),
            Action = FileSystemEntryAction.Change,
            Properties = new FileSystemEntryChangeProperties
            {
                ChangeProps = new ChangeProperties
                {
                    LastWriteTime = new DateTime(2024, 4, 26, 19, 40, 1),
                    Length = 3345
                }
            }
        },
        new FileSystemEntryChange
        {
            Timestamp = new DateTime(2024, 4, 26, 20, 0, 0),
            Path = "folder1" + Path.DirectorySeparatorChar,
            Action = FileSystemEntryAction.Rename,
            Properties = new FileSystemEntryChangeProperties
            {
                RenameProps = new RenameProperties
                {
                    Name = "folder1-renamed-source-1"
                }
            }
        },
        new FileSystemEntryChange
        {
            Timestamp = new DateTime(2024, 4, 26, 20, 40, 0),
            Path = Path.Combine("folder1-renamed-source-1", "file1-renamed-source-1"),
            Action = FileSystemEntryAction.Rename,
            Properties = new FileSystemEntryChangeProperties
            {
                RenameProps = new RenameProperties
                {
                    Name = "file1-renamed-source-2"
                }
            }
        },
        new FileSystemEntryChange
        {
            Timestamp = new DateTime(2024, 4, 26, 20, 40, 1),
            Path = Path.Combine("folder1-renamed-source-1", "file2"),
            Action = FileSystemEntryAction.Change,
            Properties = new FileSystemEntryChangeProperties
            {
                ChangeProps = new ChangeProperties
                {
                    LastWriteTime = new DateTime(2024, 4, 26, 20, 40, 1),
                    Length = 4345
                }
            }
        },
        new FileSystemEntryChange
        {
            Timestamp = new DateTime(2024, 4, 26, 21, 0, 0),
            Path = "folder1-renamed-source-1" + Path.DirectorySeparatorChar,
            Action = FileSystemEntryAction.Rename,
            Properties = new FileSystemEntryChangeProperties
            {
                RenameProps = new RenameProperties
                {
                    Name = "folder1-renamed-source-2"
                }
            }
        },
        new FileSystemEntryChange
        {
            Timestamp = new DateTime(2024, 4, 26, 21, 40, 0),
            Path = Path.Combine("folder1-renamed-source-2", "file1-renamed-source-2"),
            Action = FileSystemEntryAction.Rename,
            Properties = new FileSystemEntryChangeProperties
            {
                RenameProps = new RenameProperties
                {
                    Name = "file1-renamed-source-3"
                }
            }
        },
        new FileSystemEntryChange
        {
            Timestamp = new DateTime(2024, 4, 26, 21, 40, 1),
            Path = Path.Combine("folder1-renamed-source-2", "file2"),
            Action = FileSystemEntryAction.Change,
            Properties = new FileSystemEntryChangeProperties
            {
                ChangeProps = new ChangeProperties
                {
                    LastWriteTime = new DateTime(2024, 4, 26, 21, 40, 1),
                    Length = 5345
                }
            }
        },
    };

    public Test4() : base(_changes) { }
}
