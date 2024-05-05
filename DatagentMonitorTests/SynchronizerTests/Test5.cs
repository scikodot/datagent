﻿using DatagentMonitor.FileSystem;

namespace DatagentMonitorTests.SynchronizerTests;

public class Test5 : TestBase
{
    private static readonly List<FileSystemEntryChange> _changes = new()
    {
        new FileSystemEntryChange
        {
            Timestamp = new DateTime(2024, 5, 2, 13, 56, 0),
            Path = "file1",
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
            Timestamp = new DateTime(2024, 5, 2, 13, 56, 1),
            Path = "file1",
            Action = FileSystemEntryAction.Create,
            Properties = new FileSystemEntryChangeProperties
            {
                ChangeProps = new ChangeProperties
                {
                    LastWriteTime = new DateTime(2077, 1, 1, 0, 0, 0),
                    Length = 256
                }
            }
        }
    };

    public Test5() : base(_changes) { }
}