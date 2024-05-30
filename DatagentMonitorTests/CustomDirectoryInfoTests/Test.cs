using DatagentMonitor;
using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests.CustomDirectoryInfoTests;

public class Test : TestBase
{
    private static readonly List<EntryChange> _changes = new()
    {
        new EntryChange(
            null, 
            Path.Combine("folder1", "subfolder1", "ssubfolder1"), 
            EntryType.Directory, EntryAction.Create, 
            null, null),

        new EntryChange(
            null, 
            "folder1", 
            EntryType.Directory, EntryAction.Rename, 
            new RenameProperties("folder1-renamed"), null),

        new EntryChange(
            null, 
            "folder2", 
            EntryType.Directory, EntryAction.Delete, 
            null, null),

        new EntryChange(
            null,
            "file5.xlsx", 
            EntryType.File, EntryAction.Create, 
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2007, 7, 7),
                Length = 777
            }),

        new EntryChange(
            null,
            "file5.xlsx", 
            EntryType.File, EntryAction.Rename, 
            new RenameProperties("file5-renamed.xlsx"), null),

        new EntryChange(
            null, 
            Path.Combine("folder1-renamed", "subfolder1", "file1.txt"), 
            EntryType.File, EntryAction.Rename, 
            new RenameProperties("file1-renamed.txt"), null),

        new EntryChange(
            null,
            Path.Combine("folder1-renamed", "subfolder1", "file2.csv"), 
            EntryType.File, EntryAction.Change, 
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 4, 9, 19, 47, 36),
                Length = 7331
            }),

        new EntryChange(
            null, 
            Path.Combine("folder1-renamed", "file3"), 
            EntryType.File, EntryAction.Delete, 
            null, null)
    };

    private readonly SourceIndex _index;

    public Test()
    {
        _index = new SourceIndex(Path.Combine(DataPath, "index.txt"));
    }

    [Fact]
    public void TestMergeChanges()
    {
        _index.MergeChanges(_changes);
        _index.Serialize(out var actual);
        Assert.Equal(Config["Source"], actual);
    }
}
