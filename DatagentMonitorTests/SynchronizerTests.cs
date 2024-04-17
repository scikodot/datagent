using DatagentMonitor;
using DatagentMonitor.FileSystem;
using DatagentMonitor.Utils;
using Microsoft.Data.Sqlite;
using System.Text;

namespace DatagentMonitorTests;

public class SynchronizerTests
{
    private static readonly string _indexSerialized = string.Concat(
        "folder1\n",
        "\tsubfolder1\n",
        "\t\tfile1.txt: 20240407221842000, 1234\n",
        "\t\tfile2.csv: 20240407211200000, 1338\n",
        "\tfile3: 19700101000000777, 2222\n",
        "folder2\n",
        "file3: 20770101123456789, 4242\n",
        "file4.pdb: 19991231235959000, 65536\n"
    );

    private static readonly List<FileSystemEntryChange> _sourceChanges = new()
    {
        new FileSystemEntryChange
        {
            Timestamp = new DateTime(2024, 4, 11, 15, 0, 0),
            Path = Path.Combine("folder1", "subfolder1", "ssubfolder1") + Path.DirectorySeparatorChar,
            Action = FileSystemEntryAction.Create
        },
        new FileSystemEntryChange
        {
            Timestamp = new DateTime(2024, 4, 11, 15, 0, 1),
            Path = "folder1" + Path.DirectorySeparatorChar,
            Action = FileSystemEntryAction.Rename,
            Properties = new FileSystemEntryChangeProperties
            {
                RenameProps = new RenameProperties
                {
                    Name = "folder1-renamed-source"
                }
            }
        },
        new FileSystemEntryChange
        {
            Timestamp = new DateTime(2024, 4, 11, 15, 0, 2),
            Path = "folder2" + Path.DirectorySeparatorChar,
            Action = FileSystemEntryAction.Delete
        },
        new FileSystemEntryChange
        {
            Timestamp = new DateTime(2024, 4, 11, 15, 0, 3),
            Path = "file5.xlsx",
            Action = FileSystemEntryAction.Create,
            Properties = new FileSystemEntryChangeProperties
            {
                ChangeProps = new ChangeProperties
                {
                    LastWriteTime = new DateTime(2007, 7, 7),
                    Length = 888
                }
            }
        },
        new FileSystemEntryChange
        {
            Timestamp = new DateTime(2024, 4, 11, 15, 0, 4),
            Path = "file5.xlsx",
            Action = FileSystemEntryAction.Rename,
            Properties = new FileSystemEntryChangeProperties
            {
                RenameProps = new RenameProperties
                {
                    Name = "file5-renamed-source.xlsx"
                }
            }
        },
        new FileSystemEntryChange
        {
            Timestamp = new DateTime(2024, 4, 11, 15, 0, 5),
            Path = Path.Combine("folder1-renamed-source", "subfolder1", "file1.txt"),
            Action = FileSystemEntryAction.Rename,
            Properties = new FileSystemEntryChangeProperties
            {
                RenameProps = new RenameProperties
                {
                    Name = "file1-renamed-source.txt"
                }
            }
        },
        new FileSystemEntryChange
        {
            Timestamp = new DateTime(2024, 4, 11, 15, 0, 6),
            Path = Path.Combine("folder1-renamed-source", "subfolder1", "file2.csv"),
            Action = FileSystemEntryAction.Change,
            Properties = new FileSystemEntryChangeProperties
            {
                ChangeProps = new ChangeProperties
                {
                    LastWriteTime = new DateTime(2024, 4, 9, 19, 47, 36),
                    Length = 3318
                }
            }
        },
        new FileSystemEntryChange
        {
            Timestamp = new DateTime(2024, 4, 11, 15, 0, 7),
            Path = Path.Combine("folder1-renamed-source", "file3"),
            Action = FileSystemEntryAction.Delete
        }
    };

    private static readonly string _sourceSerialized = string.Concat(
        "folder1-renamed-source\n",
        "\tsubfolder1\n",
        "\t\tssubfolder1\n",
        "\t\tfile2.csv: 20240409194736000, 3318\n",
        "\t\tfile1-renamed-source.txt: 20240407221842000, 1234\n",
        "file3: 20770101123456789, 4242\n",
        "file4.pdb: 19991231235959000, 65536\n",
        "file5-renamed-source.xlsx: 20070707000000000, 888\n"
    );

