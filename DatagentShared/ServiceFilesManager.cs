namespace DatagentShared
{
    public static class ServiceFilesManager
    {
        private static string? _root;
        public static string Root
        {
            get => _root ?? throw new ArgumentException("Service manager has not been initialized yet.");
            private set
            {
                if (_root != null)
                    throw new ArgumentException("Service manager has already been initialized.");

                if (!Directory.Exists(value))
                    throw new ArgumentException("The specified root directory does not exist.");

                _root = value;
            }
        }

        public static readonly string Folder = ".datagent";
        public static readonly string MainDatabase = "storages.db";
        public static readonly string MonitorDatabase = "events.db";
        public static readonly string Index = "index.txt";
        public static readonly string BackupIndex = "index_backup.txt";

        public static void Initialize(string root)
        {
            Root = root;
            var info = Directory.CreateDirectory(Path.Combine(Root, Folder));
            info.Attributes |= FileAttributes.Hidden;
        }

        public static string FolderPath => Path.Combine(Root, Folder);
        public static string MainDatabasePath => Path.Combine(Root, Folder, MainDatabase);
        public static string MonitorDatabasePath => Path.Combine(Root, Folder, MonitorDatabase);
        public static string IndexPath => Path.Combine(Root, Folder, Index);
        public static string BackupIndexPath => Path.Combine(Root, Folder, BackupIndexPath);

        public static string GetRootSubpath(string fullPath) => fullPath[_root!.Length..];

        public static bool IsServiceLocation(string path) => path.StartsWith(Folder);
    }
}