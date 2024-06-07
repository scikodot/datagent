namespace DatagentMonitor.FileSystem;

public class CustomFileInfo : CustomFileSystemInfo
{
    public override EntryType Type => EntryType.File;

    private long _length;
    public long Length
    {
        get => _length;
        set => _length = value;
    }

    public CustomFileInfo(string name, DateTime lastWriteTime, long length) : base(name, lastWriteTime)
    {
        _length = length;
    }

    public CustomFileInfo(FileInfo info) : base(info)
    {
        _length = info.Length;
    }

    public override string ToString() => $"{Name}: {LastWriteTime.Serialize()}, {Length}";
}
