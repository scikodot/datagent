using DatagentMonitor;
using DatagentMonitor.FileSystem;
using DatagentMonitor.Synchronization;

namespace DatagentMonitorTests.SourceIndexTests.Test;

public class DateTimeProviderFixture : DateTimeProviderFixtureAbstract
{
    public override IDateTimeProvider DateTimeProvider => DateTimeProviderFactory.FromDateTime(new DateTime(2024, 4, 8));
}

public class Test : TestBase, IClassFixture<DirectoryFixture>, IClassFixture<DateTimeProviderFixture>
{
    private static readonly List<EntryChange> _changes = new()
    {
        new EntryChange(
            new DateTime(2024, 4, 7, 3, 0, 0), 
            Path.Combine("folder1", "subfolder1", "ssubfolder1"), 
            EntryType.Directory, EntryAction.Create, 
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 4, 7, 3, 0, 0)
            }),

        new EntryChange(
            new DateTime(2024, 4, 7, 4, 0, 0), 
            "folder1", 
            EntryType.Directory, EntryAction.Rename, 
            new RenameProperties("folder1-renamed"), null),

        new EntryChange(
            new DateTime(2024, 4, 7, 5, 0, 0), 
            "folder2", 
            EntryType.Directory, EntryAction.Delete, 
            null, null),

        new EntryChange(
            new DateTime(2024, 4, 7, 6, 0, 0),
            "file5.xlsx", 
            EntryType.File, EntryAction.Create, 
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2007, 7, 7),
                Length = 777
            }),

        new EntryChange(
            new DateTime(2024, 4, 7, 7, 0, 0),
            "file5.xlsx", 
            EntryType.File, EntryAction.Rename, 
            new RenameProperties("file5-renamed.xlsx"), null),

        new EntryChange(
            new DateTime(2024, 4, 7, 8, 0, 0), 
            Path.Combine("folder1-renamed", "subfolder1", "file1.txt"), 
            EntryType.File, EntryAction.Rename, 
            new RenameProperties("file1-renamed.txt"), null),

        new EntryChange(
            new DateTime(2024, 4, 7, 9, 0, 0),
            Path.Combine("folder1-renamed", "subfolder1", "file2.csv"), 
            EntryType.File, EntryAction.Change, 
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 4, 7, 9, 0, 0),
                Length = 250
            }),

        new EntryChange(
            new DateTime(2024, 4, 7, 10, 0, 0), 
            Path.Combine("folder1-renamed", "file3"), 
            EntryType.File, EntryAction.Delete, 
            null, null)
    };

    private readonly SourceIndex _index;

    public Test(DirectoryFixture directoryFixture, DateTimeProviderFixture dateTimeProviderFixture)
    {
        var source = directoryFixture.CreateTempDirectory(GetTempDirectoryName("source"));
        var path = Path.Combine(source.FullName, "index.txt");
        File.WriteAllText(path, Config["Index"]);
        _index = new SourceIndex(path);
    }

    [Fact]
    public void Test_MergeChanges()
    {
        _index.MergeChanges(_changes);
        _index.Serialize(out var actual);
        Assert.Equal(Config["Source"], actual);
    }
}
