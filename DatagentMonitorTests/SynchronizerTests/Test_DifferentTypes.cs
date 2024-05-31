using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests.SynchronizerTests;

public class Test_DifferentTypes : TestBase
{
    protected override IEnumerable<EntryChange> Changes => new List<EntryChange>
    {
        // Empty folder
        new EntryChange(
            new DateTime(2024, 5, 29, 18, 00, 0),
            Path.Combine("folder1", "entry1"),
            EntryType.Directory, EntryAction.Create,
            null, null),

        // Folder with a lower priority than the target
        new EntryChange(
            new DateTime(2024, 5, 29, 18, 10, 0),
            "entry2",
            EntryType.Directory, EntryAction.Create,
            null, null),

        new EntryChange(
            new DateTime(2024, 5, 29, 18, 10, 0),
            Path.Combine("entry2", "file4"),
            EntryType.File, EntryAction.Create,
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 5, 29, 18, 10, 0),
                Length = 444
            }),

        // Folder with a higher priority than the target
        new EntryChange(
            new DateTime(2024, 5, 29, 18, 50, 0),
            "entry3",
            EntryType.Directory, EntryAction.Create,
            null, null),

        new EntryChange(
            new DateTime(2024, 5, 29, 18, 50, 0),
            Path.Combine("entry3", "file5"),
            EntryType.File, EntryAction.Create,
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 5, 29, 18, 50, 0),
                Length = 555
            })
    };

    protected override DateTime? LastSyncTime => null;
}
