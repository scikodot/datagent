using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests.CustomDirectoryInfoSerializerTests;

public class Test : TestBase
{
    private static readonly List<CustomDirectoryInfo> _directories = new()
    {
        new("Empty", new DateTime(2024, 6, 13, 0, 0, 0)),

        new("SingleLevel", new DateTime(2024, 6, 13, 5, 0, 0))
        {
            Entries = new CustomFileSystemInfoCollection
            {
                new CustomDirectoryInfo("folder1", new DateTime(2024, 6, 13, 1, 0, 0)),
                new CustomDirectoryInfo("folder2", new DateTime(2024, 6, 13, 2, 0, 0)),
                new CustomFileInfo("file1", new DateTime(2024, 6, 13, 3, 0, 0), 300),
                new CustomFileInfo("file2", new DateTime(2024, 6, 13, 4, 0, 0), 400),
                new CustomFileInfo("file3", new DateTime(2024, 6, 13, 5, 0, 0), 500),
            }
        },

        new("MultiLevel", new DateTime(2024, 6, 13, 23, 0, 0))
        {
            Entries = new CustomFileSystemInfoCollection
            {
                new CustomDirectoryInfo("folder1", new DateTime(2024, 6, 13, 13, 0, 0))
                {
                    Entries = new CustomFileSystemInfoCollection
                    {
                        new CustomDirectoryInfo("subfolder1", new DateTime(2024, 6, 13, 12, 0, 0))
                        {
                            Entries = new CustomFileSystemInfoCollection
                            {
                                new CustomFileInfo("file1_1.txt", new DateTime(2024, 6, 13, 11, 0, 0), 1100),
                                new CustomFileInfo("file2_1.csv", new DateTime(2024, 6, 13, 12, 0, 0), 1200)
                            }
                        },
                        new CustomFileInfo("file3", new DateTime(2024, 6, 13, 13, 0, 0), 1300)
                    }
                },
                new CustomDirectoryInfo("folder2", new DateTime(2024, 6, 13, 22, 0, 0, 0).AddTicks(200))
                {
                    Entries = new CustomFileSystemInfoCollection
                    {
                        new CustomFileInfo("file1_2.sfx", new DateTime(2024, 6, 13, 21, 0, 0, 100), 2100),
                        new CustomFileInfo("file2_2.mp4", new DateTime(2024, 6, 13, 22, 0, 0, 0).AddTicks(200), 2200)
                    }
                },
                new CustomDirectoryInfo("folder3", new DateTime(2024, 6, 13, 22, 0, 0)),
                new CustomFileInfo("file3", new DateTime(2024, 6, 13, 23, 0, 0), 2300),
                new CustomFileInfo("file4.pdb", new DateTime(2024, 6, 13, 4, 0, 0), 400)
            }
        },
        
        new("Unicode", new DateTime(2024, 6, 13, 22, 0, 0))
        {
            Entries = new CustomFileSystemInfoCollection
            {
                new CustomDirectoryInfo("директория1", new DateTime(2024, 6, 13, 12, 0, 0))
                {
                    Entries = new CustomFileSystemInfoCollection
                    {
                        new CustomFileInfo("документ1", new DateTime(2024, 6, 13, 11, 0, 0), 1100),
                        new CustomFileInfo("файл2", new DateTime(2024, 6, 13, 12, 0, 0), 1200)
                    }
                },
                new CustomDirectoryInfo("папка2", new DateTime(2024, 6, 13, 2, 0, 0)),
                new CustomFileInfo("документ1_outer", new DateTime(2024, 6, 13, 21, 0, 0), 2100),
                new CustomFileInfo("файл2_outer", new DateTime(2024, 6, 13, 22, 0, 0), 2200)
            }
        }
    };

    public static IEnumerable<object[]> Directories => _directories.Select(d => new object[] { d });

    [Theory]
    [MemberData(nameof(Directories))]
    public void Test_Serialize(CustomDirectoryInfo info)
    {
        var actual = CustomDirectoryInfoSerializer.Serialize(info);
        Assert.Equal(Config[info.Name], actual);
    }

    // Tests that serializer f(x) and deserializer g(x) are inverse, i. e. f(g(x)) = x;
    // if the serializer is correct, this test confirms that the deserializer is correct
    [Theory]
    [MemberData(nameof(Directories))]
    public void Test_Deserialize(CustomDirectoryInfo info)
    {
        using var reader = new StringReader(Config[info.Name]);
        var root = CustomDirectoryInfoSerializer.Deserialize(reader);
        var actual = CustomDirectoryInfoSerializer.Serialize(root);
        Assert.Equal(Config[info.Name], actual);
    }
}
