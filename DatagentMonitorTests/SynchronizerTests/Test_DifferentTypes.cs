using DatagentMonitor;
using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests.SynchronizerTests.Test_DifferentTypes;

public class DateTimeProviderFixture : DateTimeProviderFixtureAbstract
{
    public override IDateTimeProvider DateTimeProvider => DateTimeProviderFactory.FromDateTime(new DateTime(2024, 6, 3));
}

public class Test_DifferentTypes : TestBase, IClassFixture<DirectoryFixture>, IClassFixture<DateTimeProviderFixture>
{
    protected override IEnumerable<EntryChange> Changes => new List<EntryChange>
    {
        // Directory Create VS File Create
        new EntryChange(
            new DateTime(2024, 6, 2, 7, 0, 0), 
            "entry1", 
            EntryType.Directory, EntryAction.Create, 
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 6, 2, 7, 0, 0)
            }),

        new EntryChange(
            new DateTime(2024, 6, 2, 7, 0, 0), 
            Path.Combine("entry1", "subentry1"), 
            EntryType.Directory, EntryAction.Create, 
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 6, 2, 7, 0, 0)
            }),

        new EntryChange(
            new DateTime(2024, 6, 2, 7, 0, 0), 
            Path.Combine("entry1", "subentry2"), 
            EntryType.File, EntryAction.Create, 
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 6, 2, 7, 0, 0),
                Length = 700
            }),

        // Directory Create VS File Change
        new EntryChange(
            new DateTime(2024, 6, 2, 7, 30, 0), 
            Path.Combine("folder1", "subfolder1", "file1"), 
            EntryType.File, EntryAction.Delete, 
            null, null), 

        new EntryChange(
            new DateTime(2024, 6, 2, 8, 0, 0), 
            Path.Combine("folder1", "subfolder1", "file1"), 
            EntryType.Directory, EntryAction.Create, 
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 6, 2, 8, 0, 0)
            }), 

        new EntryChange(
            new DateTime(2024, 6, 2, 8, 0, 0), 
            Path.Combine("folder1", "subfolder1", "file1", "subentry1"), 
            EntryType.File, EntryAction.Create, 
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 6, 2, 8, 0, 0),
                Length = 800
            }),

        // Directory Rename VS File Create
        new EntryChange(
            new DateTime(2024, 6, 2, 9, 0, 0), 
            Path.Combine("folder1", "subfolder2"), 
            EntryType.Directory, EntryAction.Rename, 
            new RenameProperties("subfolder2-renamed-source"), null),

        // Directory null VS File Create
        new EntryChange(
            new DateTime(2024, 6, 2, 10, 30, 0), 
            Path.Combine("folder2", "file10"), 
            EntryType.File, EntryAction.Create, 
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 6, 2, 10, 30, 0),
                Length = 1000
            }),

        // File Create VS Directory Create
        new EntryChange(
            new DateTime(2024, 6, 2, 11, 30, 0), 
            Path.Combine("folder1", "entry2"), 
            EntryType.File, EntryAction.Create, 
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 6, 2, 11, 30, 0),
                Length = 1100
            }),

        // File Rename VS Directory Create
        new EntryChange(
            new DateTime(2024, 6, 2, 12, 30, 0), 
            "file5", 
            EntryType.File, EntryAction.Rename, 
            new RenameProperties("file5-renamed-source"), null),

        // File Change VS Directory Create
        new EntryChange(
            new DateTime(2024, 6, 2, 13, 30, 0), 
            "file6", EntryType.File, EntryAction.Change, 
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 6, 2, 13, 30, 0),
                Length = 1300
            })
    };

    protected override DateTime? LastSyncTime => null;

    public Test_DifferentTypes(DirectoryFixture directoryFixture) : base(directoryFixture) { }
}
