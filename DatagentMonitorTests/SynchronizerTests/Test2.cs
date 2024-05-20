using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests.SynchronizerTests;

// Test Delete after Create
public class Test2 : TestBase
{
    private static readonly List<FileSystemEntryChange> _changes = new()
    {
        new FileSystemEntryChange
        {
            Timestamp = new DateTime(2024, 4, 19, 13, 0, 0),
            Path = "folder2" + Path.DirectorySeparatorChar,
            Action = FileSystemEntryAction.Create
        },
        // As we haven't created a real directory, imitate its contents creation
        new FileSystemEntryChange
        {
            Timestamp = new DateTime(2024, 4, 19, 13, 0, 0),
            Path = Path.Combine("folder2", "file4.ffx"),
            Action = FileSystemEntryAction.Create
        },
        new FileSystemEntryChange
        {
            Timestamp = new DateTime(2024, 4, 19, 13, 0, 0),
            Path = Path.Combine("folder2", "file5.tex"),
            Action = FileSystemEntryAction.Create
        },
        new FileSystemEntryChange
        {
            Timestamp = new DateTime(2024, 4, 19, 13, 0, 1),
            Path = Path.Combine("folder1", "file3.sfx"),
            Action = FileSystemEntryAction.Create
        },
        new FileSystemEntryChange
        {
            Timestamp = new DateTime(2024, 4, 19, 13, 0, 2),
            Path = "folder2" + Path.DirectorySeparatorChar,
            Action = FileSystemEntryAction.Delete
        },
        new FileSystemEntryChange
        {
            Timestamp = new DateTime(2024, 4, 19, 13, 0, 3),
            Path = Path.Combine("folder1", "file3.sfx"),
            Action = FileSystemEntryAction.Delete
        }
    };

    public Test2() : base(_changes) {}
}
