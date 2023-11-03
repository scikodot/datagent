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
    public MainWindowViewModel ViewModel { get; }

    public MainWindow()
    {
        DataContext = ViewModel = new MainWindowViewModel(StorageProvider);
        InitializeComponent();
    }

    public void CreateTable(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        if (StorageName.Text is not null)
            viewModel.CreateTable(StorageName.Text);
    }

    public void SelectTable(object sender, SelectionChangedEventArgs e)
    {
        if (e.RemovedItems.Count > 1 || e.AddedItems.Count != 1 || e.AddedItems[0] is not Database.Table tableNew)
            return;

        // Clear the old contents
        ViewModel.ClearTableContents(e.RemovedItems.Count == 1 ? e.RemovedItems[0] as Database.Table : null);

        // Update the DataGrid layout
        ContentsGrid.Columns.Clear();
        for (int i = 0; i < tableNew.Columns.Count; i++)
        {
            var column = new DataGridTextColumn { Header = tableNew.Columns[i].Name, Binding = new Binding($"[{i}]") };
            ContentsGrid.Columns.Add(column);
        }

        // Bind the new contents
        ViewModel.LoadTableContents(tableNew);
    }
}