    private static readonly List<FileSystemEntryChange> _targetChanges = new()
    {
        new FileSystemEntryChange
        {
            Path = "folder3" + Path.DirectorySeparatorChar,
            Action = FileSystemEntryAction.Create
        },
        new FileSystemEntryChange
        {
            Path = "folder1" + Path.DirectorySeparatorChar,
            Action = FileSystemEntryAction.Rename,
            Properties = new FileSystemEntryChangeProperties
            {
                RenameProps = new RenameProperties
                {
                    Name = "folder1-renamed-target"
                }
            }
        },
        new FileSystemEntryChange
        {
            Path = "folder2" + Path.DirectorySeparatorChar,
            Action = FileSystemEntryAction.Delete
        },
        new FileSystemEntryChange
        {
            Path = Path.Combine("folder1", "subfolder1", "file3.png"),
            Action = FileSystemEntryAction.Create,
            Properties = new FileSystemEntryChangeProperties
            {
                ChangeProps = new ChangeProperties
                {
                    LastWriteTime = new DateTime(2013, 12, 11),
                    Length = 444
                }
            }
        },
        new FileSystemEntryChange
        {
            Path = "file3",
            Action = FileSystemEntryAction.Rename,
            Properties = new FileSystemEntryChangeProperties
            {
                RenameProps = new RenameProperties
                {
                    Name = "file3-renamed-target"
                }
            }
        },
        new FileSystemEntryChange
        {
            Path = Path.Combine("folder1", "file3"),
            Action = FileSystemEntryAction.Change,
            Properties = new FileSystemEntryChangeProperties
            {
                ChangeProps = new ChangeProperties
                {
                    LastWriteTime = new DateTime(2056, 8, 14, 20, 43, 57),
                    Length = 12000
                }
            }
        },
        new FileSystemEntryChange
        {
            Path = "file4.pdb",
            Action = FileSystemEntryAction.Delete
        }
    };

    private static readonly string _targetSerialized = string.Concat(
        "folder3\n",
        "folder1-renamed-target\n",
        "\tsubfolder1\n",
        "\t\tfile1.txt: 20240407221842000, 1234\n",
        "\t\tfile2.csv: 20240407211200000, 1338\n",
        "\t\tfile3.png: 20131211000000000, 444\n",
        "\tfile3: 20560814204357000, 12000\n",
        "file3-renamed-target: 20770101123456789, 4242\n"
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

        // Fill the source with the changed data
        using var sourceReader = new StringReader(_sourceSerialized);
        ToFileSystem(_source, CustomDirectoryInfoSerializer.Deserialize(sourceReader));

        _manager = new SynchronizationSourceManager(_source.FullName);
        _synchronizer = new Synchronizer(_manager);

        // Overwrite the source index with the initial data
        File.WriteAllText(_manager.IndexPath, _indexSerialized);

        // Fill the source database with the changes
        foreach (var change in _sourceChanges)
        {
            ActionProperties? properties = change.Action switch
            {
                FileSystemEntryAction.Rename => change.Properties.RenameProps,
                FileSystemEntryAction.Create or
                FileSystemEntryAction.Change => change.Properties.ChangeProps,
                _ => null
            };
            _manager.InsertEventEntry(change.Path, change.Action, change.Timestamp, properties).Wait();
        }

        // Fill the target with the changed data
        using var targetReader = new StringReader(_targetSerialized);
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
        AssertDirectoriesEqual(_source, _target, sourceUnique, targetUnique);
        if (sourceUnique.Count > 0 || targetUnique.Count > 0)
        {
            var builder = new StringBuilder();
            builder.AppendLine("Synchronization failed.");
            if (sourceUnique.Count > 0)
            {
                builder.AppendLine("Source entries not in target:");
                foreach (var sourceEntry in sourceUnique)
                    builder.AppendLine(sourceEntry.FullName);
            }
            if (targetUnique.Count > 0)
            {
                builder.AppendLine("Target entries not in source:");
                foreach (var targetEntry in targetUnique)
                    builder.AppendLine(targetEntry.FullName);
            }

            Assert.Fail(builder.ToString());
        }
    }

    private static void AssertDirectoriesEqual(DirectoryInfo source, DirectoryInfo target, 
        List<FileSystemInfo> sourceUnique, List<FileSystemInfo> targetUnique)
    {
        var sourceDirectories = new Dictionary<string, DirectoryInfo>(
            source.EnumerateDirectories().Select(d => new KeyValuePair<string, DirectoryInfo>(d.Name, d)));
        foreach (var targetDirectory in target.EnumerateDirectories())
        {
            if (sourceDirectories.Remove(targetDirectory.Name, out var sourceDirectory))
            {
                AssertDirectoriesEqual(sourceDirectory, targetDirectory, sourceUnique, targetUnique);
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
}
