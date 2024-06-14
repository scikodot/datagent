﻿using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests.SynchronizerTests;

/* This test represents a case when there are no events in the database, 
 * so no changes are to occur to either source or target.
 */
public class Test_NoChanges : TestBase, IClassFixture<DirectoryFixture>
{
    protected override IEnumerable<EntryChange> Changes => new List<EntryChange>();

    protected override DateTime? LastSyncTime => null;

    public Test_NoChanges(DirectoryFixture df) : base(df) { }
}
