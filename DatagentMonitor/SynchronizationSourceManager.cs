using DatagentMonitor.FileSystem;
using DatagentShared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DatagentMonitor;

internal class SynchronizationSourceManager : SourceManager
{
    private static readonly string _eventsDatabaseName = "events.db";
    private static readonly string _indexName = "index.txt";

    private Database? _eventsDatabase;
    public Database EventsDatabase => _eventsDatabase ??= new Database(EventsDatabasePath);

    public string EventsDatabasePath => Path.Combine(_root, _folderName, _eventsDatabaseName);
    public string IndexPath => Path.Combine(_root, _folderName, _indexName);

    public SynchronizationSourceManager(string root) : base(root)
    {
        // Ensure the index is initialized
        if (!File.Exists(IndexPath))
            SerializeIndex(new CustomDirectoryInfo(_root));
    }

    public static string GetRootedEventsDatabasePath(string root) => Path.Combine(root, _eventsDatabaseName, _indexName);

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
