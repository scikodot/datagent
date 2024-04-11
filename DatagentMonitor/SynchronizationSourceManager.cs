using DatagentMonitor.FileSystem;
using DatagentShared;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DatagentMonitor;

internal class SynchronizationSourceManager : SourceManager
{
    private static readonly string _eventsDatabaseName = "events.db";
    public static string EventsDatabaseName => _eventsDatabaseName;

    private static readonly string _indexName = "index.txt";
    public static string IndexName => _indexName;

    private Database? _eventsDatabase;
    public Database EventsDatabase
    {
        get
        {
            if (_eventsDatabase == null)
            {
                _eventsDatabase = new Database(EventsDatabasePath);
                _eventsDatabase.ExecuteNonQuery(
                    new SqliteCommand("CREATE TABLE IF NOT EXISTS events (time TEXT, path TEXT, type TEXT, prop TEXT)"));
            }

            return _eventsDatabase;
        }
    }

    public string EventsDatabasePath => Path.Combine(_root, _folderName, _eventsDatabaseName);
    public string IndexPath => Path.Combine(_root, _folderName, _indexName);

    public SynchronizationSourceManager(string root) : base(root)
    {
        // Ensure the index is initialized
        if (!File.Exists(IndexPath))
            SerializeIndex(new CustomDirectoryInfo(_root));
    }

    public void SerializeIndex(CustomDirectoryInfo info)
    {
        using var writer = new StreamWriter(IndexPath, append: false, encoding: Encoding.UTF8);
        writer.Write(CustomDirectoryInfoSerializer.Serialize(info));
    }

    public CustomDirectoryInfo DeserializeIndex()
    {
        using var reader = new StreamReader(IndexPath, encoding: Encoding.UTF8);
        return CustomDirectoryInfoSerializer.Deserialize(reader);
    }
}
