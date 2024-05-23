using DatagentMonitor.FileSystem;
using DatagentShared;
using Microsoft.Data.Sqlite;

namespace DatagentMonitor;

internal class SyncDatabase : Database
{
    private static readonly string _name = "sync.db";

    private DateTime? _lastSyncTime = null;
    public DateTime? LastSyncTime
    {
        get
        {
            try
            {
                using var command = new SqliteCommand("SELECT * FROM history ORDER BY time DESC LIMIT 1");
                ExecuteReader(command, reader =>
                {
                    if (reader.Read())
                        _lastSyncTime = DateTime.ParseExact(reader.GetString(1), CustomFileInfo.DateTimeFormat, null);
                });
            }
            catch (SqliteException ex)
            {
                // TODO: handle
            }
            return _lastSyncTime;
        }
        set
        {
            if (value is null)
                throw new ArgumentNullException(nameof(value));

            using var command = new SqliteCommand("INSERT INTO history VALUES :time");
            command.Parameters.AddWithValue(":time", (_lastSyncTime = value).Value.ToString(CustomFileInfo.DateTimeFormat));
            ExecuteNonQuery(command);
        }
    }

    public SyncDatabase(string path) : base(Path.Combine(path, _name))
    {
        using var eventsCommand = new SqliteCommand(
            "CREATE TABLE IF NOT EXISTS events " +
            "(time TEXT, path TEXT, type TEXT, prop TEXT)");
        ExecuteNonQuery(eventsCommand);

        using var historyCommand = new SqliteCommand(
            "CREATE TABLE IF NOT EXISTS history " +
            "(time TEXT)");
        ExecuteNonQuery(historyCommand);
    }

    public async Task AddEvent(FileSystemEntryChange change)
    {
        if (change.Action is FileSystemEntryAction.Change && CustomFileSystemInfo.GetEntryType(change.Path) is FileSystemEntryType.Directory)
            throw new DirectoryChangeActionNotAllowedException();

        ActionProperties? properties = change.Action switch
        {
            FileSystemEntryAction.Rename => change.Properties.RenameProps,
            FileSystemEntryAction.Create or
            FileSystemEntryAction.Change => change.Properties.ChangeProps,
            _ => null
        };
        using var command = new SqliteCommand("INSERT INTO events VALUES (:time, :path, :type, :prop)");
        command.Parameters.AddWithValue(":time", (change.Timestamp ?? DateTime.Now).ToString(CustomFileInfo.DateTimeFormat));
        command.Parameters.AddWithValue(":path", change.Path);
        command.Parameters.AddWithValue(":type", FileSystemEntryActionExtensions.ActionToString(change.Action));
        command.Parameters.AddWithValue(":prop", properties is not null ? ActionProperties.Serialize(properties) : DBNull.Value);
        ExecuteNonQuery(command);  // TODO: use async
    }

    public IEnumerable<FileSystemEntryChange> GetEvents()
    {
        using var command = new SqliteCommand("SELECT * FROM events");
        return ExecuteForEach(command, reader =>
        {
            var change = new FileSystemEntryChange(
                reader.GetString(1), 
                FileSystemEntryActionExtensions.StringToAction(reader.GetString(2)))
            {
                Timestamp = DateTime.ParseExact(reader.GetString(0), CustomFileInfo.DateTimeFormat, null)
            };

            if (!reader.IsDBNull(3))
            {
                var json = reader.GetString(3);
                switch (change.Action)
                {
                    case FileSystemEntryAction.Rename:
                        change.Properties.RenameProps = ActionProperties.Deserialize<RenameProperties>(json)!;
                        break;

                    case FileSystemEntryAction.Create:
                    case FileSystemEntryAction.Change:
                        change.Properties.ChangeProps = ActionProperties.Deserialize<ChangeProperties>(json)!;
                        break;
                }
            }

            return change;
        });
    }

    public void ClearEvents()
    {
        using var command = new SqliteCommand("DELETE FROM events");
        ExecuteNonQuery(command);
    }
}
