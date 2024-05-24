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
    private static readonly List<NamedEntryChange> _changesFolder = new()
    {
        new NamedEntryChange(
            Path.Combine("folder1", "subfolder1"), 
            FileSystemEntryType.Directory, 
            FileSystemEntryAction.Rename)
        {
            Timestamp = new DateTime(2024, 5, 22, 13, 0, 0),
            RenameProperties = new RenameProperties("subfolder1-renamed-source-1")
        },
        new NamedEntryChange(
            Path.Combine("folder1", "subfolder1-renamed-source-1"), 
            FileSystemEntryType.Directory, 
            FileSystemEntryAction.Rename)
        {
            Timestamp = new DateTime(2024, 5, 22, 14, 0, 0),
            RenameProperties = new RenameProperties("subfolder1-renamed-source-2")
        },
        new NamedEntryChange(
            Path.Combine("folder1", "subfolder1-renamed-source-2"), 
            FileSystemEntryType.Directory, 
            FileSystemEntryAction.Rename)
        {
            Timestamp = new DateTime(2024, 5, 22, 15, 0, 0),
            RenameProperties = new RenameProperties("subfolder1")
        },
        new NamedEntryChange(
            Path.Combine("folder1", "subfolder1"), 
            FileSystemEntryType.Directory, 
            FileSystemEntryAction.Rename)
        {
            Timestamp = new DateTime(2024, 5, 22, 16, 0, 0),
            RenameProperties = new RenameProperties("subfolder1-renamed-source-3")
        },
        new NamedEntryChange(
            Path.Combine("folder1", "subfolder1-renamed-source-3"), 
            FileSystemEntryType.Directory, 
            FileSystemEntryAction.Rename)
        {
            Timestamp = new DateTime(2024, 5, 22, 17, 0, 0),
            RenameProperties = new RenameProperties("subfolder1-renamed-source-4")
        }
    };

    private static readonly List<NamedEntryChange> _changesFileCycle = new()
    {
        new NamedEntryChange(
            Path.Combine("folder1", "subfolder1-renamed-source-4", "file1.txt"), 
            FileSystemEntryType.File, 
            FileSystemEntryAction.Rename)
        {
            Timestamp = new DateTime(2024, 5, 22, 13, 20, 0),
            RenameProperties = new RenameProperties("file1-renamed-source-1.txt")
        },
        new NamedEntryChange(
            Path.Combine("folder1", "subfolder1-renamed-source-4", "file1-renamed-source-1.txt"), 
            FileSystemEntryType.File, 
            FileSystemEntryAction.Rename)
        {
            Timestamp = new DateTime(2024, 5, 22, 14, 20, 0),
            RenameProperties = new RenameProperties("file1-renamed-source-2.txt")
        },
        new NamedEntryChange(
            Path.Combine("folder1", "subfolder1-renamed-source-4", "file1-renamed-source-2.txt"), 
            FileSystemEntryType.File, 
            FileSystemEntryAction.Rename)
        {
            Timestamp = new DateTime(2024, 5, 22, 15, 20, 0),
            RenameProperties = new RenameProperties("file1.txt")
        }
    };

    private static readonly List<NamedEntryChange> _changesFileRepeat = new()
    {
        new NamedEntryChange(
            Path.Combine("folder1", "file3.std"), 
            FileSystemEntryType.File, 
            FileSystemEntryAction.Rename)
        {
            Timestamp = new DateTime(2024, 5, 22, 13, 40, 0),
            RenameProperties = new RenameProperties("file3-renamed-source-1.std")
        },
        new NamedEntryChange(
            Path.Combine("folder1", "file3-renamed-source-1.std"), 
            FileSystemEntryType.File, 
            FileSystemEntryAction.Rename)
        {
            Timestamp = new DateTime(2024, 5, 22, 14, 40, 0),
            RenameProperties = new RenameProperties("file3-renamed-source-2.std")
        },
        new NamedEntryChange(
            Path.Combine("folder1", "file3-renamed-source-2.std"), 
            FileSystemEntryType.File, 
            FileSystemEntryAction.Rename)
        {
            Timestamp = new DateTime(2024, 5, 22, 15, 40, 0),
            RenameProperties = new RenameProperties("file3.std")
        },
        new NamedEntryChange(
            Path.Combine("folder1", "file3.std"), 
            FileSystemEntryType.File, 
            FileSystemEntryAction.Rename)
        {
            Timestamp = new DateTime(2024, 5, 22, 16, 40, 0),
            RenameProperties = new RenameProperties("file3-renamed-source-1.std")
        },
        new NamedEntryChange(
            Path.Combine("folder1", "file3-renamed-source-1.std"), 
            FileSystemEntryType.File, 
            FileSystemEntryAction.Rename)
        {
            Timestamp = new DateTime(2024, 5, 22, 17, 40, 0),
            RenameProperties = new RenameProperties("file3-renamed-source-2.std")
        }
    };

    public Test7() : base(_changesFolder.Concat(
                          _changesFileCycle.Concat(
                          _changesFileRepeat))) { }
}
