using DatagentMonitor;
using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests.CustomDirectoryInfoTests;

public class Test1 : TestBaseCommon
{
    private static readonly List<EntryChange> _changes = new()
    {
        new EntryChange(
            null, 
            Path.Combine("folder1", "subfolder1", "ssubfolder1"), 
            FileSystemEntryType.Directory, FileSystemEntryAction.Create, 
            null, null),

        new EntryChange(
            null, 
            "folder1", 
            FileSystemEntryType.Directory, FileSystemEntryAction.Rename, 
            new RenameProperties("folder1-renamed"), null),

        new EntryChange(
            null, 
            "folder2", 
            FileSystemEntryType.Directory, FileSystemEntryAction.Delete, 
            null, null),

        new EntryChange(
            null,
            "file5.xlsx", 
            FileSystemEntryType.File, FileSystemEntryAction.Create, 
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2007, 7, 7),
                Length = 777
            }),

        new EntryChange(
            null,
            "file5.xlsx", 
            FileSystemEntryType.File, FileSystemEntryAction.Rename, 
            new RenameProperties("file5-renamed.xlsx"), null),

        new EntryChange(
            null, 
            Path.Combine("folder1-renamed", "subfolder1", "file1.txt"), 
            FileSystemEntryType.File, FileSystemEntryAction.Rename, 
            new RenameProperties("file1-renamed.txt"), null),

        new EntryChange(
            null,
            Path.Combine("folder1-renamed", "subfolder1", "file2.csv"), 
            FileSystemEntryType.File, FileSystemEntryAction.Change, 
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 4, 9, 19, 47, 36),
                Length = 7331
            }),

        new EntryChange(
            null, 
            Path.Combine("folder1-renamed", "file3"), 
            FileSystemEntryType.File, FileSystemEntryAction.Delete, 
            null, null)
    };

    private readonly SourceIndex _index;

    public Test1()
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
