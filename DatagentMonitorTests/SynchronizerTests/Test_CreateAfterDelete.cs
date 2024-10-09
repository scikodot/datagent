using DatagentMonitor;
using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests.SynchronizerTests.Test_CreateAfterDelete;

public class DateTimeProviderFixture : DateTimeProviderFixtureAbstract
{
    public override IDateTimeProvider DateTimeProvider => DateTimeProviderFactory.FromDateTime(new DateTime(2024, 10, 12));
}

public class Test_CreateAfterDelete : TestBase, IClassFixture<DirectoryFixture>, IClassFixture<DateTimeProviderFixture>
{
    protected override IEnumerable<EntryChange> Changes => new List<EntryChange>
    {
        // Standalone Directory Delete
        new EntryChange(
            new DateTime(2024, 10, 10, 0, 0, 0),
            Path.Combine("folder1", "subfolder2", "file1"),
            EntryType.File, EntryAction.Delete,
            null, null),

        new EntryChange(
            new DateTime(2024, 10, 10, 0, 0, 0),
            Path.Combine("folder1", "subfolder2", "file2"),
            EntryType.File, EntryAction.Delete,
            null, null),

        new EntryChange(
            new DateTime(2024, 10, 10, 0, 0, 0),
            Path.Combine("folder1", "subfolder2"),
            EntryType.Directory, EntryAction.Delete,
            null, null),

        // Directory Create after Directory Delete
        new EntryChange(
            new DateTime(2024, 10, 10, 1, 0, 0), 
            Path.Combine("folder1", "subfolder1", "file1"), 
            EntryType.File, EntryAction.Delete, 
            null, null),

        new EntryChange(
            new DateTime(2024, 10, 10, 1, 0, 0),
            Path.Combine("folder1", "subfolder1", "file2"),
            EntryType.File, EntryAction.Delete,
            null, null),

        new EntryChange(
            new DateTime(2024, 10, 10, 1, 0, 0),
            Path.Combine("folder1", "subfolder1"),
            EntryType.Directory, EntryAction.Delete,
            null, null),

        new EntryChange(
            new DateTime(2024, 10, 10, 2, 0, 0),
            Path.Combine("folder1", "subfolder1"),
            EntryType.Directory, EntryAction.Create,
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 10, 10, 2, 0, 0)
            }),

        new EntryChange(
            new DateTime(2024, 10, 10, 2, 0, 0),
            Path.Combine("folder1", "subfolder1", "file1"),
            EntryType.File, EntryAction.Create,
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 10, 10, 2, 0, 0), 
                Length = 1000
            }),

        new EntryChange(
            new DateTime(2024, 10, 10, 2, 0, 0),
            Path.Combine("folder1", "subfolder1", "file2.txt"),
            EntryType.File, EntryAction.Create,
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 10, 10, 2, 0, 0),
                Length = 2000
            }),

        // Directory Create after File Delete
        new EntryChange(
            new DateTime(2024, 10, 10, 3, 0, 0),
            Path.Combine("folder1", "file3"),
            EntryType.File, EntryAction.Delete,
            null, null),

        new EntryChange(
            new DateTime(2024, 10, 10, 4, 0, 0),
            Path.Combine("folder1", "file3"),
            EntryType.Directory, EntryAction.Create,
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 10, 10, 4, 0, 0)
            }),

        // File Create after Directory Delete
        new EntryChange(
            new DateTime(2024, 10, 10, 5, 0, 0),
            "folder2",
            EntryType.Directory, EntryAction.Delete,
            null, null),

        new EntryChange(
            new DateTime(2024, 10, 10, 6, 0, 0),
            "folder2",
            EntryType.File, EntryAction.Create,
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 10, 10, 6, 0, 0),
                Length = 2100
            }),

        // File Create after File Delete
        new EntryChange(
            new DateTime(2024, 10, 10, 7, 0, 0),
            "file5",
            EntryType.File, EntryAction.Delete,
            null, null),

        new EntryChange(
            new DateTime(2024, 10, 10, 8, 0, 0),
            "file5",
            EntryType.File, EntryAction.Create,
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 10, 10, 8, 0, 0),
                Length = 5000
            }),
    };

    protected override DateTime? LastSyncTime => null;

    public Test_CreateAfterDelete(DirectoryFixture directoryFixture) : base(directoryFixture) { }
}
