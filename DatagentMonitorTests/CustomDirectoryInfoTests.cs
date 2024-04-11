using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests;

public class CustomDirectoryInfoTests
{
    private static readonly string _rootSerialized = string.Concat(
        "folder1\n",
        "\tsubfolder1\n",
        "\t\tfile1.txt: 20240407221842000, 1234\n",
        "\t\tfile2.csv: 20240407211200000, 1337\n",
        "\tfile3: 19700101000000777, 197011000\n",
        "folder2\n",
        "file3: 20770101123456789, 4221\n",
        "file4.pdb: 19991231235959000, 65536\n"
    );

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
                RenameProps = new RenameProperties
                {
                    Name = "file5-renamed.xlsx"
                },
                ChangeProps = new ChangeProperties
                {
                    LastWriteTime = new DateTime(2007, 7, 7),
                    Length = 777
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

    private static readonly string _rootChangedSerialized = string.Concat(
        "folder1-renamed\n",
        "\tsubfolder1\n",
        "\t\tssubfolder1\n",
        "\t\tfile2.csv: 20240409194736000, 7331\n",
        "\t\tfile1-renamed.txt: 20240407221842000, 1234\n",
        "file3: 20770101123456789, 4221\n",
        "file4.pdb: 19991231235959000, 65536\n",
        "file5-renamed.xlsx: 20070707000000000, 777\n"
    );

    [Fact]
    public void TestMergeChanges()
    {
        using var reader = new StringReader(_rootSerialized);
        var root = CustomDirectoryInfoSerializer.Deserialize(reader);
        root.MergeChanges(_changes);
        var actual = CustomDirectoryInfoSerializer.Serialize(root).ToString();
        Assert.Equal(_rootChangedSerialized, actual);
    }
}
