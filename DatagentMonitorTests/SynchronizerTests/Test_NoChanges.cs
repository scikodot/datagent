using DatagentMonitor;
using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests.SynchronizerTests.Test_NoChanges;

public class DateTimeProviderFixture : DateTimeProviderFixtureAbstract
{
    public override IDateTimeProvider DateTimeProvider => DateTimeProviderFactory.FromDateTime(new DateTime(2024, 5, 30));
}

/* This test represents a case when there are no events in the database, 
 * so no changes are to occur to either source or target.
 */
public class Test_NoChanges : TestBase, IClassFixture<DirectoryFixture>, IClassFixture<DateTimeProviderFixture>
{
    protected override IEnumerable<EntryChange> Changes => new List<EntryChange>();

    protected override DateTime? LastSyncTime => null;

    public Test_NoChanges(DirectoryFixture directoryFixture, DateTimeProviderFixture dateTimeProviderFixture) : 
        base(directoryFixture) { }
}
