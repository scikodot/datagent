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
using static Datagent.ViewModels.Database.Table;

namespace Datagent.Views;

public partial class MainWindow : Window
{
    public MainWindowViewModel ViewModel { get; }

    public MainWindow()
    {
        DataContext = ViewModel = new MainWindowViewModel(StorageProvider);
        InitializeComponent();
    }

    //private void UpdateDataGridLayout(Database.Table table)
    //{
    //    ContentsGrid.Columns.Clear();
    //    for (int i = 0; i < table.Columns.Count; i++)
    //    {
    //        var column = new DataGridTextColumn
    //        {
    //            Header = table.Columns[i].Name,
    //            Binding = new Binding($"[{i}]"),
    //        };
    //        ContentsGrid.Columns.Add(column);
    //    }
    //}

    //public void CreateTable(object sender, RoutedEventArgs e)
    //{
    //    if (StorageName.Text is not null)
    //        ViewModel.CreateTable(StorageName.Text);
    //}

    //public void SelectTable(object sender, SelectionChangedEventArgs e)
    //{
    //    if (e.RemovedItems.Count > 1 || e.AddedItems.Count != 1 || e.AddedItems[0] is not Database.Table tableNew)
    //        return;

    //    // Clear the old contents
    //    ViewModel.ClearTableContents(e.RemovedItems.Count == 1 ? e.RemovedItems[0] as Database.Table : null);

    //    UpdateDataGridLayout(tableNew);

    //    // Bind the new contents
    //    ViewModel.LoadTableContents(tableNew);
    //}

    //public void AddColumn_Confirm(object sender, RoutedEventArgs e)
    //{
    //    ViewModel.AddColumn(AddColumn_Name.Text);
    //    UpdateDataGridLayout(ViewModel.CurrentTable);

    //    var button = (Button)sender;
    //    var flyout = (Popup)button.Tag;
    //    flyout.Close();
    //}

    //public void DeleteColumn_Confirm(object sender, RoutedEventArgs e)
    //{
    //    ViewModel.DeleteColumn(((MenuItem)sender).CommandParameter);
    //    UpdateDataGridLayout(ViewModel.CurrentTable);
    //}

    //public void UpdateRow(object sender, DataGridCellEditEndedEventArgs e)
    //{
    //    if (e.EditAction == DataGridEditAction.Cancel)
    //        return;

    //    var row = e.Row.DataContext;
    //    var column = e.Column.Header;
    //    ViewModel.UpdateRow(row, column);
    //}

    private void UpdateDataGridLayout()
    {
        ContentsGrid.Columns.Clear();
        for (int i = 0; i < ViewModel.CurrentTable.Columns.Count; i++)
        {
            var column = new DataGridTextColumn
            {
                Header = new TextBlock
                {
                    [!TextBlock.TextProperty] = new Binding
                    {
                        Source = ViewModel,
                        Path = $"CurrentTable.Columns[{i}].Name"
                    }
                },
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

        ViewModel.CurrentTable = tableNew;

        UpdateDataGridLayout();

        // Bind the new contents
        ViewModel.LoadTableContents();
    }

    public void AddColumn_Confirm(object sender, RoutedEventArgs e)
    {
        ViewModel.AddColumn(AddColumn_Name.Text);
        UpdateDataGridLayout();

        var button = (Button)sender;
        var flyout = (Popup)button.Tag;
        flyout.Close();
    }

    public void EditColumn(object sender, RoutedEventArgs e)
    {
        var menuItem = (MenuItem)sender;
        var header = (DataGridColumnHeader)menuItem.Tag;

        EditColumn_Name.Text = ((TextBlock)header.Content).Text;
        _controlAttachedFlyoutFromContextMenu = header;
    }

    // When a flyout is called from a context menu, the former's interactivity becomes broken;
    // such a flyout has to be called *only after* the context menu is completely closed
    private Control? _controlAttachedFlyoutFromContextMenu = null;
    private void OnContextMenuClosed(object? sender, RoutedEventArgs e)
    {
        if (_controlAttachedFlyoutFromContextMenu != null)
            FlyoutBase.ShowAttachedFlyout(_controlAttachedFlyoutFromContextMenu);
    }

    public void EditColumn_Confirm(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        var flyout = (Popup)button.Tag;
        var header = (DataGridColumnHeader)flyout.Parent;

        ViewModel.UpdateColumn(((TextBlock)header.Content).Text, EditColumn_Name.Text);

        flyout.Close();
    }

    public void DeleteColumn_Confirm(object sender, RoutedEventArgs e)
    {
        ViewModel.DeleteColumn(((MenuItem)sender).CommandParameter);
        UpdateDataGridLayout();
    }

    public void UpdateRow(object sender, DataGridCellEditEndedEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Cancel)
            return;

        var row = e.Row.DataContext;
        var column = e.Column.Header;
        ViewModel.UpdateRow(row, column);
    }
}