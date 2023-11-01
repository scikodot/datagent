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

public class Storage
{
    public string Name { get; set; }
    public List<string> Columns {  get; set; }
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

public class Entry/* : IEnumerable<string>*/
{
    public int? ID { get; init; }
    public List<string?> Values { get; init; } = new();

    public string? this[int index]
    {
        get => Values[index];
        set => Values[index] = value;
    }

    //public int Count => _data.Count;

    //public void Add(string value) => _data.Add(value);

    //public void AddRange(IEnumerable<string> values) => _data.AddRange(values);

    //public void Clear() => _data.Clear();

    //public IEnumerator<string> GetEnumerator() => _data.GetEnumerator();

    //IEnumerator IEnumerable.GetEnumerator() => _data.GetEnumerator();
}

public class MainWindowViewModel : ViewModelBase
{
    private static readonly Dictionary<string, List<List<string>>> _storageMap = new()
    {
        ["Storage #1"] = new()
        {
            new() { "ID", "Name", "Contents" },
            new() { "1", "S1E1", "Bad" },
            new() { "2", "S1E2", "Foul" },
            new() { "3", "S1E3", "Awful" },
        },
        ["Storage #2"] = new()
        {
            new() { "ID", "Name", "Character" },
            new() { "1", "S2E1", "Normal" },
            new() { "2", "S2E2", "Regular" },
            new() { "3", "S2E3", "Mediocre" },
        },
        ["Storage #3"] = new()
        {
            new() { "ID", "Name", "Contents", "Extra" },
            new() { "1", "S3E1", "Good", "v.1" },
            new() { "2", "S3E2", "Perfect", "v.2" },
            new() { "3", "S3E3", "Brilliant", "v.3" },
        },
    };

    private readonly string _databaseFolder = "Datagent Resources";
    private readonly string _databaseName = "storages.db";
    private readonly string _connectionString;

    private readonly ObservableCollection<Storage> _storages = new();
    public ObservableCollection<Storage> Storages => _storages;

    // TODO: determine the use of DataGridCollectionView
    private readonly ObservableCollection<Entry> _storageItems = new();
    public ObservableCollection<Entry> StorageItems => _storageItems;

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
            _storages.Add(new Storage { Name = name, Columns = columns });
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

        _storages.Add(new Storage { Name = name, Columns = columns });
    }

    public void ClearStorageContents() => _storageItems.Clear();

    public void LoadStorageContents(Storage storage)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var command = connection.CreateCommand();
        command.CommandText = @$"SELECT rowid AS ID, * FROM {storage.Name}";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetInt32(0);
            var values = new List<string?>();
            for (int i = 0; i < storage.Columns.Count; i++)
                values.Add(reader.GetString(i + 1));

            _storageItems.Add(new Entry { ID = id, Values = values });
        }
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
