using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests.SynchronizerTests;

// Test Delete after Create
public class Test2 : TestBase
{
    private static readonly List<EntryChange> _changes = new()
    {
        new EntryChange(
            "folder2", 
            FileSystemEntryType.Directory, 
            FileSystemEntryAction.Create)
        {
            Timestamp = new DateTime(2024, 4, 19, 13, 0, 0)
        },
        // As we haven't created a real directory, imitate its contents creation
        new EntryChange(
            Path.Combine("folder2", "file4.ffx"), 
            FileSystemEntryType.File, 
            FileSystemEntryAction.Create)
        {
            Timestamp = new DateTime(2024, 4, 19, 13, 0, 0)
        },
        new EntryChange(
            Path.Combine("folder2", "file5.tex"), 
            FileSystemEntryType.File, 
            FileSystemEntryAction.Create)
        {
            Timestamp = new DateTime(2024, 4, 19, 13, 0, 0)
        },
        new EntryChange(
            Path.Combine("folder1", "file3.sfx"), 
            FileSystemEntryType.File, 
            FileSystemEntryAction.Create)
        {
            Timestamp = new DateTime(2024, 4, 19, 13, 0, 1)
        },
        new EntryChange(
            "folder2", 
            FileSystemEntryType.Directory, 
            FileSystemEntryAction.Delete)
        {
            Timestamp = new DateTime(2024, 4, 19, 13, 0, 2)
        },
        new EntryChange(
            Path.Combine("folder1", "file3.sfx"), 
            FileSystemEntryType.File, 
            FileSystemEntryAction.Delete)
        {
            Timestamp = new DateTime(2024, 4, 19, 13, 0, 3),
        }
    };

    public Test2() : base(_changes) {}
}
