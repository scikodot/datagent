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
public class Test6 : TestBase
{
    private static List<FileSystemEntryChange> _changes = new()
    {
        new FileSystemEntryChange
        {
            Timestamp = new DateTime(2024, 5, 8, 13, 40, 0),
            Path = Path.Combine("folder1", "file1.txt"),
            Action = FileSystemEntryAction.Change,
            Properties = new FileSystemEntryChangeProperties
            {
                ChangeProps = new ChangeProperties
                {
                    LastWriteTime = new DateTime(2024, 5, 8, 13, 40, 0),
                    Length = 333
                }
            }
        },
        new FileSystemEntryChange
        {
            Timestamp = new DateTime(2024, 5, 8, 13, 40, 1),
            Path = Path.Combine("folder1", "file1.txt"),
            Action = FileSystemEntryAction.Rename,
            Properties = new FileSystemEntryChangeProperties
            {
                RenameProps = new RenameProperties
                {
                    Name = "file1-renamed.txt"
                }
            }
        },
        new FileSystemEntryChange
        {
            Timestamp = new DateTime(2024, 5, 8, 13, 40, 2),
            Path = Path.Combine("folder1", "file1.txt"),
            Action = FileSystemEntryAction.Create,
            Properties = new FileSystemEntryChangeProperties
            {
                ChangeProps = new ChangeProperties
                {
                    LastWriteTime = new DateTime(2024, 5, 8, 13, 40, 2),
                    Length = 444
                }
            }
        }
    };

    public Test6() : base(_changes) { }
}
