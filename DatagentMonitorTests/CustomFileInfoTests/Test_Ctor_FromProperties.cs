using DatagentMonitor.FileSystem;
using DatagentMonitor;

namespace DatagentMonitorTests.CustomFileInfoTests.Test_Ctor_FromProperties;

public class DateTimeProviderFixture : DateTimeProviderFixtureAbstract
{
    public override IDateTimeProvider DateTimeProvider => DateTimeProviderFactory.FromDateTime(new DateTime(2024, 6, 15));
}

public class Test_Ctor_FromProperties : TestBase, IClassFixture<DateTimeProviderFixture>
{
    private static readonly List<(string Name, DateTime LastWriteTime, long Length)> _successArgs = new()
    {
        ("file0", DateTime.MinValue, 0),
        ("file1", new DateTime(2024, 6, 14), 100),
        ("file2", new DateTime(2024, 6, 14, 12, 34, 56), 200),
        ("file3", new DateTime(2024, 6, 14, 12, 34, 56, 789), 300),
        ("file4", new DateTime(2024, 6, 14, 12, 34, 56, 789).AddTicks(123), 400),

        // Unicode characters
        ("файл5", new DateTime(2024, 6, 14, 5, 0, 0), 500),
        ("файл6.doc", new DateTime(2024, 6, 14, 6, 0, 0), 600),

        // Invalid path/filename characters are not checked, 
        // as this ctor does not communicate with the file system
        (".", new DateTime(2024, 6, 14, 7, 0, 0), 700),
        ("(>_<)", new DateTime(2024, 6, 14, 8, 0, 0), 800),
        (@"\/\/\/", new DateTime(2024, 6, 14, 9, 0, 0), 900),
        ("[System.String]::new('not-a-string')", new DateTime(2024, 6, 14, 10, 0, 0), 1000),
    };

    private static readonly List<(string? Name, DateTime LastWriteTime, long Length, Type ExceptionType)> _failureArgs = new()
    {
        (null, new DateTime(2024, 6, 14, 1, 0, 0), 100, typeof(ArgumentException)),
        ("", new DateTime(2024, 6, 14, 2, 0, 0), 100, typeof(ArgumentException)),
        ("file3", DateTime.MaxValue, 300, typeof(FutureTimestampException)),
        ("file4", new DateTime(2024, 6, 14, 4, 0, 0), -400, typeof(ArgumentException)),
    };

    public static IEnumerable<object[]> SuccessArgs => _successArgs.Select(a => new object[] { a.Name, a.LastWriteTime, a.Length });
    public static IEnumerable<object?[]> FailureArgs => _failureArgs.Select(a => new object?[] { a.Name, a.LastWriteTime, a.Length, a.ExceptionType });

    public Test_Ctor_FromProperties(DateTimeProviderFixture dateTimeProviderFixture) : base()
    {

    }

    [Theory]
    [MemberData(nameof(SuccessArgs))]
    public void Test_Success(string name, DateTime lastWriteTime, long length)
    {
        var file = new CustomFileInfo(name, lastWriteTime, length);
        Assert.Equal(name, file.Name);
        Assert.Equal(lastWriteTime, file.LastWriteTime);
        Assert.Equal(length, file.Length);
    }

    [Theory]
    [MemberData(nameof(FailureArgs))]
    public void Test_Failure(string name, DateTime lastWriteTime, long length, Type exceptionType)
    {
        Assert.Throws(exceptionType, () => new CustomFileInfo(name, lastWriteTime, length));
    }
}
