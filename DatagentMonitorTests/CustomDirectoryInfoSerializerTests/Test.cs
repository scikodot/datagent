using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests.CustomDirectoryInfoSerializerTests;

public class Test : TestBase
{
    private static readonly CustomDirectoryInfo _root = new("root", new DateTime(2077, 1, 1, 12, 34, 56, 789).AddTicks(123))
    {
        Entries = new()
        {
            new CustomDirectoryInfo("folder1", new DateTime(2024, 4, 7, 22, 18, 42))
            {
                Entries = new()
                {
                    new CustomDirectoryInfo("subfolder1", new DateTime(2024, 4, 7, 22, 18, 42))
                    {
                        Entries = new()
                        {
                            new CustomFileInfo("file1.txt", new DateTime(2024, 4, 7, 22, 18, 42), 1234),
                            new CustomFileInfo("file2.csv", new DateTime(2024, 4, 7, 21, 12, 0), 1337)
                        }
                    },
                    new CustomFileInfo("file3", new DateTime(1970, 1, 1, 0, 0, 0, 777), 197011000)
                }
            },
            new CustomDirectoryInfo("folder2", new DateTime(2007, 7, 7, 0, 0, 0)),
            new CustomFileInfo("file3", new DateTime(2077, 1, 1, 12, 34, 56, 789).AddTicks(123), 4221),
            new CustomFileInfo("file4.pdb", new DateTime(1999, 12, 31, 23, 59, 59), 65536)
        }
    };

    [Fact]
    public void TestSerialize()
    {
        var actual = CustomDirectoryInfoSerializer.Serialize(_root);
        Assert.Equal(Config["Root"], actual);
    }

    // Tests that serializer f(x) and deserializer g(x) are inverse, i. e. f(g(x)) = x;
    // if the serializer is correct, this test confirms that the deserializer is correct
    [Fact]
    public void TestSerializeDeserialize()
    {
        using var reader = new StringReader(Config["Root"]);
        var root = CustomDirectoryInfoSerializer.Deserialize(reader);
        var actual = CustomDirectoryInfoSerializer.Serialize(root);
        Assert.Equal(Config["Root"], actual);
    }
}
