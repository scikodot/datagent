namespace DatagentMonitor.FileSystem;

public class CustomFileInfo : CustomFileSystemInfo
{
    public override EntryType Type => EntryType.File;

    private long _length;
    public long Length
    {
        get => _length;
        set
        {
            if (value < 0)
                throw new ArgumentException($"{nameof(Length)} cannot be less than zero.");

            _length = value;
        }
    }

    public CustomFileInfo(string name, DateTime lastWriteTime, long length) : base(name, lastWriteTime)
    {
        Length = length;
    }

    public CustomFileInfo(FileInfo info) : base(info)
    {
        Length = info.Length;
    }

    public override string ToString() => $"{Name}: {LastWriteTime.Serialize()}, {Length}";
}
