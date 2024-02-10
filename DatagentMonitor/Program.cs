using Microsoft.Data.Sqlite;
using System.IO;

namespace DatagentMonitor
{
    public enum Actions
    {
        Created,
        Renamed,
        Changed,
        RenamedChanged,
        Deleted,
    }

    public class Program
    {
        private static string _dbPath;
        private static string _tableName = "deltas";
        private static List<string> _tableColumns = new() { "path", "type", "action" };
        private static SqliteConnection _connection;

        static void Main(string[] args)
        {
            foreach (var arg in args)
            {
                if (!arg.StartsWith("--"))
                    throw new ArgumentException("Invalid argument. Any argument should start with '--'.");

                if (arg.Length < 3)
                    throw new ArgumentException("No argument name given.");

                var split = arg[2..].Split('=');
                if (split.Length < 2 || split[0] == "" || split[1] == "")
                    throw new ArgumentException("No argument name and/or value given.");

                (var argName, var argValue) = (split[0], split[1]);
                switch (argName)
                {
                    case "db-path":
                        _dbPath = argValue;
                        break;
                    //...
                    default:
                        throw new ArgumentException($"Unexpected argument: {argName}");
                }
            }

            // Connect to the specified database
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = _dbPath,
                Mode = SqliteOpenMode.ReadWriteCreate
            }.ToString();
            _connection = new SqliteConnection(connectionString);
            _connection.Open();

            // Setup watcher
            // TODO: use aux file, say .monitor-filter, to watch only over a subset of files/directories
            var watcher = new FileSystemWatcher(Path.GetDirectoryName(_dbPath));

            watcher.NotifyFilter = NotifyFilters.Attributes
                                 | NotifyFilters.CreationTime
                                 | NotifyFilters.DirectoryName
                                 | NotifyFilters.FileName
                                 | NotifyFilters.LastAccess
                                 | NotifyFilters.LastWrite
                                 | NotifyFilters.Security
                                 | NotifyFilters.Size;

            watcher.Changed += OnChanged;
            watcher.Created += OnCreated;
            watcher.Deleted += OnDeleted;
            watcher.Renamed += OnRenamed;
            watcher.Error += OnError;

            watcher.Filter = "*";  // track all files, even with no extension
            watcher.IncludeSubdirectories = true;
            watcher.EnableRaisingEvents = true;

            while (true)
            {
                // Events polling, logging, etc.
            }

            _connection.Dispose();
        }

        private static void OnChanged(object sender, FileSystemEventArgs e)
        {
            if (e.ChangeType != WatcherChangeTypes.Changed)
                return;

            // Track changes to files only; directory changes are not essential
            var attrs = File.GetAttributes(e.FullPath);
            if (attrs.HasFlag(FileAttributes.Directory))
                return;

            var command = new SqliteCommand("SELECT action WHERE path=:path");
            command.Parameters.AddWithValue(":path", e.FullPath);
            command.Connection = _connection;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var action = reader.GetString(0);

            }


            Console.WriteLine($"Changed: {e.FullPath}");
        }

        private static void OnCreated(object sender, FileSystemEventArgs e)
        {
            string value = $"Created: {e.FullPath}";
            Console.WriteLine(value);
        }

        private static void OnDeleted(object sender, FileSystemEventArgs e) =>
            Console.WriteLine($"Deleted: {e.FullPath}");

        private static void OnRenamed(object sender, RenamedEventArgs e)
        {
            Console.WriteLine($"Renamed:");
            Console.WriteLine($"    Old: {e.OldFullPath}");
            Console.WriteLine($"    New: {e.FullPath}");
        }

        private static void OnError(object sender, ErrorEventArgs e) =>
            PrintException(e.GetException());

        private static void PrintException(Exception? ex)
        {
            if (ex != null)
            {
                Console.WriteLine($"Message: {ex.Message}");
                Console.WriteLine("Stacktrace:");
                Console.WriteLine(ex.StackTrace);
                Console.WriteLine();
                PrintException(ex.InnerException);
            }
        }
    }
}