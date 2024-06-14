namespace DatagentMonitorTests.CustomDirectoryInfoTests;

public class Test_RealCtor : TestBase, IClassFixture<DirectoryFixture>
{
    private static readonly List<(DirectoryInfo Directory, Func<FileSystemInfo, bool>? Filter)> _successArgs = new()
    {
        (new DirectoryInfo("source1"), null),
        (new DirectoryInfo("source2"), null),
        (new DirectoryInfo("source3"), null),
        (new DirectoryInfo("source4"), null),
        (new DirectoryInfo("source5"), null),
    };

    private static readonly List<(DirectoryInfo Directory, Func<FileSystemInfo, bool>? Filter, Type ExceptionType)> _failureArgs = new()
    {
        (new DirectoryInfo("source1"), null, typeof(Exception)),
        (new DirectoryInfo("source2"), null, typeof(Exception)),
        (new DirectoryInfo("source3"), null, typeof(Exception)),
        (new DirectoryInfo("source4"), null, typeof(Exception)),
        (new DirectoryInfo("source5"), null, typeof(Exception)),
    };

    public static IEnumerable<object?[]> SuccessArgs => _successArgs.Select(a => new object?[] { a.Directory, a.Filter });
    public static IEnumerable<object?[]> FailureArgs => _failureArgs.Select(a => new object?[] { a.Directory, a.Filter, a.ExceptionType });

    // TODO: add real directories (source1, source2, etc.) to Data folder 
    // and copy them to the temp directory for this test; 
    // DirectoryInfo's are then created based on those folders
    public Test_RealCtor(DirectoryFixture df)
    {
        var source = df.CreateTempDirectory(GetTempDirectoryName(""));
    }

    //[Theory]
    //[MemberData(nameof(SuccessArgs))]
    //public void Test_Success(DirectoryInfo directory, Func<FileSystemInfo, bool>? filter)
    //{
    //    var dir = new CustomDirectoryInfo(directory, filter);
    //    Assert.Equal(directory.Name, dir.Name);
    //    Assert.Equal(directory.LastWriteTime, dir.LastWriteTime);
    //}

    //[Theory]
    //[MemberData(nameof(FailureArgs))]
    //public void Test_Failure(DirectoryInfo directory, Func<FileSystemInfo, bool>? filter, Type exceptionType)
    //{
    //    Assert.Throws(exceptionType, () => new CustomDirectoryInfo(directory, filter));
    //}
}
