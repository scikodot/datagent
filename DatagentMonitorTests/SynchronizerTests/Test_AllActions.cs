﻿using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests.SynchronizerTests;

public class Test_AllActions : TestBase
{
    protected override IEnumerable<EntryChange> Changes => new List<EntryChange>
    {
        new EntryChange(
            new DateTime(2024, 4, 11, 0, 0, 0),
            Path.Combine("folder1", "subfolder1", "ssubfolder1"), 
            EntryType.Directory, EntryAction.Create, 
            null, null),

        new EntryChange(
            new DateTime(2024, 4, 11, 1, 0, 0),
            "folder1", 
            EntryType.Directory, EntryAction.Rename, 
            new RenameProperties("folder1-renamed-source"), null),

        new EntryChange(
            new DateTime(2024, 4, 11, 2, 0, 0),
            "folder2", 
            EntryType.Directory, EntryAction.Delete, 
            null, null),

        new EntryChange(
            new DateTime(2024, 4, 11, 3, 0, 0),
            "file5.xlsx", 
            EntryType.File, EntryAction.Create, 
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2007, 7, 7),
                Length = 777
            }),

        new EntryChange(
            new DateTime(2024, 4, 11, 4, 0, 0),
            "file5.xlsx", 
            EntryType.File, EntryAction.Rename, 
            new RenameProperties("file5-renamed-source.xlsx"), null),

        new EntryChange(
            new DateTime(2024, 4, 11, 5, 0, 0),
            Path.Combine("folder1-renamed-source", "subfolder1", "file1.txt"), 
            EntryType.File, EntryAction.Rename, 
            new RenameProperties("file1-renamed-source.txt"), null),

        new EntryChange(
            new DateTime(2024, 4, 11, 6, 0, 0),
            Path.Combine("folder1-renamed-source", "subfolder1", "file2.csv"), 
            EntryType.File, EntryAction.Change, 
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 4, 11, 6, 0, 0),
                Length = 250
            }),

        new EntryChange(
            new DateTime(2024, 4, 11, 7, 0, 0),
            Path.Combine("folder1-renamed-source", "file3"), 
            EntryType.File, EntryAction.Delete, 
            null, null)
    };

    // If a Delete operation on the target:
    // 1. Does not have a conflicting counterpart change on the source
    // 2. Does not have a timestamp due to the absence of LastSyncTime
    //
    // ...it will break MergeChanges routine.
    //
    // This timestamp is only used here to counter that.
    // TODO: remove this when Delete's without LastSyncTime will start using DateTime.Now
    protected override DateTime? LastSyncTime => new DateTime(2024, 4, 10);
}
