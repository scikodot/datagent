using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests.SynchronizerTests;

// Test Delete after Create
public class Test2 : TestBase
{
    private static readonly List<FileSystemEntryChange> _changes = new()
    {
        new FileSystemEntryChange(
            "folder2" + Path.DirectorySeparatorChar, 
            FileSystemEntryAction.Create)
        {
            Timestamp = new DateTime(2024, 4, 19, 13, 0, 0)
        },
        // As we haven't created a real directory, imitate its contents creation
        new FileSystemEntryChange(
            Path.Combine("folder2", "file4.ffx"), 
            FileSystemEntryAction.Create)
        {
            Timestamp = new DateTime(2024, 4, 19, 13, 0, 0)
        },
        new FileSystemEntryChange(
            Path.Combine("folder2", "file5.tex"), 
            FileSystemEntryAction.Create)
        {
            Timestamp = new DateTime(2024, 4, 19, 13, 0, 0)
        },
        new FileSystemEntryChange(
            Path.Combine("folder1", "file3.sfx"), 
            FileSystemEntryAction.Create)
        {
            Timestamp = new DateTime(2024, 4, 19, 13, 0, 1)
        },
        new FileSystemEntryChange(
            "folder2" + Path.DirectorySeparatorChar, 
            FileSystemEntryAction.Delete)
        {
            Timestamp = new DateTime(2024, 4, 19, 13, 0, 2)
        },
        new FileSystemEntryChange(
            Path.Combine("folder1", "file3.sfx"), 
            FileSystemEntryAction.Delete)
        {
            Timestamp = new DateTime(2024, 4, 19, 13, 0, 3),
        }
    };

    public Test2() : base(_changes) {}
}
