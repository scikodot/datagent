using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests.SynchronizerTests;

public class Test5 : TestBase
{
    private static readonly List<NamedEntryChange> _changes = new()
    {
        new NamedEntryChange(
            "file1", 
            FileSystemEntryAction.Rename)
        {
            Timestamp = new DateTime(2024, 5, 2, 13, 56, 0),
            RenameProperties = new RenameProperties("file1-renamed-source")
        },
        new NamedEntryChange(
            "file1", 
            FileSystemEntryAction.Create)
        {
            Timestamp = new DateTime(2024, 5, 2, 13, 56, 1),
            ChangeProperties = new ChangeProperties
            {
                LastWriteTime = new DateTime(2077, 1, 1, 0, 0, 0),
                Length = 256
            }
        }
    };

    public Test5() : base(_changes) { }
}
