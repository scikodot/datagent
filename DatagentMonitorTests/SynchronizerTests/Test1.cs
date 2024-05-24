﻿using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests.SynchronizerTests;

// Test all actions
public class Test1 : TestBase
{
    private static readonly List<NamedEntryChange> _changes = new()
    {
        new NamedEntryChange(
            Path.Combine("folder1", "subfolder1", "ssubfolder1") + Path.DirectorySeparatorChar, 
            FileSystemEntryAction.Create)
        {
            Timestamp = new DateTime(2024, 4, 11, 15, 0, 0)
        },
        new NamedEntryChange(
            "folder1" + Path.DirectorySeparatorChar, 
            FileSystemEntryAction.Rename)
        {
            Timestamp = new DateTime(2024, 4, 11, 15, 0, 1),
            RenameProperties = new RenameProperties("folder1-renamed-source")
        },
        new NamedEntryChange(
            "folder2" + Path.DirectorySeparatorChar, 
            FileSystemEntryAction.Delete)
        {
            Timestamp = new DateTime(2024, 4, 11, 15, 0, 2)
        },
        new NamedEntryChange(
            "file5.xlsx", 
            FileSystemEntryAction.Create)
        {
            Timestamp = new DateTime(2024, 4, 11, 15, 0, 3),
            ChangeProperties = new ChangeProperties
            {
                LastWriteTime = new DateTime(2007, 7, 7),
                Length = 888
            }
        },
        new NamedEntryChange(
            "file5.xlsx", 
            FileSystemEntryAction.Rename)
        {
            Timestamp = new DateTime(2024, 4, 11, 15, 0, 4),
            RenameProperties = new RenameProperties("file5-renamed-source.xlsx")
        },
        new NamedEntryChange(
            Path.Combine("folder1-renamed-source", "subfolder1", "file1.txt"), 
            FileSystemEntryAction.Rename)
        {
            Timestamp = new DateTime(2024, 4, 11, 15, 0, 5),
            RenameProperties = new RenameProperties("file1-renamed-source.txt")
        },
        new NamedEntryChange(
            Path.Combine("folder1-renamed-source", "subfolder1", "file2.csv"), 
            FileSystemEntryAction.Change)
        {
            Timestamp = new DateTime(2024, 4, 11, 15, 0, 6),
            ChangeProperties = new ChangeProperties
            {
                LastWriteTime = new DateTime(2024, 4, 9, 19, 47, 36),
                Length = 3318
            }
        },
        new NamedEntryChange(
            Path.Combine("folder1-renamed-source", "file3"), 
            FileSystemEntryAction.Delete)
        {
            Timestamp = new DateTime(2024, 4, 11, 15, 0, 7),
        }
    };

    public Test1() : base(_changes) { }
}
