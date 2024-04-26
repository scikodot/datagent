using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests.SynchronizerTests;

public class Test3 : TestBase
{
    private static readonly List<FileSystemEntryChange> _changes = new()
    {
        new FileSystemEntryChange
        {
            Timestamp = new DateTime(2024, 4, 19, 13, 58, 0),
            Path = Path.Combine("folder1", "file1"),
            Action = FileSystemEntryAction.Rename,
            Properties = new FileSystemEntryChangeProperties
            {
                RenameProps = new RenameProperties
                {
                    Name = "file1-renamed-source"
                }
            }
        },
        new FileSystemEntryChange
        {
            Timestamp = new DateTime(2024, 4, 19, 13, 58, 1),
            Path = Path.Combine("folder1", "file2"),
            Action = FileSystemEntryAction.Change,
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
