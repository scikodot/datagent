using Avalonia.Collections;
using Datagent.Extensions;
using Datagent.ViewModels;
using DatagentShared;
using DynamicData;
using Microsoft.Data.Sqlite;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Datagent.Data
{
    public enum ColumnType
    {
        Integer,
        Real,
        Text
    }

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

        private Database _database;

        // TODO: add ToSqlite conversion for queries; see Column.ToSqlite()
        public string Name { get; set; }
        public string Identifier => $"\"{Name}\"";

        private readonly ObservableCollection<Column> _columns = new();
        public ObservableCollection<Column> Columns => _columns;

        private readonly ObservableCollection<Row> _rows = new();
        public DataGridCollectionView Rows { get; }

        public Table(string name, Database database, List<Column>? columns = null)
        {
            _database = database;

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

            _database.ExecuteReader(command, action);
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

            _database.ExecuteReader(command, action);
        }

        public void ClearContents() => _rows.Clear();

        public void AddColumn(string name)
        {
            var column = new Column(name);

            var command = new SqliteCommand
            {
                CommandText = @$"ALTER TABLE {Identifier} ADD COLUMN {column.Definition}"
            };

            _database.ExecuteNonQuery(command);

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

            _database.ExecuteNonQuery(command);

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

            _database.ExecuteNonQuery(command);

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

            _database.ExecuteReader(command, action);
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

            _database.ExecuteNonQuery(command);
        }

        private void DeleteRow(Row row)
        {
            var command = new SqliteCommand
            {
                CommandText = $@"DELETE FROM {Identifier} WHERE rowid = :id"
            };
            command.Parameters.AddWithValue("id", row.ID);

            _database.ExecuteNonQuery(command);
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
}
