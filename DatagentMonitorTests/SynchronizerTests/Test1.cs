using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests.SynchronizerTests;

// Test all actions
public class Test1 : TestBase
{
    private static readonly List<FileSystemEntryChange> _changes = new()
    {
        new FileSystemEntryChange
        {
            Timestamp = new DateTime(2024, 4, 11, 15, 0, 0),
            Path = Path.Combine("folder1", "subfolder1", "ssubfolder1") + Path.DirectorySeparatorChar,
            Action = FileSystemEntryAction.Create
        },
        new FileSystemEntryChange
        {
            Timestamp = new DateTime(2024, 4, 11, 15, 0, 1),
            Path = "folder1" + Path.DirectorySeparatorChar,
            Action = FileSystemEntryAction.Rename,
            Properties = new FileSystemEntryChangeProperties
            {
                RenameProps = new RenameProperties
                {
                    Name = "folder1-renamed-source"
                }
            }
        },
        new FileSystemEntryChange
        {
            Timestamp = new DateTime(2024, 4, 11, 15, 0, 2),
            Path = "folder2" + Path.DirectorySeparatorChar,
            Action = FileSystemEntryAction.Delete
        },
        new FileSystemEntryChange
        {
            Timestamp = new DateTime(2024, 4, 11, 15, 0, 3),
            Path = "file5.xlsx",
            Action = FileSystemEntryAction.Create,
            Properties = new FileSystemEntryChangeProperties
            {
                ChangeProps = new ChangeProperties
                {
                    LastWriteTime = new DateTime(2007, 7, 7),
                    Length = 888
                }
            }
        },
        new FileSystemEntryChange
        {
            Timestamp = new DateTime(2024, 4, 11, 15, 0, 4),
            Path = "file5.xlsx",
            Action = FileSystemEntryAction.Rename,
            Properties = new FileSystemEntryChangeProperties
            {
                RenameProps = new RenameProperties
                {
                    Name = "file5-renamed-source.xlsx"
                }
            }
        },
        new FileSystemEntryChange
        {
            Timestamp = new DateTime(2024, 4, 11, 15, 0, 5),
            Path = Path.Combine("folder1-renamed-source", "subfolder1", "file1.txt"),
            Action = FileSystemEntryAction.Rename,
            Properties = new FileSystemEntryChangeProperties
            {
                RenameProps = new RenameProperties
                {
                    Name = "file1-renamed-source.txt"
                }
            }
        },
        new FileSystemEntryChange
        {
            Timestamp = new DateTime(2024, 4, 11, 15, 0, 6),
            Path = Path.Combine("folder1-renamed-source", "subfolder1", "file2.csv"),
            Action = FileSystemEntryAction.Change,
            Properties = new FileSystemEntryChangeProperties
            {
                ChangeProps = new ChangeProperties
                {
                    LastWriteTime = new DateTime(2024, 4, 9, 19, 47, 36),
                    Length = 3318
                }
            }
        },
        new FileSystemEntryChange
        {
            Timestamp = new DateTime(2024, 4, 11, 15, 0, 7),
            Path = Path.Combine("folder1-renamed-source", "file3"),
            Action = FileSystemEntryAction.Delete
        }
    };

    public Test1() : base(_changes) { }
}
