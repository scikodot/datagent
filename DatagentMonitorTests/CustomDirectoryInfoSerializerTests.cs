using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests;

public class CustomDirectoryInfoSerializerTests
{
    private static readonly CustomDirectoryInfo _root = new CustomDirectoryInfo
    {
        Directories = new Dictionary<string, CustomDirectoryInfo>
        {
            ["folder1"] = new CustomDirectoryInfo
            {
                Directories = new Dictionary<string, CustomDirectoryInfo>
                {
                    ["subfolder1"] = new CustomDirectoryInfo
                    {
                        Files = new Dictionary<string, CustomFileInfo>
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
                Files = new Dictionary<string, CustomFileInfo>
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
        Files = new Dictionary<string, CustomFileInfo>
        {
            ["file3"] = new CustomFileInfo
            {
                LastWriteTime = new DateTime(2077, 1, 1, 12, 34, 56, 7890123),
                Length = 4221
            },
            ["file4.pdb"] = new CustomFileInfo
            {
                LastWriteTime = new DateTime(1999, 31, 12, 23, 59, 59),
                Length = 65536
            }
        }
    };

    private static readonly string _rootSerialized =
@"folder1
    subfolder1
        file1.txt: 202447221842000, 1234
        file2.csv: 202447211200000, 1337
    file3: 19700101000000777000, 197011000
folder2
file3: 20770101123456789, 4221
file4.pdb: 19993112235959000, 65536
";

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
