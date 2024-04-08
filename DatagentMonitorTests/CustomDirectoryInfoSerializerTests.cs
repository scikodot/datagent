using DatagentMonitor;
using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests;

public class CustomDirectoryInfoSerializerTests
{
    private static readonly CustomDirectoryInfo _root = new()
    {
        Name = "root",
        Directories = new(d => d.Name)
        {
            new CustomDirectoryInfo
            {
                Name = "folder1",
                Directories = new(d => d.Name)
                {
                    new CustomDirectoryInfo
                    {
                        Name = "subfolder1",
                        Files = new(f => f.Name)
                        {
                            new CustomFileInfo
                            {
                                Name = "file1.txt",
                                LastWriteTime = new DateTime(2024, 4, 7, 22, 18, 42),
                                Length = 1234
                            },
                            new CustomFileInfo
                            {
                                Name = "file2.csv",
                                LastWriteTime = new DateTime(2024, 4, 7, 21, 12, 0),
                                Length = 1337
                            }
                        }
                    }
                },
                Files = new(f => f.Name)
                {
                    new CustomFileInfo
                    {
                        Name = "file3",
                        LastWriteTime = new DateTime(1970, 1, 1, 0, 0, 0, 777),
                        Length = 197011000
                    }
                }
            },
            new CustomDirectoryInfo
            {
                Name = "folder2"
            }
        },
        Files = new(f => f.Name)
        {
            new CustomFileInfo
            {
                Name = "file3",
                LastWriteTime = new DateTime(2077, 1, 1, 12, 34, 56, 789).AddTicks(123),
                Length = 4221
            },
            new CustomFileInfo
            {
                Name = "file4.pdb",
                LastWriteTime = new DateTime(1999, 12, 31, 23, 59, 59),
                Length = 65536
            }
        }
    };

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

    [Fact]
    public void TestSerialize()
    {
        var actual = CustomDirectoryInfoSerializer.Serialize(_root).ToString();
        Assert.Equal(_rootSerialized, actual);
    }

    // Tests that serializer f(x) and deserializer g(x) are inverse, i. e. f(g(x)) = x;
    // if the serializer is correct, this test confirms that the deserializer is correct
    [Fact]
    public void TestSerializeDeserialize()
    {
        using var reader = new StringReader(_rootSerialized);
        var root = CustomDirectoryInfoSerializer.Deserialize(reader);
        var actual = CustomDirectoryInfoSerializer.Serialize(root).ToString();
        Assert.Equal(_rootSerialized, actual);
    }
}
