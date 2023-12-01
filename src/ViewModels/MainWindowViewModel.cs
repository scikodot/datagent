using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Platform.Storage;
using Datagent.Extensions;
using DynamicData;
using Microsoft.Data.Sqlite;
using ReactiveUI;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Tmds.DBus.Protocol;
using static Datagent.ViewModels.Database;

namespace Datagent.ViewModels;

public enum ColumnType
{
    Integer,
    Real,
    Text
}

public class Database
{
    // TODO: implement contents caching
    public class Table
    {
        public class Column : ReactiveObject
        {
            private string _name;
            public string Name
            {
                get => _name;
                set => this.RaiseAndSetIfChanged(ref _name, value);
            }
            public string Identifier => $"\"{Name}\"";
            public ColumnType Type { get; } = ColumnType.Text;
            public string Constraints { get; } = "NOT NULL DEFAULT ''";

            // TODO: consider replacing single quotes with double for identifiers;
            // single quotes seem to be an SQLite feature
            public string Definition => $"{Identifier} {Type.ToSqlite()} {Constraints}";

            public Column(string name)
            {
                Name = name;
            }
        }

        public class Row
        {
            public int? ID { get; }
            public List<string?> Values { get; } = new();

            public Row(int? id, List<string?> values)
            {
                ID = id;
                Values = values;
            }

            public string? this[int index]
            {
                get => Values[index];
                set => Values[index] = value;
            }
        }

        // TODO: add ToSqlite conversion for queries; see Column.ToSqlite()
        public string Name { get; set; }
        public string Identifier => $"\"{Name}\"";
        public ObservableCollection<Column> Columns { get; } = new();
        public ObservableCollection<Row> Rows { get; } = new();

        public Table(string name, List<Column>? columns = null)
        {
            Name = name;

            if (columns is not null)
                Columns.AddRange(columns);
            else
                LoadColumns();
        }

        private void LoadColumns()
        {
            var command = new SqliteCommand
            {
                CommandText = @"SELECT name FROM pragma_table_info(:table)"
            };
            command.Parameters.AddWithValue(":table", Name);

            var action = (SqliteDataReader reader) =>
            {
                while (reader.Read())
                {
                    var name = reader.GetString(0);
                    Columns.Add(new Column(name));
                }
            };
            
            ExecuteReader(command, action);
        }

        public void LoadContents()
        {
            var command = new SqliteCommand
            {
                CommandText = @$"SELECT rowid AS ID, * FROM {Identifier}"
            };

            var action = (SqliteDataReader reader) =>
            {
                while (reader.Read())
                {
                    var id = reader.GetInt32(0);
                    var values = new List<string?>();
                    for (int i = 0; i < Columns.Count; i++)
                        values.Add(reader.IsDBNull(i + 1) ? null : reader.GetString(i + 1));

                    Rows.Add(new Row(id, values));
                }
            };

            ExecuteReader(command, action);
        }

        public void ClearContents() => Rows.Clear();

        public void AddColumn(string name)
        {
            var column = new Column(name);

            var command = new SqliteCommand
            {
                CommandText = @$"ALTER TABLE {Identifier} ADD COLUMN {column.Definition}"
            };

            ExecuteNonQuery(command);

            Columns.Add(column);

            foreach (var row in Rows)
                row.Values.Add("");
        }

        public void UpdateColumn(string name, string nameNew)
        {
            var column = Columns.Single(x => x.Name == name);

            var command = new SqliteCommand
            {
                CommandText = $"ALTER TABLE {Identifier} RENAME COLUMN {column.Identifier} TO \"{nameNew}\""
            };
            //command.Parameters.AddWithValue("name", nameNew);

            ExecuteNonQuery(command);

            column.Name = nameNew;
        }

        public void DropColumn(string name)
        {
            int index = 0;
            for (int i = 0; i < Columns.Count; i++)
            {
                if (Columns[i].Name == name)
                {
                    index = i;
                    break;
                }
            }

            var command = new SqliteCommand
            {
                CommandText = @$"ALTER TABLE {Identifier} DROP COLUMN {Columns[index].Identifier}"
            };

            ExecuteNonQuery(command);

            Columns.RemoveAt(index);
            foreach (var row in Rows)
                row.Values.RemoveAt(index);
        }

        public void InsertRow()
        {
            var command = new SqliteCommand
            {
                CommandText = $@"INSERT INTO {Identifier} DEFAULT VALUES RETURNING rowid AS ID"
            };

            var action = (SqliteDataReader reader) =>
            {
                reader.Read();
                var id = reader.GetInt32(0);
                var values = new List<string?>();
                for (int i = 0; i < Columns.Count; i++)
                    values.Add("");

                Rows.Add(new Row(id, values));
            };

            ExecuteReader(command, action);
        }

