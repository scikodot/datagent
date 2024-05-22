using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests.SynchronizerTests;

/* This test represents a sequence of renames for 1 folder and 2 files.
 * All of those sequences involve renaming back to the original names at some point.
 * Folder: a -> b -> c -> a -> d -> e
 * File (cycle): a -> b -> c -> a
 * File (repeat): a -> b -> c -> a -> b -> c
*/
public class Test7 : TestBase
{
    private static readonly List<FileSystemEntryChange> _changesFolder = new()
    {
        new FileSystemEntryChange
        {
            Path = Path.Combine("folder1", "subfolder1") + Path.DirectorySeparatorChar,
            Action = FileSystemEntryAction.Rename,
            Timestamp = new DateTime(2024, 5, 22, 13, 0, 0),
            Properties = new FileSystemEntryChangeProperties
            {
                RenameProps = new RenameProperties
                {
                    Name = "subfolder1-renamed-source-1"
                }
            }
        },
        new FileSystemEntryChange
        {
            Path = Path.Combine("folder1", "subfolder1-renamed-source-1") + Path.DirectorySeparatorChar,
            Action = FileSystemEntryAction.Rename,
            Timestamp = new DateTime(2024, 5, 22, 14, 0, 0),
            Properties = new FileSystemEntryChangeProperties
            {
                RenameProps = new RenameProperties
                {
                    Name = "subfolder1-renamed-source-2"
                }
            }
        },
        new FileSystemEntryChange
        {
            Path = Path.Combine("folder1", "subfolder1-renamed-source-2") + Path.DirectorySeparatorChar,
            Action = FileSystemEntryAction.Rename,
            Timestamp = new DateTime(2024, 5, 22, 15, 0, 0),
            Properties = new FileSystemEntryChangeProperties
            {
                RenameProps = new RenameProperties
                {
                    Name = "subfolder1"
                }
            }
        },
        new FileSystemEntryChange
        {
            Path = Path.Combine("folder1", "subfolder1") + Path.DirectorySeparatorChar,
            Action = FileSystemEntryAction.Rename,
            Timestamp = new DateTime(2024, 5, 22, 16, 0, 0),
            Properties = new FileSystemEntryChangeProperties
            {
                RenameProps = new RenameProperties
                {
                    Name = "subfolder1-renamed-source-3"
                }
            }
        },
        new FileSystemEntryChange
        {
            Path = Path.Combine("folder1", "subfolder1-renamed-source-3") + Path.DirectorySeparatorChar,
            Action = FileSystemEntryAction.Rename,
            Timestamp = new DateTime(2024, 5, 22, 17, 0, 0),
            Properties = new FileSystemEntryChangeProperties
            {
                RenameProps = new RenameProperties
                {
                    Name = "subfolder1-renamed-source-4"
                }
            }
        }
    };

    private static readonly List<FileSystemEntryChange> _changesFileCycle = new()
    {
        new FileSystemEntryChange
        {
            Path = Path.Combine("folder1", "subfolder1-renamed-source-4", "file1.txt"),
            Action = FileSystemEntryAction.Rename,
            Timestamp = new DateTime(2024, 5, 22, 13, 20, 0),
            Properties = new FileSystemEntryChangeProperties
            {
                RenameProps = new RenameProperties
                {
                    Name = "file1-renamed-source-1.txt"
                }
            }
        },
        new FileSystemEntryChange
        {
            Path = Path.Combine("folder1", "subfolder1-renamed-source-4", "file1-renamed-source-1.txt"),
            Action = FileSystemEntryAction.Rename,
            Timestamp = new DateTime(2024, 5, 22, 14, 20, 0),
            Properties = new FileSystemEntryChangeProperties
            {
                RenameProps = new RenameProperties
                {
                    Name = "file1-renamed-source-2.txt"
                }
            }
        },
        new FileSystemEntryChange
        {
            Path = Path.Combine("folder1", "subfolder1-renamed-source-4", "file1-renamed-source-2.txt"),
            Action = FileSystemEntryAction.Rename,
            Timestamp = new DateTime(2024, 5, 22, 15, 20, 0),
            Properties = new FileSystemEntryChangeProperties
            {
                RenameProps = new RenameProperties
                {
                    Name = "file1.txt"
                }
            }
        }
    };

    private static readonly List<FileSystemEntryChange> _changesFileRepeat = new()
    {
        new FileSystemEntryChange
        {
            Path = Path.Combine("folder1", "file3.std"),
            Action = FileSystemEntryAction.Rename,
            Timestamp = new DateTime(2024, 5, 22, 13, 40, 0),
            Properties = new FileSystemEntryChangeProperties
            {
                RenameProps = new RenameProperties
                {
                    Name = "file3-renamed-source-1.std"
                }
            }
        },
        new FileSystemEntryChange
        {
            Path = Path.Combine("folder1", "file3-renamed-source-1.std"),
            Action = FileSystemEntryAction.Rename,
            Timestamp = new DateTime(2024, 5, 22, 14, 40, 0),
            Properties = new FileSystemEntryChangeProperties
            {
                RenameProps = new RenameProperties
                {
                    Name = "file3-renamed-source-2.std"
                }
            }
        },
        new FileSystemEntryChange
        {
            Path = Path.Combine("folder1", "file3-renamed-source-2.std"),
            Action = FileSystemEntryAction.Rename,
            Timestamp = new DateTime(2024, 5, 22, 15, 40, 0),
            Properties = new FileSystemEntryChangeProperties
            {
                RenameProps = new RenameProperties
                {
                    Name = "file3.std"
                }
            }
        },
        new FileSystemEntryChange
        {
            Path = Path.Combine("folder1", "file3.std"),
            Action = FileSystemEntryAction.Rename,
            Timestamp = new DateTime(2024, 5, 22, 16, 40, 0),
            Properties = new FileSystemEntryChangeProperties
            {
                RenameProps = new RenameProperties
                {
                    Name = "file3-renamed-source-1.std"
                }
            }
        },
        new FileSystemEntryChange
        {
            Path = Path.Combine("folder1", "file3-renamed-source-1.std"),
            Action = FileSystemEntryAction.Rename,
            Timestamp = new DateTime(2024, 5, 22, 17, 40, 0),
            Properties = new FileSystemEntryChangeProperties
            {
                RenameProps = new RenameProperties
                {
                    Name = "file3-renamed-source-2.std"
                }
            }
        }
    };

    public Test7() : base(_changesFolder.Concat(
                          _changesFileCycle.Concat(
                          _changesFileRepeat))) { }
}
