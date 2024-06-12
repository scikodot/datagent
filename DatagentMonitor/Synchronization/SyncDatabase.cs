﻿using DatagentMonitor.FileSystem;
using DatagentShared;
using Microsoft.Data.Sqlite;

namespace DatagentMonitor.Synchronization;

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
                        _lastSyncTime = DateTimeExtensions.Parse(reader.GetString(0));
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

            using var command = new SqliteCommand("INSERT INTO history VALUES (:time)");
            command.Parameters.AddWithValue(":time", (_lastSyncTime = value).Value.Serialize());
            ExecuteNonQuery(command);
        }
    }

    public SyncDatabase(string path) : base(Path.Combine(path, _name))
    {
        using var eventsCommand = new SqliteCommand(
            "CREATE TABLE IF NOT EXISTS events " +
            "(time TEXT, path TEXT, type TEXT, chng TEXT, prop TEXT)");
        ExecuteNonQuery(eventsCommand);

        using var historyCommand = new SqliteCommand(
            "CREATE TABLE IF NOT EXISTS history " +
            "(time TEXT)");
        ExecuteNonQuery(historyCommand);
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
        ExecuteNonQuery(command);  // TODO: use async
    }

    public IEnumerable<EntryChange> EnumerateEvents()
    {
        using var command = new SqliteCommand("SELECT * FROM events");
        return ExecuteForEach(command, reader =>
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
        });
    }

    public void ClearEvents()
    {
        using var command = new SqliteCommand("DELETE FROM events");
        ExecuteNonQuery(command);
    }
}