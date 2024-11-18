using DatagentMonitor;
using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests.CustomDirectoryInfoTests.Test_Ctor_FromProperties;

public class DateTimeProviderFixture : DateTimeProviderFixtureAbstract
{
    public override IDateTimeProvider DateTimeProvider => DateTimeProviderFactory.FromDateTime(new DateTime(2024, 6, 15));
}

public class Test_Ctor_FromProperties : TestBase, IClassFixture<DateTimeProviderFixture>
{
    private static readonly List<(string Name, DateTime LastWriteTime)> _successArgs = new()
    {
        ("folder1", DateTime.MinValue),
        ("folder2", new DateTime(2024, 6, 14)),
        ("folder3", new DateTime(2024, 6, 14, 12, 34, 56)),
        ("folder4", new DateTime(2024, 6, 14, 12, 34, 56, 789)),
        ("folder5", new DateTime(2024, 6, 14, 12, 34, 56, 789).AddTicks(123)),

        // Unicode characters
        ("папка6", new DateTime(2024, 6, 14, 6, 0, 0)),
        ("папка7.fld", new DateTime(2024, 6, 14, 7, 0, 0)),

        // Invalid path/filename characters are not checked, 
        // as this ctor does not communicate with the file system
        (".", new DateTime(2024, 6, 14, 8, 0, 0)),
        ("<^*^>", new DateTime(2024, 6, 14, 9, 0, 0)),
        (@"|\/|@Э$ТР0", new DateTime(2024, 6, 14, 10, 0, 0)),
        ("some/path/to\\folder", new DateTime(2024, 6, 14, 11, 0, 0)),
    };

    private static readonly List<(string? Name, DateTime LastWriteTime, Type ExceptionType)> _failureArgs = new()
    {
        (null, new DateTime(2024, 6, 14, 1, 0, 0), typeof(ArgumentException)),
        ("", new DateTime(2024, 6, 14, 2, 0, 0), typeof(ArgumentException)),
        ("folder3", DateTime.MaxValue, typeof(FutureTimestampException)),
    };

    public static IEnumerable<object[]> SuccessArgs => _successArgs.Select(a => new object[] { a.Name, a.LastWriteTime });
    public static IEnumerable<object?[]> FailureArgs => _failureArgs.Select(a => new object?[] { a.Name, a.LastWriteTime, a.ExceptionType });

    public Test_Ctor_FromProperties(DateTimeProviderFixture dateTimeProviderFixture) : base()
    {

    }

    [Theory]
    [MemberData(nameof(SuccessArgs))]
    public void Test_Success(string name, DateTime lastWriteTime)
    {
        var directory = new CustomDirectoryInfo(name, lastWriteTime);
        Assert.Equal(name, directory.Name);
        Assert.Equal(lastWriteTime, directory.LastWriteTime);
    }

    [Theory]
    [MemberData(nameof(FailureArgs))]
    public void Test_Failure(string name, DateTime lastWriteTime, Type exceptionType)
    {
        Assert.Throws(exceptionType, () => new CustomDirectoryInfo(name, lastWriteTime));
    }
}
