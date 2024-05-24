using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests.SynchronizerTests;

// Test all actions
public class Test1 : TestBase
{
    private static readonly List<EntryChange> _changes = new()
    {
        new EntryChange(
            Path.Combine("folder1", "subfolder1", "ssubfolder1"), 
            FileSystemEntryType.Directory, 
            FileSystemEntryAction.Create)
        {
            Timestamp = new DateTime(2024, 4, 11, 15, 0, 0)
        },
        new EntryChange(
            "folder1", 
            FileSystemEntryType.Directory, 
            FileSystemEntryAction.Rename)
        {
            Timestamp = new DateTime(2024, 4, 11, 15, 0, 1),
            RenameProperties = new RenameProperties("folder1-renamed-source")
        },
        new EntryChange(
            "folder2", 
            FileSystemEntryType.Directory, 
            FileSystemEntryAction.Delete)
        {
            Timestamp = new DateTime(2024, 4, 11, 15, 0, 2)
        },
        new EntryChange(
            "file5.xlsx", 
            FileSystemEntryType.File, 
            FileSystemEntryAction.Create)
        {
            Timestamp = new DateTime(2024, 4, 11, 15, 0, 3),
            ChangeProperties = new ChangeProperties
            {
                LastWriteTime = new DateTime(2007, 7, 7),
                Length = 888
            }
        },
        new EntryChange(
            "file5.xlsx", 
            FileSystemEntryType.File, 
            FileSystemEntryAction.Rename)
        {
            Timestamp = new DateTime(2024, 4, 11, 15, 0, 4),
            RenameProperties = new RenameProperties("file5-renamed-source.xlsx")
        },
        new EntryChange(
            Path.Combine("folder1-renamed-source", "subfolder1", "file1.txt"), 
            FileSystemEntryType.File, 
            FileSystemEntryAction.Rename)
        {
            Timestamp = new DateTime(2024, 4, 11, 15, 0, 5),
            RenameProperties = new RenameProperties("file1-renamed-source.txt")
        },
        new EntryChange(
            Path.Combine("folder1-renamed-source", "subfolder1", "file2.csv"), 
            FileSystemEntryType.File, 
            FileSystemEntryAction.Change)
        {
            Timestamp = new DateTime(2024, 4, 11, 15, 0, 6),
            ChangeProperties = new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 4, 9, 19, 47, 36),
                Length = 3318
            }
        },
        new EntryChange(
            Path.Combine("folder1-renamed-source", "file3"), 
            FileSystemEntryType.File, 
            FileSystemEntryAction.Delete)
        {
            Timestamp = new DateTime(2024, 4, 11, 15, 0, 7),
        }
    };

    public Test1() : base(_changes) { }
}
