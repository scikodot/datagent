using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Platform.Storage;
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

namespace Datagent.ViewModels;

// TODO: implement contents caching
public class Table
{
    public class Row
    {
        public int? ID { get; init; }
        public List<string?> Values { get; init; } = new();

        public string? this[int index]
        {
            get => Values[index];
            set => Values[index] = value;
        }
    }

    private readonly string _connectionString;

    public string Name { get; set; }
    public List<string> Columns { get; set; }
    public ObservableCollection<Row> Rows { get; set; } = new();

    public Table(string connectionString, string name, List<string>? columns = null)
    {
        _connectionString = connectionString;
        Name = name;
        Columns = columns ?? new();
    }

    public void LoadContents()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var command = connection.CreateCommand();
        command.CommandText = @$"SELECT rowid AS ID, * FROM {Name}";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetInt32(0);
            var values = new List<string?>();
            for (int i = 0; i < Columns.Count; i++)
                values.Add(reader.GetString(i + 1));

            Rows.Add(new Row { ID = id, Values = values });
        }
    }

    public void ClearContents() => Rows.Clear();
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
    private readonly string _databaseFolder = "Datagent Resources";
    private readonly string _databaseName = "storages.db";
    private readonly string _connectionString;

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
        string path;
        var baseDir = storageProvider.TryGetWellKnownFolderAsync(WellKnownFolder.Documents).Result?.Path.LocalPath;
        if (baseDir != null)
        {
            var folder = Path.Combine(baseDir ?? "", _databaseFolder);
            Directory.CreateDirectory(folder);
            path = Path.Combine(folder, _databaseName);
        }
        else
        {
            path = _databaseName;
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        LoadStorages();
    }

    private void LoadStorages()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var command = connection.CreateCommand();
        command.CommandText = @"SELECT name FROM sqlite_master WHERE type='table'";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var name = reader.GetString(0);
            var columns = LoadColumns(connection, name);
            _tables.Add(new Table(_connectionString, name, columns));
        }
    }

    private List<string> LoadColumns(SqliteConnection connection, string storageName)
    {
        var command = connection.CreateCommand();
        command.CommandText = @"SELECT name FROM pragma_table_info(:storage)";
        command.Parameters.Add(new SqliteParameter(":storage", storageName));
        
        var columns = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            columns.Add(reader.GetString(0));

        return columns;
    }

    public void CreateStorage(string name)
    {
        var columns = new List<string>() { "Name" };
        if (name.Contains("foo"))
            columns.Add("Contents");
        if (name.Contains("bar"))
            columns.Add("Extra");

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var command = connection.CreateCommand();
        command.CommandText = @$"CREATE TABLE {name} ({string.Join(", ", columns.Select(x => x + " TEXT"))})";
        command.ExecuteNonQuery();

        _tables.Add(new Table(_connectionString, name, columns));
    }

    public void ClearTableContents(Table? table)
    {
        table?.ClearContents();
    }

    public void LoadTableContents(Table table)
    {
        table.LoadContents();
        CurrentTable = table;
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
