using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests.SynchronizerTests;

public class Test_DeleteFolderWithChanges : TestBase
{
    protected override IEnumerable<EntryChange> Changes => new List<EntryChange>
    {
        new EntryChange(
            new DateTime(2024, 4, 19, 13, 58, 0),
            Path.Combine("folder1", "file1"), 
            EntryType.File, EntryAction.Rename, 
            new RenameProperties("file1-renamed-source"), null),

        new EntryChange(
            new DateTime(2024, 4, 19, 13, 58, 1),
            Path.Combine("folder1", "file2"), 
            EntryType.File, EntryAction.Change, 
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 4, 19, 13, 58, 1),
                Length = 5432
            })
    };

    protected override DateTime? LastSyncTime => null;
}
