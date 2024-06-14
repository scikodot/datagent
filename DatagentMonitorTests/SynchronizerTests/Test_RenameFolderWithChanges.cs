using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests.SynchronizerTests;

public class Test_RenameFolderWithChanges : TestBase, IClassFixture<DirectoryFixture>
{
    protected override IEnumerable<EntryChange> Changes => new List<EntryChange>
    {
        new EntryChange(
            new DateTime(2024, 4, 26, 3, 20, 0),
            Path.Combine("folder1", "file1"), 
            EntryType.File, EntryAction.Rename, 
            new RenameProperties("file1-renamed-source-1"), null),

        new EntryChange(
            new DateTime(2024, 4, 26, 3, 40, 0),
            Path.Combine("folder1", "file2"), 
            EntryType.File, EntryAction.Change, 
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 4, 26, 3, 40, 0),
                Length = 1200
            }),

        new EntryChange(
            new DateTime(2024, 4, 26, 4, 0, 0),
            "folder1", 
            EntryType.Directory, EntryAction.Rename, 
            new RenameProperties("folder1-renamed-source-1"), null),

        new EntryChange(
            new DateTime(2024, 4, 26, 4, 20, 0),
            Path.Combine("folder1-renamed-source-1", "file1-renamed-source-1"), 
            EntryType.File, EntryAction.Rename, 
            new RenameProperties("file1-renamed-source-2"), null),

        new EntryChange(
            new DateTime(2024, 4, 26, 4, 40, 0),
            Path.Combine("folder1-renamed-source-1", "file2"), 
            EntryType.File, EntryAction.Change, 
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 4, 26, 4, 40, 0),
                Length = 2200
            }),

        new EntryChange(
            new DateTime(2024, 4, 26, 5, 0, 0),
            "folder1-renamed-source-1", 
            EntryType.Directory, EntryAction.Rename, 
            new RenameProperties("folder1-renamed-source-2"), null),

        new EntryChange(
            new DateTime(2024, 4, 26, 5, 20, 0),
            Path.Combine("folder1-renamed-source-2", "file1-renamed-source-2"), 
            EntryType.File, EntryAction.Rename, 
            new RenameProperties("file1-renamed-source-3"), null),

        new EntryChange(
            new DateTime(2024, 4, 26, 5, 40, 0),
            Path.Combine("folder1-renamed-source-2", "file2"), 
            EntryType.File, EntryAction.Change, 
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 4, 26, 5, 40, 0),
                Length = 3200
            })
    };

    protected override DateTime? LastSyncTime => null;

    public Test_RenameFolderWithChanges(DirectoryFixture df) : base(df) { }
}
