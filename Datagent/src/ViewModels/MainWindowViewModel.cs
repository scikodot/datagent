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
using Datagent.Data;

namespace Datagent.ViewModels;

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
    private Database _database;

    private readonly ObservableCollection<Table> _tables = new();
    public ObservableCollection<Table> Tables => _tables;

    private ItemsSourceView<Table.Column> _tableColumns = ItemsSourceView<Table.Column>.Empty;
    public ItemsSourceView<Table.Column> TableColumns
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
        var root = (storageProvider.TryGetWellKnownFolderAsync(WellKnownFolder.Documents).Result?.Path.LocalPath) ??
            throw new IOException("Documents folder not found.");

        ServiceFilesManager.Initialize(root);

        _database = new Database(ServiceFilesManager.MainDatabasePath);
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
                _tables.Add(new Table(name, _database));
            }
        };

        _database.ExecuteReader(command, action);
    }

    public void CreateTable(string name)
    {
        var column = new Table.Column("");

        var command = new SqliteCommand
        {
            CommandText = $"CREATE TABLE \"{name}\" ({column.Definition})"
        };

        _database.ExecuteNonQuery(command);

        _tables.Add(new Table(name, _database));
    }

    public void DeleteTable()
    {
        var command = new SqliteCommand
        {
            CommandText = $"DROP TABLE \"{_currentTable.Name}\""
        };

        _database.ExecuteNonQuery(command);

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
