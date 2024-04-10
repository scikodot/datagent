using System.Text;

namespace DatagentShared;

public class SourceManager
{
    protected static readonly string _folderName = ".datagent";
    protected static readonly string _mainDatabaseName = "storages.db";

    protected readonly string _root;
    public string Root => _root;

    protected Database? _mainDatabase;
    public Database MainDatabase => _mainDatabase ??= new Database(MainDatabasePath);

    public string FolderPath => Path.Combine(_root, _folderName);
    public string MainDatabasePath => Path.Combine(_root, _folderName, _mainDatabaseName);

    public SourceManager(string root)
    {
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException(root);

        _root = root;
        var info = Directory.CreateDirectory(Path.Combine(_root, _folderName));
        info.Attributes |= FileAttributes.Hidden;
    }

    public string GetRootSubpath(string fullPath) => fullPath[_root!.Length..];

    public static bool IsServiceLocation(string path) => path.StartsWith(_folderName);
}
