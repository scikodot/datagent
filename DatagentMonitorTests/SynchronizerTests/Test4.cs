using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests.SynchronizerTests;

public class Test4 : TestBase
{
    private static readonly List<NamedEntryChange> _changes = new()
    {
        new NamedEntryChange(
            Path.Combine("folder1", "file1"), 
            FileSystemEntryAction.Rename)
        {
            Timestamp = new DateTime(2024, 4, 26, 19, 40, 0),
            RenameProperties = new RenameProperties("file1-renamed-source-1")
        },
        new NamedEntryChange(
            Path.Combine("folder1", "file2"), 
            FileSystemEntryAction.Change)
        {
            Timestamp = new DateTime(2024, 4, 26, 19, 40, 1),
            ChangeProperties = new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 4, 26, 19, 40, 1),
                Length = 3345
            }
        },
        new NamedEntryChange(
            "folder1" + Path.DirectorySeparatorChar, 
            FileSystemEntryAction.Rename)
        {
            Timestamp = new DateTime(2024, 4, 26, 20, 0, 0),
            RenameProperties = new RenameProperties("folder1-renamed-source-1")
        },
        new NamedEntryChange(
            Path.Combine("folder1-renamed-source-1", "file1-renamed-source-1"), 
            FileSystemEntryAction.Rename)
        {
            Timestamp = new DateTime(2024, 4, 26, 20, 40, 0),
            RenameProperties = new RenameProperties("file1-renamed-source-2")
        },
        new NamedEntryChange(
            Path.Combine("folder1-renamed-source-1", "file2"), 
            FileSystemEntryAction.Change)
        {
            Timestamp = new DateTime(2024, 4, 26, 20, 40, 1),
            ChangeProperties = new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 4, 26, 20, 40, 1),
                Length = 4345
            }
        },
        new NamedEntryChange(
            "folder1-renamed-source-1" + Path.DirectorySeparatorChar, 
            FileSystemEntryAction.Rename)
        {
            Timestamp = new DateTime(2024, 4, 26, 21, 0, 0),
            RenameProperties = new RenameProperties("folder1-renamed-source-2")
        },
        new NamedEntryChange(
            Path.Combine("folder1-renamed-source-2", "file1-renamed-source-2"), 
            FileSystemEntryAction.Rename)
        {
            Timestamp = new DateTime(2024, 4, 26, 21, 40, 0),
            RenameProperties = new RenameProperties("file1-renamed-source-3")
        },
        new NamedEntryChange(
            Path.Combine("folder1-renamed-source-2", "file2"), 
            FileSystemEntryAction.Change)
        {
            Timestamp = new DateTime(2024, 4, 26, 21, 40, 1),
            ChangeProperties = new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 4, 26, 21, 40, 1),
                Length = 5345
            }
        },
    };

    public Test4() : base(_changes) { }
}
