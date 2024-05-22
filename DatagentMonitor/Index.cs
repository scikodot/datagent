using DatagentMonitor.FileSystem;
using System.Text;

namespace DatagentMonitor;

internal class Index
{
    private static readonly string _name = "index.txt";

    private readonly string _path;
    public string Path => _path;

    private CustomDirectoryInfo _root;
    public CustomDirectoryInfo Root => _root;

    public Index(string root, string path, Func<DirectoryInfo, bool>? directoryFilter = null)
    {
        _path = System.IO.Path.Combine(root, path, _name);
        _root = new CustomDirectoryInfo(root, directoryFilter);
        if (!File.Exists(_path))
            Serialize();
    }

    public void Serialize()
    {
        using var writer = new StreamWriter(_path, append: false, encoding: Encoding.UTF8);
        writer.Write(CustomDirectoryInfoSerializer.Serialize(_root));
    }

    public CustomDirectoryInfo Deserialize()
    {
        using var reader = new StreamReader(_path, encoding: Encoding.UTF8);
        return _root = CustomDirectoryInfoSerializer.Deserialize(reader);
    }
}
