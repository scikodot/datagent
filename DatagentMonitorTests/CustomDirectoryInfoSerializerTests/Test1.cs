using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests.CustomDirectoryInfoSerializerTests;

public class Test1 : TestBaseCommon
{
    private static readonly CustomDirectoryInfo _root = new("root")
    {
        Directories = new(d => d.Name)
        {
            new CustomDirectoryInfo("folder1")
            {
                Directories = new(d => d.Name)
                {
                    new CustomDirectoryInfo("subfolder1")
                    {
                        Files = new(f => f.Name)
                        {
                            new CustomFileInfo("file1.txt")
                            {
                                LastWriteTime = new DateTime(2024, 4, 7, 22, 18, 42),
                                Length = 1234
                            },
                            new CustomFileInfo("file2.csv")
                            {
                                LastWriteTime = new DateTime(2024, 4, 7, 21, 12, 0),
                                Length = 1337
                            }
                        }
                    }
                },
                Files = new(f => f.Name)
                {
                    new CustomFileInfo("file3")
                    {
                        LastWriteTime = new DateTime(1970, 1, 1, 0, 0, 0, 777),
                        Length = 197011000
                    }
                }
            },
            new CustomDirectoryInfo("folder2")
        },
        Files = new(f => f.Name)
        {
            new CustomFileInfo("file3")
            {
                LastWriteTime = new DateTime(2077, 1, 1, 12, 34, 56, 789).AddTicks(123),
                Length = 4221
            },
            new CustomFileInfo("file4.pdb")
            {
                LastWriteTime = new DateTime(1999, 12, 31, 23, 59, 59),
                Length = 65536
            }
        }
    };

    private static readonly string _rootSerialized;

    static Test1()
    {
        var dataPath = GetTestDataPath(typeof(Test1));
        _rootSerialized = File.ReadAllText(Path.Combine(dataPath, "serialized.txt"));
    }

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
