using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests.SynchronizerTests;

public class Test_DeleteAfterCreate : TestBase
{
    private static readonly List<EntryChange> _changes = new()
    {
        new EntryChange(
            new DateTime(2024, 4, 19, 13, 0, 0),
            "folder2", 
            FileSystemEntryType.Directory, FileSystemEntryAction.Create, 
            null, null),

        new EntryChange(
            new DateTime(2024, 4, 19, 13, 0, 0),
            Path.Combine("folder2", "file4.ffx"), 
            FileSystemEntryType.File, FileSystemEntryAction.Create, 
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 4, 19, 13, 0, 0),
                Length = 222
            }),

        new EntryChange(
            new DateTime(2024, 4, 19, 13, 0, 0),
            Path.Combine("folder2", "file5.tex"), 
            FileSystemEntryType.File, FileSystemEntryAction.Create, 
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 4, 19, 13, 0, 0),
                Length = 444
            }),

        new EntryChange(
            new DateTime(2024, 4, 19, 13, 0, 1),
            Path.Combine("folder1", "file3.sfx"), 
            FileSystemEntryType.File, FileSystemEntryAction.Create, 
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 4, 19, 13, 0, 1),
                Length = 666
            }),

        new EntryChange(
            new DateTime(2024, 4, 19, 13, 0, 2),
            "folder2", 
            FileSystemEntryType.Directory, FileSystemEntryAction.Delete, 
            null, null),

        new EntryChange(
            new DateTime(2024, 4, 19, 13, 0, 3),
            Path.Combine("folder1", "file3.sfx"), 
            FileSystemEntryType.File, FileSystemEntryAction.Delete, 
            null, null)
    };

    public Test_DeleteAfterCreate() : base(_changes) {}
}
