﻿namespace DatagentMonitor.FileSystem;

public delegate void CustomRenameEventHandler(object sender, CustomRenameEventArgs e);

public record class CustomRenameEventArgs(string OldName, string Name);

public abstract class CustomFileSystemInfo
{
    public event CustomRenameEventHandler? NamePropertyChanged;

    public abstract EntryType Type { get; }

    protected string _name;
    public string Name
    {
        get => _name;
        set
        {
            if (value != _name)
            {
                NamePropertyChanged?.Invoke(this, new CustomRenameEventArgs(_name, value));
                _name = value;
            }
        }
    }

    private DateTime _lastWriteTime;
    public DateTime LastWriteTime
    {
        get => _lastWriteTime;
        set => _lastWriteTime = value;
    }

    protected CustomFileSystemInfo(string name, DateTime lastWriteTime)
    {
        _name = name;
        _lastWriteTime = lastWriteTime;
    }

    protected CustomFileSystemInfo(FileSystemInfo info)
    {
        if (!info.Exists)
            throw info switch
            {
                DirectoryInfo => new DirectoryNotFoundException(info.FullName),
                FileInfo => new FileNotFoundException(info.FullName)
            };

        _name = info.Name;
        _lastWriteTime = info.LastWriteTime;
    }

    public static CustomFileSystemInfo Parse(string entry)
    {
        var split = entry!.Split(new char[] { ':', ',' }, StringSplitOptions.TrimEntries);
        var name = split[0];
        return split.Length switch
        {
            1 => throw new ArgumentException($"Could not parse the entry: '{entry}'"),
            2 => new CustomDirectoryInfo(name, DateTimeExtensions.Parse(split[1])),
            _ => new CustomFileInfo(name, DateTimeExtensions.Parse(split[1]), long.Parse(split[2]))
        };
    }
}