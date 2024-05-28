using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests.SynchronizerTests;

public class Test3 : TestBase
{
    private static readonly List<EntryChange> _changes = new()
    {
        new EntryChange(
            new DateTime(2024, 4, 19, 13, 58, 0),
            Path.Combine("folder1", "file1"), 
            FileSystemEntryType.File, FileSystemEntryAction.Rename, 
            new RenameProperties("file1-renamed-source"), null),

        new EntryChange(
            new DateTime(2024, 4, 19, 13, 58, 1),
            Path.Combine("folder1", "file2"), 
            FileSystemEntryType.File, FileSystemEntryAction.Change, 
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 4, 19, 13, 58, 1),
                Length = 5432
            })
    };

    public Test3() : base(_changes) { }
}
