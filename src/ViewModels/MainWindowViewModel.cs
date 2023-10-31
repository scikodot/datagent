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

public class Entry : IEnumerable<string>
{
    private readonly List<string> _data;

    public Entry() => _data = new();

    public Entry(IEnumerable<string> data) => _data = new(data);

    public string this[int index]
    {
        get => _data[index];
        set => _data[index] = value;
    }

    public int Count => _data.Count;

    public void Add(string value) => _data.Add(value);

    public void AddRange(IEnumerable<string> values) => _data.AddRange(values);

    public void Clear() => _data.Clear();

    public IEnumerator<string> GetEnumerator() => _data.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _data.GetEnumerator();
}

public class MainWindowViewModel : ViewModelBase
{
    public List<Storage> Storages => new()
    {
        new Storage { Name = "Storage #1", Color = "Red" },
        new Storage { Name = "Storage #2", Color = "Green" },
        new Storage { Name = "Storage #3", Color = "Blue" },
    };

    private static readonly Dictionary<string, List<Entry>> _storageMap = new()
    {
        ["Storage #1"] = new()
        {
            new() { "Name", "Contents" },
            new() { "S1E1", "Bad" },
            new() { "S1E2", "Foul" },
            new() { "S1E3", "Awful" },
        },
        ["Storage #2"] = new()
        {
            new() { "Name", "Character" },
            new() { "S2E1", "Normal" },
            new() { "S2E2", "Regular" },
            new() { "S2E3", "Mediocre" },
        },
        ["Storage #3"] = new()
        {
            new() { "Name", "Contents", "Extra" },
            new() { "S3E1", "Good", "v.1" },
            new() { "S3E2", "Perfect", "v.2" },
            new() { "S3E3", "Brilliant", "v.3" },
        },
    };

    // Use a backing collection to keep refs to empty entries;
    // this avoids re-instantiations and excessive GC passes
    private readonly List<Entry> _storageItemsContainers = new();
    private int _storageItemsCount = 0;
    // TODO: determine the use of DataGridCollectionView
    private readonly ObservableCollection<Entry> _storageItems = new();
    public ObservableCollection<Entry> StorageItems => _storageItems;

    public Entry GetColumnsNames(Storage storage) => _storageMap[storage.Name][0];

    public void LoadStorageContents(Storage storage)
    {
        // Emulate SQL query, i. e. get a copy of DB data on each call
        var rows = new List<Entry>();
        foreach (var row in _storageMap[storage.Name])
            rows.Add(new Entry(row));

        // Create new entries if not enough
        int newContainers = rows.Count - _storageItemsContainers.Count;
        for (int i = 0; i < newContainers; i++)
            _storageItemsContainers.Add(new Entry());

        // Populate the entries with data
        for (int i = 0; i < rows.Count; i++)
        {
            _storageItemsContainers[i].Clear();
            _storageItemsContainers[i].AddRange(rows[i]);
        }

        _storageItemsCount = rows.Count;
    }

    public void RefreshContents()
    {
        _storageItems.Clear();
        _storageItems.AddRange(_storageItemsContainers.Skip(1).Take(_storageItemsCount));
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
