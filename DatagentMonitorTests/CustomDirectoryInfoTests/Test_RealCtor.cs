using DatagentMonitor.FileSystem;
using System.Diagnostics;

namespace DatagentMonitorTests.CustomDirectoryInfoTests;

// TODO: add filtration tests
public class Test_RealCtor : TestBase
{
    private static readonly IEnumerable<DirectoryInfo> _successArgs =
        new DirectoryInfo(GetDataPath(typeof(Test_RealCtor))).EnumerateDirectories();

    private static readonly List<(DirectoryInfo? Directory, Type ExceptionType)> _failureArgs = new()
    {
        (null, typeof(ArgumentNullException)),
        (new DirectoryInfo("path/to/absent/directory"), typeof(DirectoryNotFoundException))
    };

    public static IEnumerable<object[]> SuccessArgs => _successArgs.Select(d => new object[] { d });
    public static IEnumerable<object?[]> FailureArgs => _failureArgs.Select(a => new object?[] { a.Directory, a.ExceptionType });

    static Test_RealCtor()
    {
        // Generate test data
        // TODO: this won't work on Unix; fix
        var generator = new Process()
        {
            StartInfo = new ProcessStartInfo("cmd.exe")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                Arguments = $"/C python {Path.Combine(GetDataPath(typeof(Test_RealCtor)), "gen.py")}"
            }
        };
        generator.Start();
        generator.WaitForExit(10000);
        if (generator.ExitCode != 0)
            throw new TimeoutException($"The generator has failed to generate the data.");
    }

    [Theory]
    [MemberData(nameof(SuccessArgs))]
    public void Test_Success(DirectoryInfo directory)
    {
        var dir = new CustomDirectoryInfo(directory);
        Assert.Equal(directory.Name, dir.Name);
        Assert.Equal(directory.LastWriteTime, dir.LastWriteTime);
    }

    [Theory]
    [MemberData(nameof(FailureArgs))]
    public void Test_Failure(DirectoryInfo directory, Type exceptionType)
    {
        Assert.Throws(exceptionType, () => new CustomDirectoryInfo(directory));
    }
}
