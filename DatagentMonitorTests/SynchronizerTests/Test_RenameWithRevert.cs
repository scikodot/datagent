using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests.SynchronizerTests;

/* This test represents a sequence of renames for 1 folder and 2 files.
 * All of those sequences involve renaming back to the original names at some point.
 * Folder: a -> b -> c -> a -> d -> e
 * File (cycle): a -> b -> c -> a
 * File (repeat): a -> b -> c -> a -> b -> c
*/
public class Test_RenameWithRevert : TestBase
{
    private static readonly List<EntryChange> _changesFolder = new()
    {
        new EntryChange(
            new DateTime(2024, 5, 22, 5, 0, 0),
            Path.Combine("folder1", "subfolder1"), 
            EntryType.Directory, EntryAction.Rename, 
            new RenameProperties("subfolder1-renamed-source-1"), null),

        new EntryChange(
            new DateTime(2024, 5, 22, 6, 0, 0),
            Path.Combine("folder1", "subfolder1-renamed-source-1"), 
            EntryType.Directory, EntryAction.Rename, 
            new RenameProperties("subfolder1-renamed-source-2"), null),

        new EntryChange(
            new DateTime(2024, 5, 22, 7, 0, 0),
            Path.Combine("folder1", "subfolder1-renamed-source-2"), 
            EntryType.Directory, EntryAction.Rename, 
            new RenameProperties("subfolder1"), null),

        new EntryChange(
            new DateTime(2024, 5, 22, 8, 0, 0),
            Path.Combine("folder1", "subfolder1"), 
            EntryType.Directory, EntryAction.Rename, 
            new RenameProperties("subfolder1-renamed-source-3"), null),

        new EntryChange(
            new DateTime(2024, 5, 22, 9, 0, 0),
            Path.Combine("folder1", "subfolder1-renamed-source-3"), 
            EntryType.Directory, EntryAction.Rename, 
            new RenameProperties("subfolder1-renamed-source-4"), null)
    };

    private static readonly List<EntryChange> _changesFileCycle = new()
    {
        new EntryChange(
            new DateTime(2024, 5, 22, 5, 20, 0),
            Path.Combine("folder1", "subfolder1-renamed-source-4", "file1.txt"), 
            EntryType.File, EntryAction.Rename, 
            new RenameProperties("file1-renamed-source-1.txt"), null),

        new EntryChange(
            new DateTime(2024, 5, 22, 6, 20, 0),
            Path.Combine("folder1", "subfolder1-renamed-source-4", "file1-renamed-source-1.txt"), 
            EntryType.File, EntryAction.Rename, 
            new RenameProperties("file1-renamed-source-2.txt"), null),

        new EntryChange(
            new DateTime(2024, 5, 22, 7, 20, 0),
            Path.Combine("folder1", "subfolder1-renamed-source-4", "file1-renamed-source-2.txt"), 
            EntryType.File, EntryAction.Rename, 
            new RenameProperties("file1.txt"), null)
    };

    private static readonly List<EntryChange> _changesFileRepeat = new()
    {
        new EntryChange(
            new DateTime(2024, 5, 22, 5, 40, 0),
            Path.Combine("folder1", "file3.std"), 
            EntryType.File, EntryAction.Rename, 
            new RenameProperties("file3-renamed-source-1.std"), null),

        new EntryChange(
            new DateTime(2024, 5, 22, 6, 40, 0),
            Path.Combine("folder1", "file3-renamed-source-1.std"), 
            EntryType.File, EntryAction.Rename, 
            new RenameProperties("file3-renamed-source-2.std"), null),

        new EntryChange(
            new DateTime(2024, 5, 22, 7, 40, 0),
            Path.Combine("folder1", "file3-renamed-source-2.std"), 
            EntryType.File, EntryAction.Rename, 
            new RenameProperties("file3.std"), null),

        new EntryChange(
            new DateTime(2024, 5, 22, 8, 40, 0),
            Path.Combine("folder1", "file3.std"), 
            EntryType.File, EntryAction.Rename, 
            new RenameProperties("file3-renamed-source-1.std"), null),

        new EntryChange(
            new DateTime(2024, 5, 22, 9, 40, 0),
            Path.Combine("folder1", "file3-renamed-source-1.std"), 
            EntryType.File, EntryAction.Rename, 
            new RenameProperties("file3-renamed-source-2.std"), null)
    };

    protected override IEnumerable<EntryChange> Changes =>
        _changesFolder.Concat(_changesFileCycle.Concat(_changesFileRepeat));

    protected override DateTime? LastSyncTime => null;
}
