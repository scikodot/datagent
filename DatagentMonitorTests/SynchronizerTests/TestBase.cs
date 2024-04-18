using DatagentMonitor;
using DatagentMonitor.FileSystem;
using DatagentMonitor.Utils;

namespace DatagentMonitorTests.SynchronizerTests;

public abstract class TestBase : IDisposable
{
    private readonly Random _rng;
    private readonly DirectoryInfo _source, _target;
    private readonly Synchronizer _synchronizer;

    public TestBase(List<FileSystemEntryChange> changes)
    {
        _rng = new Random(12345);

        var testName = GetType().Name;
        var namespaceName = GetType().Namespace!.Split('.')[^1];
        var dataPath = Path.Combine(namespaceName, "Data", testName);

        // Initialize temp source and target directories
        var timestamp = DateTime.Now.ToString(CustomFileInfo.DateTimeFormat);
        _source = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"_source_{timestamp}"));
        _target = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"_target_{timestamp}"));

        // Fill the source with the changed data
        using var sourceReader = new StreamReader(Path.Combine(dataPath, "source.txt"));
        ToFileSystem(_source, CustomDirectoryInfoSerializer.Deserialize(sourceReader));

        var manager = new SynchronizationSourceManager(_source.FullName);
        _synchronizer = new Synchronizer(manager);

        // Overwrite the source index with the initial data
        File.Copy(Path.Combine(dataPath, "index.txt"), manager.IndexPath, overwrite: true);

        // Fill the source database with the changes
        foreach (var change in changes)
        {
            ActionProperties? properties = change.Action switch
            {
                FileSystemEntryAction.Rename => change.Properties.RenameProps,
                FileSystemEntryAction.Create or
                FileSystemEntryAction.Change => change.Properties.ChangeProps,
                _ => null
            };
            manager.InsertEventEntry(change.Path, change.Action, change.Timestamp, properties).Wait();
        }

        // Fill the target with the changed data
        using var targetReader = new StreamReader(Path.Combine(dataPath, "target.txt"));
        ToFileSystem(_target, CustomDirectoryInfoSerializer.Deserialize(targetReader));
    }

    private void ToFileSystem(DirectoryInfo sourceRoot, CustomDirectoryInfo targetRoot)
    {
        foreach (var targetSubdir in targetRoot.Directories)
        {
            var sourceSubdir = sourceRoot.CreateSubdirectory(targetSubdir.Name);
            ToFileSystem(sourceSubdir, targetSubdir);
        }

        foreach (var targetFile in targetRoot.Files)
        {
            var sourceFile = new FileInfo(Path.Combine(sourceRoot.FullName, targetFile.Name));
            using (var writer = sourceFile.CreateText())
            {
                // Every char here takes 1 byte, as it is within the range [48, 123)
                for (int i = 0; i < targetFile.Length; i++)
                    writer.Write((char)_rng.Next(48, 123));
            }
            sourceFile.LastWriteTime = targetFile.LastWriteTime;
        }
    }

    [Fact]
    public void TestSynchronize()
    {
        _synchronizer.Run(_target.FullName);
        _source.Refresh();
        _target.Refresh();
        var sourceUnique = new List<FileSystemInfo>();
        var targetUnique = new List<FileSystemInfo>();
        GetUniqueEntries(_source, _target, sourceUnique, targetUnique);
        Assert.Empty(sourceUnique);
        Assert.Empty(targetUnique);
    }

    private static void GetUniqueEntries(DirectoryInfo source, DirectoryInfo target,
        List<FileSystemInfo> sourceUnique, List<FileSystemInfo> targetUnique)
    {
        var sourceDirectories = new Dictionary<string, DirectoryInfo>(
            source.EnumerateDirectories().Select(d => new KeyValuePair<string, DirectoryInfo>(d.Name, d)));
        foreach (var targetDirectory in target.EnumerateDirectories())
        {
            if (sourceDirectories.Remove(targetDirectory.Name, out var sourceDirectory))
            {
                GetUniqueEntries(sourceDirectory, targetDirectory, sourceUnique, targetUnique);
            }
            else
            {
                targetUnique.Add(targetDirectory);
            }
        }

        foreach (var sourceDirectory in sourceDirectories.Values)
            sourceUnique.Add(sourceDirectory);

        var sourceFiles = new Dictionary<string, FileInfo>(
            source.EnumerateFiles().Select(f => new KeyValuePair<string, FileInfo>(f.Name, f)));
        foreach (var targetFile in target.EnumerateFiles())
        {
            if (sourceFiles.Remove(targetFile.Name))
            {
                // TODO: compare files' properties
            }
            else
            {
                targetUnique.Add(targetFile);
            }
        }

        foreach (var sourceFile in sourceFiles.Values)
            sourceUnique.Add(sourceFile);
    }

    public void Dispose()
    {
        _source.Delete(recursive: true);
        _target.Delete(recursive: true);
        GC.SuppressFinalize(this);
    }
}
