namespace DatagentMonitorTests;

public class DirectoryFixture : IDisposable
{
    private readonly Dictionary<string, DirectoryInfo> _directories = new();

    public DirectoryFixture() { }

    public DirectoryInfo CreateTempDirectory(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), name);
        if (!_directories.TryGetValue(path, out var directory))
            _directories.Add(path, directory = new DirectoryInfo(path));

        directory.Refresh();
        if (directory.Exists)
            directory.Delete(recursive: true);
        directory.Create();
        directory.Refresh();
        return directory;
    }

    public void Dispose()
    {
        foreach (var directory in _directories.Values)
            directory.Delete(recursive: true);

        GC.SuppressFinalize(this);
    }
}
