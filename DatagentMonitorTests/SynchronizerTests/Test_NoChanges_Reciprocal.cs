using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests.SynchronizerTests;

/* This test represents a case when there are events in the database, 
 * but they are reciprocal to each other, so both source and target must not get altered.
 */
public class Test_NoChanges_Reciprocal : TestBase
{
    private static readonly List<EntryChange> _changes = new()
    {
        // Original changes
        new EntryChange(
            new DateTime(2024, 5, 29, 17, 10, 0),
            Path.Combine("folder1", "subfolder1", "temp_folder"),
            FileSystemEntryType.Directory, FileSystemEntryAction.Create,
            null, null),

        new EntryChange(
            new DateTime(2024, 5, 29, 17, 10, 0),
            Path.Combine("folder1", "subfolder1", "temp_folder", "temp_file1"),
            FileSystemEntryType.File, FileSystemEntryAction.Create,
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 5, 29, 17, 10, 0),
                Length = 555
            }),

        new EntryChange(
            new DateTime(2024, 5, 29, 17, 10, 1),
            Path.Combine("folder1", "subfolder1", "temp_file2.tmp"),
            FileSystemEntryType.File, FileSystemEntryAction.Create,
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 5, 29, 17, 10, 1),
                Length = 666
            }),

        new EntryChange(
            new DateTime(2024, 5, 29, 17, 10, 2),
            "folder1", FileSystemEntryType.Directory, FileSystemEntryAction.Rename,
            new RenameProperties("folder1-renamed"), null),

        new EntryChange(
            new DateTime(2024, 5, 29, 17, 10, 3), 
            "file4", 
            FileSystemEntryType.File, FileSystemEntryAction.Rename, 
            new RenameProperties("file4-renamed"), null),

        // Reciprocal changes
        new EntryChange(
            new DateTime(2024, 5, 29, 17, 20, 0),
            Path.Combine("folder1-renamed", "subfolder1", "temp_folder"),
            FileSystemEntryType.Directory, FileSystemEntryAction.Delete,
            null, null),

        new EntryChange(
            new DateTime(2024, 5, 29, 17, 20, 1), 
            Path.Combine("folder1-renamed", "subfolder1", "temp_file2.tmp"), 
            FileSystemEntryType.File, FileSystemEntryAction.Delete, 
            null, null), 

        new EntryChange(
            new DateTime(2024, 5, 29, 17, 20, 2), 
            "folder1-renamed", 
            FileSystemEntryType.Directory, FileSystemEntryAction.Rename, 
            new RenameProperties("folder1"), null), 

        new EntryChange(
            new DateTime(2024, 5, 29, 17, 20, 3), 
            "file4-renamed", 
            FileSystemEntryType.File, FileSystemEntryAction.Rename, 
            new RenameProperties("file4"), null)
    };

    public Test_NoChanges_Reciprocal() : base(_changes) { }
}
