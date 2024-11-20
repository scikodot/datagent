using DatagentMonitor;
using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests.SynchronizerTests.Test_RenameVersusCreate_EqualNames;

public class DateTimeProviderFixture : DateTimeProviderFixtureAbstract
{
    public override IDateTimeProvider DateTimeProvider => DateTimeProviderFactory.FromDateTime(new DateTime(2024, 11, 21));
}

/* This test represents a situation where one entry is Renamed on the source, 
 * another entry is Created on the target, and both entries have equal final names.
 * 
 * For directories, it is not a conflict, and both of them can be merged together if needed.
 * 
 * For files, it is a conflict, since renaming the old file on the target is not directly possible; its new name is already taken.
 * Such a conflict must be resolved in any of two ways:
 * 1. Apply the source's Rename by renaming the target's Created file to some other name
 *    (manual resolve only, since we can't choose a new name without user consent)
 * 2. Discard the source's Rename
 */
public class Test_RenameVersusCreate_EqualNames : TestBase, IClassFixture<DirectoryFixture>, IClassFixture<DateTimeProviderFixture>
{
    protected override IEnumerable<EntryChange> Changes => new List<EntryChange>()
    {
        // Directory
        new EntryChange(
            new DateTime(2024, 11, 20, 10, 0, 0),
            "folder2",
            EntryType.Directory, EntryAction.Rename,
            new RenameProperties("folder2-renamed-source"), null),

        // File
        new EntryChange(
            new DateTime(2024, 11, 20, 11, 0, 0),
            Path.Combine("folder1", "file1.txt"),
            EntryType.File, EntryAction.Rename,
            new RenameProperties("file1-renamed-source.txt"), null),
    };

    protected override DateTime? LastSyncTime => null;

    public Test_RenameVersusCreate_EqualNames(DirectoryFixture directoryFixture, DateTimeProviderFixture dateTimeProviderFixture) :
        base(directoryFixture) { }
}
