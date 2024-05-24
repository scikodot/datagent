using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests.SynchronizerTests;

public class Test3 : TestBase
{
    private static readonly List<NamedEntryChange> _changes = new()
    {
        new NamedEntryChange(
            Path.Combine("folder1", "file1"), 
            FileSystemEntryAction.Rename)
        {
            Timestamp = new DateTime(2024, 4, 19, 13, 58, 0),
            RenameProperties = new RenameProperties("file1-renamed-source")
        },
        new NamedEntryChange(
            Path.Combine("folder1", "file2"), 
            FileSystemEntryAction.Change)
        {
            Timestamp = new DateTime(2024, 4, 19, 13, 58, 1),
            ChangeProperties = new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 4, 19, 13, 58, 1),
                Length = 5432
            }
        }
    };

    public Test3() : base(_changes) { }
}
