using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests.SynchronizerTests;

public class Test_RenameFolderWithChanges : TestBase
{
    protected override IEnumerable<EntryChange> Changes => new List<EntryChange>
    {
        new EntryChange(
            new DateTime(2024, 4, 26, 19, 40, 0),
            Path.Combine("folder1", "file1"), 
            EntryType.File, EntryAction.Rename, 
            new RenameProperties("file1-renamed-source-1"), null),

        new EntryChange(
            new DateTime(2024, 4, 26, 19, 40, 1),
            Path.Combine("folder1", "file2"), 
            EntryType.File, EntryAction.Change, 
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 4, 26, 19, 40, 1),
                Length = 3345
            }),

        new EntryChange(
            new DateTime(2024, 4, 26, 20, 0, 0),
            "folder1", 
            EntryType.Directory, EntryAction.Rename, 
            new RenameProperties("folder1-renamed-source-1"), null),

        new EntryChange(
            new DateTime(2024, 4, 26, 20, 40, 0),
            Path.Combine("folder1-renamed-source-1", "file1-renamed-source-1"), 
            EntryType.File, EntryAction.Rename, 
            new RenameProperties("file1-renamed-source-2"), null),

        new EntryChange(
            new DateTime(2024, 4, 26, 20, 40, 1),
            Path.Combine("folder1-renamed-source-1", "file2"), 
            EntryType.File, EntryAction.Change, 
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 4, 26, 20, 40, 1),
                Length = 4345
            }),

        new EntryChange(
            new DateTime(2024, 4, 26, 21, 0, 0),
            "folder1-renamed-source-1", 
            EntryType.Directory, EntryAction.Rename, 
            new RenameProperties("folder1-renamed-source-2"), null),

        new EntryChange(
            new DateTime(2024, 4, 26, 21, 40, 0),
            Path.Combine("folder1-renamed-source-2", "file1-renamed-source-2"), 
            EntryType.File, EntryAction.Rename, 
            new RenameProperties("file1-renamed-source-3"), null),

        new EntryChange(
            new DateTime(2024, 4, 26, 21, 40, 1),
            Path.Combine("folder1-renamed-source-2", "file2"), 
            EntryType.File, EntryAction.Change, 
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 4, 26, 21, 40, 1),
                Length = 5345
            })
    };

    protected override DateTime? LastSyncTime => null;
}
