using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System;
using System.Diagnostics;
using System.Data.Common;
using Datagent.ViewModels;
using DynamicData;
using Avalonia.Data;
using Avalonia.LogicalTree;
using Avalonia.Controls.Primitives;

namespace Datagent.Views;

public partial class MainWindow : Window
{
    public MainWindowViewModel ViewModel { get; }

    public MainWindow()
    {
        DataContext = ViewModel = new MainWindowViewModel(StorageProvider);
        InitializeComponent();
    }

    private void UpdateDataGridLayout(Database.Table table)
    {
        ContentsGrid.Columns.Clear();
        for (int i = 0; i < table.Columns.Count; i++)
        {
            var column = new DataGridTextColumn
            {
                Header = table.Columns[i].Name,
                Binding = new Binding($"[{i}]"),
            };
            ContentsGrid.Columns.Add(column);
        }
    }

    public void CreateTable(object sender, RoutedEventArgs e)
    {
        if (StorageName.Text is not null)
            ViewModel.CreateTable(StorageName.Text);
    }

    public void SelectTable(object sender, SelectionChangedEventArgs e)
    {
        if (e.RemovedItems.Count > 1 || e.AddedItems.Count != 1 || e.AddedItems[0] is not Database.Table tableNew)
            return;

        // Clear the old contents
        ViewModel.ClearTableContents(e.RemovedItems.Count == 1 ? e.RemovedItems[0] as Database.Table : null);

        UpdateDataGridLayout(tableNew);

        // Bind the new contents
        ViewModel.LoadTableContents(tableNew);
    }

    public void AddColumn_Confirm(object sender, RoutedEventArgs e)
    {
        ViewModel.AddColumn(AddColumn_Name.Text);
        UpdateDataGridLayout(ViewModel.CurrentTable);

        var button = (Button)sender;
        var flyout = (Popup)button.Tag;
        flyout.Close();
    }

    public void DeleteColumn_Confirm(object sender, RoutedEventArgs e)
    {
        ViewModel.DeleteColumn(((MenuItem)sender).CommandParameter);
        UpdateDataGridLayout(ViewModel.CurrentTable);
    }
}