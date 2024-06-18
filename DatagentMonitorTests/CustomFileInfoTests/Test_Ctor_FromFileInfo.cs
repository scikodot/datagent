using DatagentMonitor;
using DatagentMonitor.FileSystem;
using System.Diagnostics;

namespace DatagentMonitorTests.CustomFileInfoTests.Test_Ctor_FromFileInfo;

public class DateTimeProviderFixture : DateTimeProviderFixtureAbstract
{
    public override IDateTimeProvider DateTimeProvider => DateTimeProviderFactory.FromDefault();
}

public class Test_Ctor_FromFileInfo : TestBase, IClassFixture<DateTimeProviderFixture>
{
    private static readonly IEnumerable<FileInfo> _successArgs =
        new DirectoryInfo(GetDataPath(typeof(Test_Ctor_FromFileInfo))).EnumerateFiles().Where(f => !f.Name.StartsWith("gen.py"));

    private static readonly List<(FileInfo? File, Type ExceptionType)> _failureArgs = new()
    {
        (null, typeof(ArgumentNullException)),
        (new FileInfo("path/to/absent/file"), typeof(FileNotFoundException))
    };

    public static IEnumerable<object[]> SuccessArgs => _successArgs.Select(f => new object[] { f });
    public static IEnumerable<object?[]> FailureArgs => _failureArgs.Select(a => new object?[] { a.File, a.ExceptionType });

    static Test_Ctor_FromFileInfo()
    {
        // Generate test data
        // TODO: this won't work on Unix; fix
        var generator = new Process()
        {
            StartInfo = new ProcessStartInfo("cmd.exe")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                Arguments = $"/C python {Path.Combine(GetDataPath(typeof(Test_Ctor_FromFileInfo)), "gen.py")}"
            }
        };
        generator.Start();
        generator.WaitForExit(10000);
        if (generator.ExitCode != 0)
            throw new TimeoutException($"The generator has failed to generate the data.");
    }

    public Test_Ctor_FromFileInfo(DateTimeProviderFixture dateTimeProviderFixture)
    {

    }

    [Theory]
    [MemberData(nameof(SuccessArgs))]
    public void Test_Success(FileInfo file)
    {
        var f = new CustomFileInfo(file);
        Assert.Equal(file.Name, f.Name);
        Assert.Equal(file.LastWriteTime, f.LastWriteTime);
        Assert.Equal(file.Length, f.Length);
    }

    [Theory]
    [MemberData(nameof(FailureArgs))]
    public void Test_Failure(FileInfo file, Type exceptionType)
    {
        Assert.Throws(exceptionType, () => new CustomFileInfo(file));
    }
}
