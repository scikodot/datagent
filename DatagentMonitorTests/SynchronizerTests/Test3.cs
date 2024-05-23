using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests.SynchronizerTests;

public class Test3 : TestBase
{
    private static readonly List<FileSystemEntryChange> _changes = new()
    {
        new FileSystemEntryChange(
            Path.Combine("folder1", "file1"), 
            FileSystemEntryAction.Rename)
        {
            Timestamp = new DateTime(2024, 4, 19, 13, 58, 0),
            Properties = new FileSystemEntryChangeProperties
            {
                RenameProps = new RenameProperties
                {
                    Name = "file1-renamed-source"
                }
            }
        },
        new FileSystemEntryChange(
            Path.Combine("folder1", "file2"), 
            FileSystemEntryAction.Change)
        {
            Timestamp = new DateTime(2024, 4, 19, 13, 58, 1),
            Properties = new FileSystemEntryChangeProperties
            {
                ChangeProps = new ChangeProperties
                {
                    LastWriteTime = new DateTime(2024, 4, 19, 13, 58, 1),
                    Length = 5432
                }
            }
        }
    };

    public Test3() : base(_changes) { }
}
