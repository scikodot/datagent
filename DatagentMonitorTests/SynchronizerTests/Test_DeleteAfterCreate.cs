using DatagentMonitor;
using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests.SynchronizerTests.Test_DeleteAfterCreate;

public class DateTimeProviderFixture : DateTimeProviderFixtureAbstract
{
    public override IDateTimeProvider DateTimeProvider => DateTimeProviderFactory.FromDateTime(new DateTime(2024, 4, 20));
}

public class Test_DeleteAfterCreate : TestBase, IClassFixture<DirectoryFixture>, IClassFixture<DateTimeProviderFixture>
{
    protected override IEnumerable<EntryChange> Changes => new List<EntryChange>
    {
        new EntryChange(
            new DateTime(2024, 4, 19, 4, 0, 0),
            "folder2", 
            EntryType.Directory, EntryAction.Create, 
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 4, 19, 4, 0, 0)
            }),

        new EntryChange(
            new DateTime(2024, 4, 19, 4, 0, 0),
            Path.Combine("folder2", "file4.ffx"), 
            EntryType.File, EntryAction.Create, 
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 4, 19, 4, 0, 0),
                Length = 400
            }),

        new EntryChange(
            new DateTime(2024, 4, 19, 4, 0, 0),
            Path.Combine("folder2", "file5.tex"), 
            EntryType.File, EntryAction.Create, 
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 4, 19, 4, 0, 0),
                Length = 500
            }),

        new EntryChange(
            new DateTime(2024, 4, 19, 5, 0, 0),
            Path.Combine("folder1", "file3.sfx"), 
            EntryType.File, EntryAction.Create, 
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 4, 19, 5, 0, 0),
                Length = 1300
            }),

        new EntryChange(
            new DateTime(2024, 4, 19, 6, 0, 0),
            "folder2", 
            EntryType.Directory, EntryAction.Delete, 
            null, null),

        new EntryChange(
            new DateTime(2024, 4, 19, 7, 0, 0),
            Path.Combine("folder1", "file3.sfx"), 
            EntryType.File, EntryAction.Delete, 
            null, null)
    };

    protected override DateTime? LastSyncTime => null;

    public Test_DeleteAfterCreate(DirectoryFixture directoryFixture) : base(directoryFixture) { }
}
