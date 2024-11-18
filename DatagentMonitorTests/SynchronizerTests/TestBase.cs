using DatagentMonitor.FileSystem;
using DatagentMonitor.Synchronization;

namespace DatagentMonitorTests.SynchronizerTests;

// TODO: consider converting derived classes to test cases, 
// i.e. use [Theory] instead of inheritance
public abstract class TestBase : DatagentMonitorTests.TestBase
{
    private readonly DirectoryInfo _source, _target;
    private readonly Synchronizer _synchronizer;
    private readonly string _result;

    protected abstract IEnumerable<EntryChange> Changes { get; }
    protected abstract DateTime? LastSyncTime { get; }

    public TestBase(DirectoryFixture directoryFixture) : base()
    {
        // Initialize temp source and target directories
        var parent = directoryFixture.CreateTempDirectory(TestName);
        _source = parent.CreateSubdirectory("source");
        _target = parent.CreateSubdirectory("target");

        // Fill the source with the changed data
        using var sourceReader = new StringReader(Config["Source"]);
        ToFileSystem(_source, CustomDirectoryInfoSerializer.Deserialize(sourceReader));

        // Fill the target with the changed data
        using var targetReader = new StringReader(Config["Target"]);
        ToFileSystem(_target, CustomDirectoryInfoSerializer.Deserialize(targetReader));

        _synchronizer = new Synchronizer(_source.FullName, _target.FullName);

        // Overwrite the source index with the initial data
        File.WriteAllText(_synchronizer.SourceManager.Index.Path, Config["Index"]);

        // Fill the source database with the changes
        foreach (var change in Changes)
            _synchronizer.SourceManager.Database.AddEvent(change).Wait();

        if (LastSyncTime is not null)
            _synchronizer.TargetManager.Database.LastSyncTime = LastSyncTime;

        // Load the result
        _result = Config["Result"];
    }

    [Fact]
    public void Test_Run()
    {
        _synchronizer.Run(out var sourceResult, out var targetResult);
        _source.Refresh();
        _target.Refresh();

        // Assert that no changes have failed
        Assert.Empty(sourceResult.Failed);
        Assert.Empty(targetResult.Failed);

        var source = CustomDirectoryInfoSerializer.Serialize(new CustomDirectoryInfo(_source, SourceFilter.ServiceMatcher));
        var target = CustomDirectoryInfoSerializer.Serialize(new CustomDirectoryInfo(_target, SourceFilter.ServiceMatcher));

        // Assert that source and target are identical to the common state
        Assert.Equal(_result, source);
        Assert.Equal(_result, target);

        var sourceIndex = File.ReadAllText(_synchronizer.SourceManager.Index.Path);
        var targetIndex = File.ReadAllText(_synchronizer.TargetManager.Index.Path);

        // Assert that both source and target indexes are identical to the common state
        Assert.Equal(_result, sourceIndex);
        Assert.Equal(_result, targetIndex);
    }
}
