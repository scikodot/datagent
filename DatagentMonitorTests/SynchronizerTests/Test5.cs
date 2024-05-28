using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests.SynchronizerTests;

public class Test5 : TestBase
{
    private static readonly List<EntryChange> _changes = new()
    {
        new EntryChange(
            new DateTime(2024, 5, 2, 13, 56, 0),
            "file1", 
            FileSystemEntryType.File, FileSystemEntryAction.Rename, 
            new RenameProperties("file1-renamed-source"), null),

        new EntryChange(
            new DateTime(2024, 5, 2, 13, 56, 1),
            "file1", 
            FileSystemEntryType.File, FileSystemEntryAction.Create, 
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(1990, 1, 1, 0, 0, 0),
                Length = 256
            })
    };

    public Test5() : base(_changes) { }
}
