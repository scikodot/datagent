using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests.SynchronizerTests;

public class Test_CreateAfterRename : TestBase
{
    protected override IEnumerable<EntryChange> Changes => new List<EntryChange>
    {
        new EntryChange(
            new DateTime(2024, 5, 2, 4, 0, 0),
            "file1", 
            EntryType.File, EntryAction.Rename, 
            new RenameProperties("file1-renamed-source"), null),

        new EntryChange(
            new DateTime(2024, 5, 2, 5, 0, 0),
            "file1", 
            EntryType.File, EntryAction.Create, 
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(1990, 1, 1, 0, 0, 0),
                Length = 2100
            })
    };

    protected override DateTime? LastSyncTime => null;
}
