﻿using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests.SynchronizerTests;

/* This test represents a conflict when a file has been changed (and renamed!) on the source
and its old name has got used by a new file, while on the target the same old file 
has only been changed.

The conflict arises from the fact that the target change priority is higher 
than that of the source change.

This test ensures that, even though the files are conflicting, the renamed file's name will be used 
for the resulting file.

If that was not the case (e.g. discard renamed file), the new file (that used the old name) 
would be in a volatile state, as its name would have to be taken back.
*/
public class Test6 : TestBase
{
    private static readonly List<NamedEntryChange> _changes = new()
    {
        new NamedEntryChange(
            Path.Combine("folder1", "file1.txt"), 
            FileSystemEntryAction.Change)
        {
            Timestamp = new DateTime(2024, 5, 8, 13, 40, 0),
            ChangeProperties = new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 5, 8, 13, 40, 0),
                Length = 333
            }
        },
        new NamedEntryChange(
            Path.Combine("folder1", "file1.txt"), 
            FileSystemEntryAction.Rename)
        {
            Timestamp = new DateTime(2024, 5, 8, 13, 40, 1),
            RenameProperties = new RenameProperties("file1-renamed.txt")
        },
        new NamedEntryChange(
            Path.Combine("folder1", "file1.txt"), 
            FileSystemEntryAction.Create)
        {
            Timestamp = new DateTime(2024, 5, 8, 13, 40, 2),
            ChangeProperties = new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 5, 8, 13, 40, 2),
                Length = 444
            }
        }
    };

    public Test6() : base(_changes) { }
}
