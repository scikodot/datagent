using DatagentMonitor.FileSystem;

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
public class Test_CreateAfterRename_NoRevert : TestBase, IClassFixture<DirectoryFixture>
{
    protected override IEnumerable<EntryChange> Changes => new List<EntryChange>
    {
        new EntryChange(
            new DateTime(2024, 5, 8, 3, 0, 0),
            Path.Combine("folder1", "file1.txt"), 
            EntryType.File, EntryAction.Change, 
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 5, 8, 3, 00, 0),
                Length = 1150
            }),

        new EntryChange(
            new DateTime(2024, 5, 8, 4, 0, 0),
            Path.Combine("folder1", "file1.txt"), 
            EntryType.File, EntryAction.Rename, 
            new RenameProperties("file1-renamed.txt"), null),

        new EntryChange(
            new DateTime(2024, 5, 8, 5, 0, 0),
            Path.Combine("folder1", "file1.txt"), 
            EntryType.File, EntryAction.Create, 
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 5, 8, 5, 0, 0),
                Length = 2100
            })
    };

    protected override DateTime? LastSyncTime => null;

    public Test_CreateAfterRename_NoRevert(DirectoryFixture df) : base(df) { }
}
