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

        var columnNames = viewModel.GetColumnsNames(storage);

        // First update the DataGrid layout ...
        ContentsGrid.Columns.Clear();
        foreach (var columnName in columnNames)
            ContentsGrid.Columns.Add(new DataGridTextColumn { Header = columnName, Binding = new Binding(columnName) });

        // ... and only then refresh its binding;
        // this prevents silent errors produced by DataGrid desync
        viewModel.RefreshContents(storage);
    }
}