using System.Text;

namespace DatagentShared;

public class SourceManager
{
    protected static readonly string _folderName = ".datagent";
    public static string FolderName => _folderName;

    protected static readonly string _mainDatabaseName = "storages.db";
    public static string MainDatabaseName => _mainDatabaseName;

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
        var info = Directory.CreateDirectory(FolderPath);
        info.Attributes |= FileAttributes.Hidden;
    }

    public string GetSubpath(string path) => Path.GetRelativePath(_root, path);

    public bool IsServiceLocation(string path) => path.AsSpan(_root.Length + 1).StartsWith(_folderName);
}
