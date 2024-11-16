namespace DatagentShared;

public class SourceManager
{
    protected static readonly string _folderName = ".datagent";
    public static string FolderName => _folderName;

    // TODO: change to "user.db" or "data.db"
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
        var directory = new DirectoryInfo(root);
        if (!directory.Exists)
            throw new DirectoryNotFoundException(root);

        _root = directory.FullName;

        // Operations performed on the service folder should not affect the root, 
        // so its LastWriteTime must be restored
        var lwt = directory.LastWriteTime;
        var info = Directory.CreateDirectory(FolderPath);
        directory.LastWriteTime = lwt;
        info.Attributes |= FileAttributes.Hidden;
    }

    public string GetSubpath(string path) => Path.GetRelativePath(_root, path);
}
