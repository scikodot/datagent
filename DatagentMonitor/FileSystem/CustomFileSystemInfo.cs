namespace DatagentMonitor.FileSystem;

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
            if (string.IsNullOrEmpty(value))
                throw new ArgumentException($"{nameof(Name)} cannot be null or empty.");

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
        set
        {
            if (value > DateTimeStaticProvider.Now)
                throw new FutureTimestampException(nameof(LastWriteTime));

            _lastWriteTime = value;
        }
    }

    protected CustomFileSystemInfo(string name, DateTime lastWriteTime)
    {
        Name = name;
        LastWriteTime = lastWriteTime;
    }

    protected CustomFileSystemInfo(FileSystemInfo info)
    {
        if (info is null)
            throw new ArgumentNullException(nameof(info));

        if (!info.Exists)
            throw info switch
            {
                DirectoryInfo => new DirectoryNotFoundException(info.FullName),
                FileInfo => new FileNotFoundException(info.FullName)
            };

        Name = info.Name;
        LastWriteTime = info.LastWriteTime;
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
