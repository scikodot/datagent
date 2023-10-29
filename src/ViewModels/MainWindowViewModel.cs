using Avalonia.Data;
using Avalonia.Data.Converters;
using System;
using System.Collections.Generic;
using System.Globalization;

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
    public List<Dictionary<string, string>> StorageItems => new()
    {
        new Dictionary<string, string>
        {
            ["Name"] = "Entry #1",
            ["Property"] = "Nice",
        },
        new Dictionary<string, string>
        {
            ["Name"] = "Entry #2",
            ["Property"] = "Cool",
        }
    };
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
