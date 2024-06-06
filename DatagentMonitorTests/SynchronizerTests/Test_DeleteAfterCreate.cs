using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests.SynchronizerTests;

public class Test_DeleteAfterCreate : TestBase
{
    protected override IEnumerable<EntryChange> Changes => new List<EntryChange>
    {
        new EntryChange(
            new DateTime(2024, 4, 19, 4, 0, 0),
            "folder2", 
            EntryType.Directory, EntryAction.Create, 
            null, null),

        new EntryChange(
            new DateTime(2024, 4, 19, 4, 0, 0),
            Path.Combine("folder2", "file4.ffx"), 
            EntryType.File, EntryAction.Create, 
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 4, 19, 4, 0, 0),
                Length = 400
            }),

        new EntryChange(
            new DateTime(2024, 4, 19, 4, 0, 0),
            Path.Combine("folder2", "file5.tex"), 
            EntryType.File, EntryAction.Create, 
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 4, 19, 4, 0, 0),
                Length = 500
            }),

        new EntryChange(
            new DateTime(2024, 4, 19, 5, 0, 0),
            Path.Combine("folder1", "file3.sfx"), 
            EntryType.File, EntryAction.Create, 
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 4, 19, 5, 0, 0),
                Length = 1300
            }),

        new EntryChange(
            new DateTime(2024, 4, 19, 6, 0, 0),
            "folder2", 
            EntryType.Directory, EntryAction.Delete, 
            null, null),

        new EntryChange(
            new DateTime(2024, 4, 19, 7, 0, 0),
            Path.Combine("folder1", "file3.sfx"), 
            EntryType.File, EntryAction.Delete, 
            null, null)
    };

    protected override DateTime? LastSyncTime => null;
}