        public void UpdateRow(Row row, string column)
        {
            // TODO: consider storing row values as dict;
            // DataGridColumn's are always identified by their headers, not by indexes
            int index = 0;
            for (int i = 0; i < Columns.Count; i++)
            {
                if (Columns[i].Name == column)
                {
                    index = i;
                    break;
                }
            }

            var command = new SqliteCommand
            {
                CommandText = $@"UPDATE {Identifier} SET {Columns[index].Identifier} = :value WHERE rowid = :id"
            };
            command.Parameters.AddWithValue("value", row[index]);
            command.Parameters.AddWithValue("id", row.ID);

            ExecuteNonQuery(command);
        }

        private void DeleteRow(Row row)
        {
            var command = new SqliteCommand
            {
                CommandText = $@"DELETE FROM {Identifier} WHERE rowid = :id"
            };
            command.Parameters.AddWithValue("id", row.ID);

            ExecuteNonQuery(command);

            //Rows.Remove(row);
        }

        public void DeleteRows(IEnumerable<Row> rows)
        {
            foreach (var row in rows)
                DeleteRow(row);

            Rows.RemoveMany(rows);
        }
    }

    private const string _folder = "Datagent Resources";
    private const string _name = "storages.db";

    public static string ConnectionString { get; private set; }

    // TODO: consider moving to a static constructor
    public Database(IStorageProvider storageProvider)
    {
        string path;
        var baseDir = storageProvider.TryGetWellKnownFolderAsync(WellKnownFolder.Documents).Result?.Path.LocalPath;
        if (baseDir != null)
        {
            var folder = Path.Combine(baseDir ?? "", _folder);
            Directory.CreateDirectory(folder);
            path = Path.Combine(folder, _name);
        }
        else
        {
            path = _name;
        }

        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
    }

    public static void ExecuteReader(SqliteCommand command, Action<SqliteDataReader> action)
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        command.Connection = connection;
        using var reader = command.ExecuteReader();
        action(reader);
    }

    public static void ExecuteNonQuery(SqliteCommand command)
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        command.Connection = connection;
        command.ExecuteNonQuery();
    }
}

//public class Entry : List<string>
//{
//    public Entry() : base() { }

//    public Entry(IEnumerable<string> elems) : base(elems) { }

//    /* TODO: possible bug
//     * 
//     * After declaring a property with a "new" modifier (here it's the indexer), 
//     * consequently hiding the inherited property,
//     * Avalonia still uses the hidden property for some reason.
//     * 
//     * Practically, after binding some Entry[1] to a TextBox and editing the text in the latter (from UI), 
//     * the debug lines below will NOT show in the output, but the elements of the underlying list
//     * will still be changed and stored as usual.
//     */
//    new public string this[int index]
//    {
//        get
//        {
//            Debug.WriteLine("Now get!");
//            return base[index];
//        }
//        set
//        {
//            Debug.WriteLine("Now set!");
//            base[index] = value;
//        }
//    }
//}

public class MainWindowViewModel : ViewModelBase
{
    private readonly ObservableCollection<Table> _tables = new();
    public ObservableCollection<Table> Tables => _tables;

    private Table? _currentTable;
    public Table? CurrentTable
    {
        get => _currentTable;
        set => this.RaiseAndSetIfChanged(ref _currentTable, value);
    }

    public MainWindowViewModel(IStorageProvider storageProvider)
    {
        _ = new Database(storageProvider);
        LoadTables();
    }

    private void LoadTables()
    {
        var command = new SqliteCommand
        {
            CommandText = @"SELECT name FROM sqlite_master WHERE type='table'"
        };

        var action = (SqliteDataReader reader) =>
        {
            while (reader.Read())
            {
                var name = reader.GetString(0);
                _tables.Add(new Table(name));
            }
        };

        ExecuteReader(command, action);
    }

    public void CreateTable(string name)
    {
        var column = new Table.Column("");

        var command = new SqliteCommand
        {
            CommandText = $"CREATE TABLE \"{name}\" ({column.Definition})"
        };

        ExecuteNonQuery(command);

        _tables.Add(new Table(name));
    }

    public void ClearTableContents(Table? table)
    {
        table?.ClearContents();
    }

    public void LoadTableContents()
    {
        _currentTable?.LoadContents();
    }

    public void AddColumn(object name)
    {
        _currentTable?.AddColumn((string)name);
    }

    public void UpdateColumn(object name, object nameNew)
    {
        _currentTable?.UpdateColumn((string)name, (string)nameNew);
    }

    public void DeleteColumn(object name)
    {
        _currentTable?.DropColumn((string)name);
    }

    public void AddRow()
    {
        _currentTable?.InsertRow();
    }

    public void UpdateRow(object row, object column)
    {
        _currentTable?.UpdateRow((Table.Row)row, (string)column);
    }

    public void DeleteRows(object rows)
    {
        _currentTable?.DeleteRows(((IList)rows).Cast<Table.Row>());
    }
}

public class DictionaryConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Dictionary<string, string> dict && parameter is string key)
            return dict[key];

        return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
