using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests.SynchronizerTests;

public class Test_AllActions : TestBase
{
    private static readonly List<EntryChange> _changes = new()
    {
        new EntryChange(
            new DateTime(2024, 4, 11, 15, 0, 0),
            Path.Combine("folder1", "subfolder1", "ssubfolder1"), 
            EntryType.Directory, EntryAction.Create, 
            null, null),

        new EntryChange(
            new DateTime(2024, 4, 11, 15, 0, 1),
            "folder1", 
            EntryType.Directory, EntryAction.Rename, 
            new RenameProperties("folder1-renamed-source"), null),

        new EntryChange(
            new DateTime(2024, 4, 11, 15, 0, 2),
            "folder2", 
            EntryType.Directory, EntryAction.Delete, 
            null, null),

        new EntryChange(
            new DateTime(2024, 4, 11, 15, 0, 3),
            "file5.xlsx", 
            EntryType.File, EntryAction.Create, 
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2007, 7, 7),
                Length = 888
            }),

        new EntryChange(
            new DateTime(2024, 4, 11, 15, 0, 4),
            "file5.xlsx", 
            EntryType.File, EntryAction.Rename, 
            new RenameProperties("file5-renamed-source.xlsx"), null),

        new EntryChange(
            new DateTime(2024, 4, 11, 15, 0, 5),
            Path.Combine("folder1-renamed-source", "subfolder1", "file1.txt"), 
            EntryType.File, EntryAction.Rename, 
            new RenameProperties("file1-renamed-source.txt"), null),

        new EntryChange(
            new DateTime(2024, 4, 11, 15, 0, 6),
            Path.Combine("folder1-renamed-source", "subfolder1", "file2.csv"), 
            EntryType.File, EntryAction.Change, 
            null, new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 4, 9, 19, 47, 36),
                Length = 3318
            }),

        new EntryChange(
            new DateTime(2024, 4, 11, 15, 0, 7),
            Path.Combine("folder1-renamed-source", "file3"), 
            EntryType.File, EntryAction.Delete, 
            null, null)
    };

    public Test_AllActions() : base(_changes) { }
}
