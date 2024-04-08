using DatagentMonitor;
using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests;

public class CustomDirectoryInfoSerializerTests
{
    private static readonly CustomDirectoryInfo _root = new CustomDirectoryInfo
    {
        Directories = new OrderedDictionary<string, CustomDirectoryInfo>
        {
            ["folder1"] = new CustomDirectoryInfo
            {
                Directories = new OrderedDictionary<string, CustomDirectoryInfo>
                {
                    ["subfolder1"] = new CustomDirectoryInfo
                    {
                        Files = new OrderedDictionary<string, CustomFileInfo>
                        {
                            ["file1.txt"] = new CustomFileInfo
                            {
                                LastWriteTime = new DateTime(2024, 4, 7, 22, 18, 42),
                                Length = 1234
                            },
                            ["file2.csv"] = new CustomFileInfo
                            {
                                LastWriteTime = new DateTime(2024, 4, 7, 21, 12, 0),
                                Length = 1337
                            }
                        }
                    }
                },
                Files = new OrderedDictionary<string, CustomFileInfo>
                {
                    ["file3"] = new CustomFileInfo
                    {
                        LastWriteTime = new DateTime(1970, 1, 1, 0, 0, 0, 777),
                        Length = 197011000
                    }
                }
            },
            ["folder2"] = new CustomDirectoryInfo()
        },
        Files = new OrderedDictionary<string, CustomFileInfo>
        {
            ["file3"] = new CustomFileInfo
            {
                LastWriteTime = new DateTime(2077, 1, 1, 12, 34, 56, 789).AddTicks(123),
                Length = 4221
            },
            ["file4.pdb"] = new CustomFileInfo
            {
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
