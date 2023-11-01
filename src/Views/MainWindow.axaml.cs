using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System;
using System.Diagnostics;
using System.Data.Common;
using Datagent.ViewModels;
using DynamicData;
using Avalonia.Data;

namespace Datagent.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        DataContext = new MainWindowViewModel(StorageProvider);
        InitializeComponent();
    }

    public void SelectStorage(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0 || e.AddedItems[0] is not Storage storage)
            return;

        if (DataContext is not MainWindowViewModel viewModel)
            return;

        // First update the DataGrid layout ...
        viewModel.ClearStorageContents();
        ContentsGrid.Columns.Clear();
        for (int i = 0; i < storage.Columns.Count; i++)
        {
            var column = new DataGridTextColumn { Header = storage.Columns[i], Binding = new Binding($"[{i}]") };
            ContentsGrid.Columns.Add(column);
        }

        // ... then bind the new data
        viewModel.LoadStorageContents(storage);
    }

    public void CreateStorage(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        if (StorageName.Text is not null)
            viewModel.CreateStorage(StorageName.Text);
    }
}