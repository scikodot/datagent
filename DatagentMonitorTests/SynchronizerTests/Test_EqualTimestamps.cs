using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests.SynchronizerTests;

/* This test represents a case when the changes happen at the same time on both source and target. 
 * By default, if two changes have the same timestamp, the source's one is favoured.
 * 
 * Note: if two directories are compared, and one of the changes is Delete, 
 * then they are compared by their priority values, not by their own changes (if those exist).
 */
public class Test_EqualTimestamps : TestBase
{
    protected override IEnumerable<EntryChange> Changes => new List<EntryChange>
    {
        // Renamed folder that is deleted on the target
        new EntryChange(
            new DateTime(2024, 05, 30, 22, 00, 0), 
            Path.Combine("folder1", "subfolder1"), 
            EntryType.Directory, EntryAction.Rename, 
            new RenameProperties("subfolder1-renamed-source"), null), 

        new EntryChange(
            new DateTime(2024, 05, 30, 22, 30, 0), 
            Path.Combine("folder1", "subfolder1-renamed-source", "file2.csv"), 
            EntryType.File, EntryAction.Change, 
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 05, 30, 22, 00, 0),
                Length = 2222
            }),

        // Not renamed folder that is deleted on the target
        new EntryChange(
            new DateTime(2024, 05, 30, 22, 00, 0), 
            Path.Combine("folder1", "subfolder2", "file3.scp"), 
            EntryType.File, EntryAction.Rename, 
            new RenameProperties("file3-renamed-source.scp"), null), 

        new EntryChange(
            new DateTime(2024, 05, 30, 22, 30, 0), 
            Path.Combine("folder1", "subfolder2", "file4.css"), 
            EntryType.File, EntryAction.Change, 
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 05, 30, 22, 30, 0),
                Length = 4444
            }),

        // A new file that is created the same time as the one on the target
        new EntryChange(
            new DateTime(2024, 05, 30, 23, 0, 0), 
            "file7.cpp", EntryType.File, EntryAction.Create, null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 05, 30, 23, 0, 0), 
                Length = 1777
            })
    };

    // This is the same as the non-renamed folder's priority value
    protected override DateTime? LastSyncTime => new DateTime(2024, 05, 30, 22, 30, 0);
}
