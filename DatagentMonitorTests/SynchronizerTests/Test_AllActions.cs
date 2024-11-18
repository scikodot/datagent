using DatagentMonitor;
using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests.SynchronizerTests.Test_AllActions;

public class DateTimeProviderFixture : DateTimeProviderFixtureAbstract
{
    public override IDateTimeProvider DateTimeProvider => DateTimeProviderFactory.FromDateTime(new DateTime(2024, 04, 12));
}

public class Test_AllActions : TestBase, IClassFixture<DirectoryFixture>, IClassFixture<DateTimeProviderFixture>
{
    protected override IEnumerable<EntryChange> Changes => new List<EntryChange>
    {
        new EntryChange(
            new DateTime(2024, 4, 11, 0, 0, 0),
            Path.Combine("folder1", "subfolder1", "ssubfolder1"), 
            EntryType.Directory, EntryAction.Create, 
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 4, 11, 0, 0, 0)
            }),

        new EntryChange(
            new DateTime(2024, 4, 11, 1, 0, 0),
            "folder1", 
            EntryType.Directory, EntryAction.Rename, 
            new RenameProperties("folder1-renamed-source"), null),

        new EntryChange(
            new DateTime(2024, 4, 11, 2, 0, 0),
            "folder2", 
            EntryType.Directory, EntryAction.Delete, 
            null, null),

        new EntryChange(
            new DateTime(2024, 4, 11, 3, 0, 0),
            "file5.xlsx", 
            EntryType.File, EntryAction.Create, 
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2007, 7, 7),
                Length = 777
            }),

        new EntryChange(
            new DateTime(2024, 4, 11, 4, 0, 0),
            "file5.xlsx", 
            EntryType.File, EntryAction.Rename, 
            new RenameProperties("file5-renamed-source.xlsx"), null),

        new EntryChange(
            new DateTime(2024, 4, 11, 5, 0, 0),
            Path.Combine("folder1-renamed-source", "subfolder1", "file1.txt"), 
            EntryType.File, EntryAction.Rename, 
            new RenameProperties("file1-renamed-source.txt"), null),

        new EntryChange(
            new DateTime(2024, 4, 11, 6, 0, 0),
            Path.Combine("folder1-renamed-source", "subfolder1", "file2.csv"), 
            EntryType.File, EntryAction.Change, 
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 4, 11, 6, 0, 0),
                Length = 250
            }),

        new EntryChange(
            new DateTime(2024, 4, 11, 7, 0, 0),
            Path.Combine("folder1-renamed-source", "file3"), 
            EntryType.File, EntryAction.Delete, 
            null, null)
    };

    protected override DateTime? LastSyncTime => null;        

    public Test_AllActions(DirectoryFixture directoryFixture, DateTimeProviderFixture dateTimeProviderFixture) : 
        base(directoryFixture) { }
}
