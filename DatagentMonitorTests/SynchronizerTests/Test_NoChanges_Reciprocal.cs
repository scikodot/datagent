using DatagentMonitor;
using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests.SynchronizerTests.Test_NoChanges_Reciprocal;

public class DateTimeProviderFixture : DateTimeProviderFixtureAbstract
{
    public override IDateTimeProvider DateTimeProvider => DateTimeProviderFactory.FromDateTime(new DateTime(2024, 5, 30));
}

/* This test represents a case when there are events in the database, 
 * but they are reciprocal to each other, so only directories' timestamps 
 * of both source and target can differ.
 */
public class Test_NoChanges_Reciprocal : TestBase, IClassFixture<DirectoryFixture>, IClassFixture<DateTimeProviderFixture>
{
    protected override IEnumerable<EntryChange> Changes => new List<EntryChange>
    {
        // Original changes
        new EntryChange(
            new DateTime(2024, 5, 29, 0, 5, 0),
            Path.Combine("folder1", "subfolder1", "temp_folder"),
            EntryType.Directory, EntryAction.Create,
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 5, 29, 0, 5, 0)
            }),

        new EntryChange(
            new DateTime(2024, 5, 29, 0, 5, 0),
            Path.Combine("folder1", "subfolder1", "temp_folder", "temp_file1"),
            EntryType.File, EntryAction.Create,
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 5, 29, 0, 5, 0),
                Length = 500
            }),

        new EntryChange(
            new DateTime(2024, 5, 29, 0, 6, 0),
            Path.Combine("folder1", "subfolder1", "temp_file2.tmp"),
            EntryType.File, EntryAction.Create,
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 5, 29, 0, 6, 0),
                Length = 600
            }),

        new EntryChange(
            new DateTime(2024, 5, 29, 0, 7, 0),
            "folder1", EntryType.Directory, EntryAction.Rename,
            new RenameProperties("folder1-renamed"), null),

        new EntryChange(
            new DateTime(2024, 5, 29, 0, 8, 0), 
            "file4", 
            EntryType.File, EntryAction.Rename, 
            new RenameProperties("file4-renamed"), null),

        // Reciprocal changes
        new EntryChange(
            new DateTime(2024, 5, 29, 5, 0, 0),
            Path.Combine("folder1-renamed", "subfolder1", "temp_folder"),
            EntryType.Directory, EntryAction.Delete,
            null, null),

        new EntryChange(
            new DateTime(2024, 5, 29, 6, 0, 0), 
            Path.Combine("folder1-renamed", "subfolder1", "temp_file2.tmp"), 
            EntryType.File, EntryAction.Delete, 
            null, null), 

        new EntryChange(
            new DateTime(2024, 5, 29, 7, 0, 0), 
            "folder1-renamed", 
            EntryType.Directory, EntryAction.Rename, 
            new RenameProperties("folder1"), null), 

        new EntryChange(
            new DateTime(2024, 5, 29, 8, 0, 0), 
            "file4-renamed", 
            EntryType.File, EntryAction.Rename, 
            new RenameProperties("file4"), null)
    };

    protected override DateTime? LastSyncTime => null;

    public Test_NoChanges_Reciprocal(DirectoryFixture directoryFixture) : base(directoryFixture) { }
}
