using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Platform.Storage;
using Datagent.Extensions;
using DatagentShared;
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
using static Datagent.ViewModels.Database.Table;

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

            public override string ToString() => Name;
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

        private readonly ObservableCollection<Column> _columns = new();
        public ObservableCollection<Column> Columns => _columns;

        private readonly ObservableCollection<Row> _rows = new();
        public DataGridCollectionView Rows { get; }

        public Table(string name, List<Column>? columns = null)
        {
            Name = name;

            if (columns is not null)
                _columns.AddRange(columns);
            else
                LoadColumns();

            Rows = new(_rows);
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
                    _columns.Add(new Column(name));
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
                    for (int i = 0; i < _columns.Count; i++)
                        values.Add(reader.IsDBNull(i + 1) ? null : reader.GetString(i + 1));

                    _rows.Add(new Row(id, values));
                }
            };

            ExecuteReader(command, action);
        }

        public void ClearContents() => _rows.Clear();

        public void AddColumn(string name)
        {
            var column = new Column(name);

            var command = new SqliteCommand
            {
                CommandText = @$"ALTER TABLE {Identifier} ADD COLUMN {column.Definition}"
            };

            ExecuteNonQuery(command);

            _columns.Add(column);

            foreach (var row in _rows)
                row.Values.Add("");
        }

        public void UpdateColumn(string name, string nameNew)
        {
            var column = _columns.Single(x => x.Name == name);

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
            for (int i = 0; i < _columns.Count; i++)
            {
                if (_columns[i].Name == name)
                {
                    index = i;
                    break;
                }
            }

            var command = new SqliteCommand
            {
                CommandText = @$"ALTER TABLE {Identifier} DROP COLUMN {_columns[index].Identifier}"
            };

            ExecuteNonQuery(command);

            _columns.RemoveAt(index);
            foreach (var row in _rows)
                row.Values.RemoveAt(index);
        }

        // TODO: multiple inserts are too slow; fix
        private void InsertRow()
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
                for (int i = 0; i < _columns.Count; i++)
                    values.Add("");

                _rows.Add(new Row(id, values));
            };

            ExecuteReader(command, action);
        }

        public void InsertRows(int count)
        {
            for (int i = 0; i < count; i++)
                InsertRow();
        }

        public void UpdateRow(Row row, string column)
        {
            // TODO: consider storing row values as dict;
            // DataGridColumn's are always identified by their headers, not by indexes
            int index = 0;
            for (int i = 0; i < _columns.Count; i++)
            {
                if (_columns[i].Name == column)
                {
                    index = i;
                    break;
                }
            }

            var command = new SqliteCommand
            {
                CommandText = $@"UPDATE {Identifier} SET {_columns[index].Identifier} = :value WHERE rowid = :id"
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
        }

        public void DeleteRows(IEnumerable<Row> rows)
        {
            foreach (var row in rows)
                DeleteRow(row);

            _rows.RemoveMany(rows);
        }

        public void FilterColumn(string name, string filter)
        {
            int index = 0;
            for (int i = 0; i < Columns.Count; i++)
            {
                if (_columns[i].Name == name)
                {
                    index = i;
                    break;
                }
            }

            Rows.Filter = x => ((Row)x)[index].StartsWith(filter);
        }

        public void Search(int searchColumnIndex, string searchText)
        {
            Rows.Filter = row =>
            {
                if (searchColumnIndex >= 0)
                    return ((Row)row)[searchColumnIndex].StartsWith(searchText);

                return true;
            };

            Rows.Refresh();
        }
    }

    public static string ConnectionString { get; private set; }

    // TODO: consider moving to a static constructor
    public Database(IStorageProvider storageProvider)
    {
        var root = (storageProvider.TryGetWellKnownFolderAsync(WellKnownFolder.Documents).Result?.Path.LocalPath) ?? 
            throw new IOException("Documents folder not found.");

        ServiceFilesManager.Initialize(root);

        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = ServiceFilesManager.MainDatabasePath,
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

    private ItemsSourceView<Column> _tableColumns = ItemsSourceView<Column>.Empty;
    public ItemsSourceView<Column> TableColumns
    {
        get => _tableColumns;
        set => this.RaiseAndSetIfChanged(ref _tableColumns, value);
    }

    private DataGridCollectionView? _tableRows = null;
    public DataGridCollectionView? TableRows
    {
        get => _tableRows;
        set => this.RaiseAndSetIfChanged(ref _tableRows, value);
    }

    private Table? _currentTable;
    public Table? CurrentTable
    {
        get => _currentTable;
        set
        {
            TableColumns = ItemsSourceView.GetOrCreate(value?.Columns);
            TableRows = value?.Rows;

            this.RaiseAndSetIfChanged(ref _currentTable, value);
        }
    }

    private int _searchColumnIndex = -1;
    public int SearchColumnIndex
    {
        get => _searchColumnIndex;
        set => this.RaiseAndSetIfChanged(ref _searchColumnIndex, value);
    }

    private string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set => this.RaiseAndSetIfChanged(ref _searchText, value);
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

    public void DeleteTable()
    {
        var command = new SqliteCommand
        {
            CommandText = $"DROP TABLE \"{_currentTable.Name}\""
        };

        ExecuteNonQuery(command);

        _tables.Remove(_currentTable);
    }

    public void ClearTableContents(Table? table)
    {
        table?.ClearContents();
        _searchColumnIndex = -1;
    }

    public void LoadTableContents()
    {
        _currentTable?.LoadContents();
        if (_currentTable is not null)
            _searchColumnIndex = 0;
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

    public void AddRows(object count)
    {
        _currentTable?.InsertRows(decimal.ToInt32((decimal)count));
    }

    public void UpdateRow(object row, object column)
    {
        _currentTable?.UpdateRow((Table.Row)row, (string)column);
    }

    public void DeleteRows(object rows)
    {
        _currentTable?.DeleteRows(((IList)rows).Cast<Table.Row>());
    }

    public void FilterColumn(object name, object filter)
    {
        _currentTable?.FilterColumn((string)name, (string)filter);
    }

    public void Search()
    {
        _currentTable?.Search(_searchColumnIndex, _searchText);
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
