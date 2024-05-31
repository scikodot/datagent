﻿using DatagentMonitor.FileSystem;
using DatagentMonitor.Utils;

namespace DatagentMonitorTests.SynchronizerTests;

public abstract class TestBase : DatagentMonitorTests.TestBase, IDisposable
{
    private readonly Random _rng;
    private readonly DirectoryInfo _source, _target;
    private readonly Synchronizer _synchronizer;
    private readonly string _result;

    protected abstract IEnumerable<EntryChange> Changes { get; }
    protected abstract DateTime? LastSyncTime { get; }

    public TestBase()
    {
        _rng = new Random(12345);

        var name = GetType().Name.ToLower();

        // Initialize temp source directory
        _source = new DirectoryInfo(Path.Combine(Path.GetTempPath(), $"_source_{name}"));
        if (_source.Exists)
            _source.Delete(recursive: true);
        _source.Create();
        _source.Refresh();

        // Initialize temp target directory
        _target = new DirectoryInfo(Path.Combine(Path.GetTempPath(), $"_target_{name}"));
        if (_target.Exists)
            _target.Delete(recursive: true);
        _target.Create();
        _target.Refresh();

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
            _synchronizer.SourceManager.SyncDatabase.AddEvent(change).Wait();

        if (LastSyncTime is not null)
            _synchronizer.TargetManager.SyncDatabase.LastSyncTime = LastSyncTime;

        // Load the result
        _result = Config["Result"];
    }

    private void ToFileSystem(DirectoryInfo sourceRoot, CustomDirectoryInfo targetRoot)
    {
        var lwt = DateTime.MinValue;
        foreach (var targetSubdir in targetRoot.Entries.Directories)
        {
            var sourceSubdir = sourceRoot.CreateSubdirectory(targetSubdir.Name);
            ToFileSystem(sourceSubdir, targetSubdir);

            if (sourceSubdir.LastWriteTime > lwt)
                lwt = sourceSubdir.LastWriteTime;
        }

        foreach (var targetFile in targetRoot.Entries.Files)
        {
            var sourceFile = new FileInfo(Path.Combine(sourceRoot.FullName, targetFile.Name));
            using (var writer = sourceFile.CreateText())
            {
                // Every char here takes 1 byte, as it is within the range [48, 123)
                for (int i = 0; i < targetFile.Length; i++)
                    writer.Write((char)_rng.Next(48, 123));
            }
            sourceFile.LastWriteTime = targetFile.LastWriteTime;

            if (sourceFile.LastWriteTime > lwt)
                lwt = sourceFile.LastWriteTime;
        }

        if (lwt > DateTime.MinValue)
            sourceRoot.LastWriteTime = lwt;
    }

    [Fact]
    public void TestSynchronize()
    {
        _synchronizer.Run(
            out _, out var failedSource, 
            out _, out var failedTarget);
        _source.Refresh();
        _target.Refresh();

        // Assert that no changes have failed
        Assert.Empty(failedSource);
        Assert.Empty(failedTarget);

        var source = CustomDirectoryInfoSerializer.Serialize(new CustomDirectoryInfo(_source,
            d => !_synchronizer.SourceManager.IsServiceLocation(d.FullName)));
        var target = CustomDirectoryInfoSerializer.Serialize(new CustomDirectoryInfo(_target,
            d => !_synchronizer.TargetManager.IsServiceLocation(d.FullName)));

        // Assert that source and target are identical to the common state
        Assert.Equal(_result, source);
        Assert.Equal(_result, target);

        var sourceIndex = File.ReadAllText(_synchronizer.SourceManager.Index.Path);
        var targetIndex = File.ReadAllText(_synchronizer.TargetManager.Index.Path);

        // Assert that both source and target indexes are identical to the common state
        Assert.Equal(_result, sourceIndex);
        Assert.Equal(_result, targetIndex);
    }

    public void Dispose()
    {
        _source.Delete(recursive: true);
        _target.Delete(recursive: true);
        GC.SuppressFinalize(this);
    }
}
