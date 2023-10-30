using Avalonia.Data;
using Avalonia.Data.Converters;
using DynamicData;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Dynamic;
using System.Globalization;
using System.Linq;

namespace Datagent.ViewModels;

public class Storage
{
    public string Name { get; set; }
    public string Color { get; set; }
}

public class MainWindowViewModel : ViewModelBase
{
    public List<Storage> Storages => new()
    {
        new Storage { Name = "Storage #1", Color = "Red" },
        new Storage { Name = "Storage #2", Color = "Green" },
        new Storage { Name = "Storage #3", Color = "Blue" },
    };

    private static readonly Dictionary<string, List<List<string>>> _storageMap = new()
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

    // TODO: determine the use of DataGridCollectionView
    private readonly ObservableCollection<ExpandoObject> _storageItems = new();
    public ObservableCollection<ExpandoObject> StorageItems => _storageItems;

    public List<string> GetColumnsNames(Storage storage)
    {
        var rows = _storageMap[storage.Name];
        return rows[0];
    }

    public void RefreshContents(Storage storage)
    {
        _storageItems.Clear();
        var rows = _storageMap[storage.Name];
        for (int i = 1; i < rows.Count; i++)
            _storageItems.Add(GetObject(rows[0], rows[i]));
    }

    public ExpandoObject GetObject(List<string> keys, List<string> values)
    {
        dynamic obj = new ExpandoObject();
        var dict = obj as IDictionary<string, object>;
        foreach (var (key, value) in keys.Zip(values))
            dict[key] = value;

        return obj;
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
