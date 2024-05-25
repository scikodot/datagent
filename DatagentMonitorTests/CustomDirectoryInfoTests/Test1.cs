using DatagentMonitor;
using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests.CustomDirectoryInfoTests;

public class Test1 : TestBaseCommon
{
    private static readonly List<EntryChange> _changes = new()
    {
        new EntryChange(
            Path.Combine("folder1", "subfolder1", "ssubfolder1"), 
            FileSystemEntryType.Directory, 
            FileSystemEntryAction.Create),
        new EntryChange(
            "folder1", 
            FileSystemEntryType.Directory, 
            FileSystemEntryAction.Rename)
        {
            RenameProperties = new RenameProperties("folder1-renamed")
        },
        new EntryChange(
            "folder2", 
            FileSystemEntryType.Directory, 
            FileSystemEntryAction.Delete),
        new EntryChange(
            "file5.xlsx", 
            FileSystemEntryType.File, 
            FileSystemEntryAction.Create)
        {
            ChangeProperties = new ChangeProperties
            {
                LastWriteTime = new DateTime(2007, 7, 7),
                Length = 777
            }
        },
        new EntryChange(
            "file5.xlsx", 
            FileSystemEntryType.File, 
            FileSystemEntryAction.Rename)
        {
            Action = FileSystemEntryAction.Rename,
            RenameProperties = new RenameProperties("file5-renamed.xlsx")
        },
        new EntryChange(
            Path.Combine("folder1-renamed", "subfolder1", "file1.txt"), 
            FileSystemEntryType.File, 
            FileSystemEntryAction.Rename)
        {
            RenameProperties = new RenameProperties("file1-renamed.txt")
        },
        new EntryChange(
            Path.Combine("folder1-renamed", "subfolder1", "file2.csv"), 
            FileSystemEntryType.File, 
            FileSystemEntryAction.Change)
        {
            ChangeProperties = new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 4, 9, 19, 47, 36),
                Length = 7331
            }
        },
        new EntryChange(
            Path.Combine("folder1-renamed", "file3"), 
            FileSystemEntryType.File, 
            FileSystemEntryAction.Delete)
    };

    private static readonly SourceIndex _index;
    private static readonly string _source;

    static Test1()
    {
        var dataPath = GetTestDataPath(typeof(Test1));
        _index = new SourceIndex(Path.Combine(dataPath, "index.txt"));
        _source = File.ReadAllText(Path.Combine(dataPath, "source.txt"));
    }

    [Fact]
    public void TestMergeChanges()
    {
        _index.MergeChanges(_changes);
        _index.Serialize(out var actual);
        Assert.Equal(_source, actual.ToString());
    }
}
