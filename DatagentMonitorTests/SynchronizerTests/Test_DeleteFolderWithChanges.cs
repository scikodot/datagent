using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests.SynchronizerTests;

public class Test_DeleteFolderWithChanges : TestBase, IClassFixture<DirectoryFixture>
{
    protected override IEnumerable<EntryChange> Changes => new List<EntryChange>
    {
        new EntryChange(
            new DateTime(2024, 4, 19, 4, 0, 0),
            Path.Combine("folder1", "file1"), 
            EntryType.File, EntryAction.Rename, 
            new RenameProperties("file1-renamed-source"), null),

        new EntryChange(
            new DateTime(2024, 4, 19, 5, 0, 0),
            Path.Combine("folder1", "file2"), 
            EntryType.File, EntryAction.Change, 
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 4, 19, 5, 0, 0),
                Length = 250
            })
    };

    protected override DateTime? LastSyncTime => null;

    public Test_DeleteFolderWithChanges(DirectoryFixture df) : base(df) { }
}
