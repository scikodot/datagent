using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests.CustomDirectoryInfoTests;

public class Test1 : TestBaseCommon
{
    private static readonly List<FileSystemEntryChange> _changes = new()
    {
        new FileSystemEntryChange
        {
            Path = Path.Combine("folder1", "subfolder1", "ssubfolder1") + Path.DirectorySeparatorChar,
            Action = FileSystemEntryAction.Create
        },
        new FileSystemEntryChange
        {
            Path = "folder1" + Path.DirectorySeparatorChar,
            Action = FileSystemEntryAction.Rename,
            Properties = new FileSystemEntryChangeProperties
            {
                RenameProps = new RenameProperties
                {
                    Name = "folder1-renamed"
                }
            }
        },
        new FileSystemEntryChange
        {
            Path = "folder2" + Path.DirectorySeparatorChar,
            Action = FileSystemEntryAction.Delete
        },
        new FileSystemEntryChange
        {
            Path = "file5.xlsx",
            Action = FileSystemEntryAction.Create,
            Properties = new FileSystemEntryChangeProperties
            {
                ChangeProps = new ChangeProperties
                {
                    LastWriteTime = new DateTime(2007, 7, 7),
                    Length = 777
                }
            }
        },
        new FileSystemEntryChange
        {
            Path = "file5.xlsx",
            Action = FileSystemEntryAction.Rename,
            Properties = new FileSystemEntryChangeProperties
            {
                RenameProps = new RenameProperties
                {
                    Name = "file5-renamed.xlsx"
                }
            }
        },
        new FileSystemEntryChange
        {
            Path = Path.Combine("folder1-renamed", "subfolder1", "file1.txt"),
            Action = FileSystemEntryAction.Rename,
            Properties = new FileSystemEntryChangeProperties
            {
                RenameProps = new RenameProperties
                {
                    Name = "file1-renamed.txt"
                }
            }
        },
        new FileSystemEntryChange
        {
            Path = Path.Combine("folder1-renamed", "subfolder1", "file2.csv"),
            Action = FileSystemEntryAction.Change,
            Properties = new FileSystemEntryChangeProperties
            {
                ChangeProps = new ChangeProperties
                {
                    LastWriteTime = new DateTime(2024, 4, 9, 19, 47, 36),
                    Length = 7331
                }
            }
        },
        new FileSystemEntryChange
        {
            Path = Path.Combine("folder1-renamed", "file3"),
            Action = FileSystemEntryAction.Delete
        }
    };

    private static readonly string _index, _source;

    static Test1()
    {
        var dataPath = GetTestDataPath(typeof(Test1));
        _index = File.ReadAllText(Path.Combine(dataPath, "index.txt"));
        _source = File.ReadAllText(Path.Combine(dataPath, "source.txt"));
    }

    [Fact]
    public void TestMergeChanges()
    {
        using var reader = new StringReader(_index);
        var index = CustomDirectoryInfoSerializer.Deserialize(reader);
        index.MergeChanges(_changes);
        var actual = CustomDirectoryInfoSerializer.Serialize(index).ToString();
        Assert.Equal(_source, actual);
    }
}
