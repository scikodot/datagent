using DatagentMonitor;
using DatagentMonitor.FileSystem;
using DatagentMonitor.Utils;

namespace DatagentMonitorTests;

internal class SynchronizerTests
{
    private static readonly string _rootSerialized = string.Concat(
        "folder1\n",
        "\tsubfolder1\n",
        "\t\tfile1.txt: 20240407221842000, 1234\n",
        "\t\tfile2.csv: 20240407211200000, 1338\n",
        "\tfile3: 19700101000000777, 2222\n",
        "folder2\n",
        "file3: 20770101123456789, 4242\n",
        "file4.pdb: 19991231235959000, 65536\n"
    );

    private static readonly string _rootChangedSerialized = string.Concat(
        "folder1-renamed\n",
        "\tsubfolder1\n",
        "\t\tssubfolder1\n",
        "\t\tfile2.csv: 20240409194736000, 3318\n",
        "\t\tfile1-renamed.txt: 20240407221842000, 1234\n",
        "file3: 20770101123456789, 4242\n",
        "file4.pdb: 19991231235959000, 65536\n",
        "file5-renamed.xlsx: 20070707000000000, 888\n"
    );

    private readonly DirectoryInfo _source;
    private readonly DirectoryInfo _target;
    private readonly SynchronizationSourceManager _manager;
    private readonly Synchronizer _synchronizer;
    private readonly Random _rng;

    public SynchronizerTests()
    {
        _rng = new Random(12345);

        // Initialize temp source and target directories
        var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
        _source = new DirectoryInfo(Path.Combine(Path.GetTempPath(), $"_source_{timestamp}"));
        _source.Create();
        _target = new DirectoryInfo(Path.Combine(Path.GetTempPath(), $"_target_{timestamp}"));
        _target.Create();

        _manager = new SynchronizationSourceManager(_source.FullName);
        _synchronizer = new Synchronizer(_manager);

        // Fill the source index with the initial data
        File.WriteAllText(_manager.IndexPath, _rootSerialized);

        // Fill the source with the changed data
        using var reader = new StringReader(_rootChangedSerialized);
        ToFileSystem(_source, CustomDirectoryInfoSerializer.Deserialize(reader));

        // TODO: fill the target with the changed data
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
            var sourceFile = new FileInfo("abc")
            {
                LastWriteTime = targetFile.LastWriteTime
            };
            using var writer = sourceFile.CreateText();
            for (int i = 0; i < targetFile.Length / 2; i++)
                writer.Write((char)_rng.Next(48, 123));
        }
    }
}
