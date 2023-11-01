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
        DataContext = new MainWindowViewModel();
        InitializeComponent();
    }

    public void SelectStorage(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0 || e.AddedItems[0] is not Storage storage)
            return;

        if (DataContext is not MainWindowViewModel viewModel)
            return;

        viewModel.LoadStorageColumns(storage);

        // First update the DataGrid layout ...
        viewModel.ClearStorageContents();
        ContentsGrid.Columns.Clear();
        for (int i = 0; i < viewModel.StorageColumns.Count; i++)
        {
            var column = new DataGridTextColumn { Header = viewModel.StorageColumns[i], Binding = new Binding($"[{i}]") };
            ContentsGrid.Columns.Add(column);
        }

        // ... then bind the new data
        viewModel.LoadStorageContents(storage);
    }
}