using Avalonia.Data;
using Avalonia.Data.Converters;
using DynamicData;
using ReactiveUI;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace Datagent.ViewModels;

public class Storage
{
    public string Name { get; set; }
    public string Color { get; set; }
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
    public static List<Storage> Storages => new()
    {
        new Storage { Name = "Storage #1", Color = "Red" },
        new Storage { Name = "Storage #2", Color = "Green" },
        new Storage { Name = "Storage #3", Color = "Blue" },
    };

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

    private readonly List<string> _storageColumns = new();
    public List<string> StorageColumns => _storageColumns;

    // TODO: determine the use of DataGridCollectionView
    private readonly ObservableCollection<Entry> _storageItems = new();
    public ObservableCollection<Entry> StorageItems => _storageItems;

    public void LoadStorageColumns(Storage storage)
    {
        _storageColumns.Clear();
        _storageColumns.AddRange(_storageMap[storage.Name][0].Skip(1));
    }

    public void ClearStorageContents() => _storageItems.Clear();

    public void LoadStorageContents(Storage storage)
    {
        // Emulate SQL query, i. e. get a copy of DB data on each call
        var data = _storageMap[storage.Name];
        var rows = new List<List<string>>(data.Skip(1).Select(x => new List<string>(x)));
        
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            _storageItems.Add(new Entry { ID = int.Parse(row[0]), Values = new(row.Skip(1)) });
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
