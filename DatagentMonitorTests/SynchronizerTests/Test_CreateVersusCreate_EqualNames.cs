using DatagentMonitor;
using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests.SynchronizerTests.Test_CreateVersusCreate_EqualNames;

public class DateTimeProviderFixture : DateTimeProviderFixtureAbstract
{
    public override IDateTimeProvider DateTimeProvider => DateTimeProviderFactory.FromDateTime(new DateTime(2024, 11, 21));
}

/* This test represents a situation when there are two entries created, one on the source 
 * and another on the target, and they have equal names.
 * 
 * For directories, it is not a conflict, and both of them can be merged together if needed.
 * 
 * For files, it is a conflict and must be resolved in either of three ways:
 * 1. Pick only one of the two files and remove the other
 * 2. Merge both files into one if possible
 * 3. Keep both files by renaming one of them
 */
public class Test_CreateVersusCreate_EqualNames : TestBase, IClassFixture<DirectoryFixture>, IClassFixture<DateTimeProviderFixture>
{
    protected override IEnumerable<EntryChange> Changes => throw new NotImplementedException();

    protected override DateTime? LastSyncTime => throw new NotImplementedException();

    public Test_CreateVersusCreate_EqualNames(DirectoryFixture directoryFixture, DateTimeProviderFixture dateTimeProviderFixture) :
        base(directoryFixture) { }
}
