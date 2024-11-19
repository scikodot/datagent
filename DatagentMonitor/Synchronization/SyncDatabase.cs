using DatagentMonitor.FileSystem;
using DatagentShared;
using Microsoft.Data.Sqlite;

namespace DatagentMonitor.Synchronization;

internal class SyncDatabase : Database
{
    private static readonly string _name = "sync.db";
    public static string Name => _name;

    private readonly string _root;
    public string Root => _root;

    public string Path => GetPath(_root);

    private DateTime? _lastSyncTime;
    public DateTime? LastSyncTime => _lastSyncTime;

    public SyncDatabase(string root) : base(GetPath(root))
    {
        _root = root;
    }

    private static string GetPath(string root) => System.IO.Path.Combine(root, SourceManager.FolderName, _name);

    protected override async Task InitAsync(SqliteConnection connection)
    {
        await base.InitAsync(connection);

        using var events = new SqliteCommand(
            "CREATE TABLE IF NOT EXISTS events " +
            "(time TEXT, path TEXT, type TEXT, chng TEXT, prop TEXT)", connection);
        await events.ExecuteNonQueryAsync();

        using var history = new SqliteCommand(
            "CREATE TABLE IF NOT EXISTS history " +
            "(time TEXT)", connection);
        await history.ExecuteNonQueryAsync();

        // Retrieve LastSyncTime if it exists
        await GetLastSyncTimeAsync(connection);
    }

    private async Task GetLastSyncTimeAsync(SqliteConnection connection)
    {
        using var lst = new SqliteCommand(
            "SELECT * FROM history " +
            "ORDER BY time DESC LIMIT 1", connection);
        using var reader = await lst.ExecuteReaderAsync();
        if (reader.Read())
            _lastSyncTime = DateTimeExtensions.Parse(reader.GetString(0));
    }

    public async Task SetLastSyncTimeAsync(DateTime value)
    {
        using var command = new SqliteCommand("INSERT INTO history VALUES (:time)");
        command.Parameters.AddWithValue(":time", (_lastSyncTime = value).Value.Serialize());
        await ExecuteNonQueryAsync(command);
    }

    public async Task AddEvent(EntryChange change)
    {
        var properties = change.Action switch
        {
            EntryAction.Rename => ActionSerializer.Serialize(change.RenameProperties),
            EntryAction.Create or
            EntryAction.Change => ActionSerializer.Serialize(change.ChangeProperties),
            _ => null
        };
        using var command = new SqliteCommand("INSERT INTO events VALUES (:time, :path, :type, :chng, :prop)");
        command.Parameters.AddWithValue(":time", change.Timestamp!.Value.Serialize());
        command.Parameters.AddWithValue(":path", change.OldPath);
        command.Parameters.AddWithValue(":type", Enum.GetName(change.Type));
        command.Parameters.AddWithValue(":chng", Enum.GetName(change.Action));
        command.Parameters.AddWithValue(":prop", properties is not null ? properties : DBNull.Value);
        await ExecuteNonQueryAsync(command);
    }

    public async IAsyncEnumerable<EntryChange> EnumerateEventsAsync()
    {
        using var command = new SqliteCommand("SELECT * FROM events");

        static EntryChange GetChange(SqliteDataReader reader)
        {
            var path = reader.GetString(1);
            var type = Enum.Parse<EntryType>(reader.GetString(2));
            var action = Enum.Parse<EntryAction>(reader.GetString(3));
            RenameProperties? renameProperties = null;
            ChangeProperties? changeProperties = null;
            if (!reader.IsDBNull(4))
            {
                var json = reader.GetString(4);
                switch (action)
                {
                    case EntryAction.Rename:
                        renameProperties = ActionSerializer.Deserialize<RenameProperties>(json);
                        break;

                    case EntryAction.Create:
                    case EntryAction.Change:
                        changeProperties = ActionSerializer.Deserialize<ChangeProperties>(json);
                        break;
                }
            }

            return new EntryChange(
                DateTimeExtensions.Parse(reader.GetString(0)), path,
                type, action, renameProperties, changeProperties);
        }

        await foreach(var change in ExecuteForEachAsync(command, GetChange))
            yield return change;
    }

    public async Task ClearEventsAsync()
    {
        using var command = new SqliteCommand("DELETE FROM events");
        await ExecuteNonQueryAsync(command);
    }
}
