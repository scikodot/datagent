using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests.SynchronizerTests;

// Test all actions
public class Test1 : TestBase
{
    private static readonly List<FileSystemEntryChange> _changes = new()
    {
        new FileSystemEntryChange(
            Path.Combine("folder1", "subfolder1", "ssubfolder1") + Path.DirectorySeparatorChar, 
            FileSystemEntryAction.Create)
        {
            Timestamp = new DateTime(2024, 4, 11, 15, 0, 0)
        },
        new FileSystemEntryChange(
            "folder1" + Path.DirectorySeparatorChar, 
            FileSystemEntryAction.Rename)
        {
            Timestamp = new DateTime(2024, 4, 11, 15, 0, 1),
            Properties = new FileSystemEntryChangeProperties
            {
                RenameProps = new RenameProperties
                {
                    Name = "folder1-renamed-source"
                }
            }
        },
        new FileSystemEntryChange(
            "folder2" + Path.DirectorySeparatorChar, 
            FileSystemEntryAction.Delete)
        {
            Timestamp = new DateTime(2024, 4, 11, 15, 0, 2)
        },
        new FileSystemEntryChange(
            "file5.xlsx", 
            FileSystemEntryAction.Create)
        {
            Timestamp = new DateTime(2024, 4, 11, 15, 0, 3),
            Properties = new FileSystemEntryChangeProperties
            {
                ChangeProps = new ChangeProperties
                {
                    LastWriteTime = new DateTime(2007, 7, 7),
                    Length = 888
                }
            }
        },
        new FileSystemEntryChange(
            "file5.xlsx", 
            FileSystemEntryAction.Rename)
        {
            Timestamp = new DateTime(2024, 4, 11, 15, 0, 4),
            Properties = new FileSystemEntryChangeProperties
            {
                RenameProps = new RenameProperties
                {
                    Name = "file5-renamed-source.xlsx"
                }
            }
        },
        new FileSystemEntryChange(
            Path.Combine("folder1-renamed-source", "subfolder1", "file1.txt"), 
            FileSystemEntryAction.Rename)
        {
            Timestamp = new DateTime(2024, 4, 11, 15, 0, 5),
            Properties = new FileSystemEntryChangeProperties
            {
                RenameProps = new RenameProperties
                {
                    Name = "file1-renamed-source.txt"
                }
            }
        },
        new FileSystemEntryChange(
            Path.Combine("folder1-renamed-source", "subfolder1", "file2.csv"), 
            FileSystemEntryAction.Change)
        {
            Timestamp = new DateTime(2024, 4, 11, 15, 0, 6),
            Properties = new FileSystemEntryChangeProperties
            {
                ChangeProps = new ChangeProperties
                {
                    LastWriteTime = new DateTime(2024, 4, 9, 19, 47, 36),
                    Length = 3318
                }
            }
        },
        new FileSystemEntryChange(
            Path.Combine("folder1-renamed-source", "file3"), 
            FileSystemEntryAction.Delete)
        {
            Timestamp = new DateTime(2024, 4, 11, 15, 0, 7),
        }
    };

    public Test1() : base(_changes) { }
}
