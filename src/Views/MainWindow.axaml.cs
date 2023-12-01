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
using System.Security.Cryptography.X509Certificates;

namespace Datagent.Views;

/* TODOs:
 * 1. Add new row automatically after the last row has been edited
 * 2. Set default order of entries by sorting (asc) on the first column
 * 3. Setup backups to local drive and/or to remote storages (Google, Yandex, etc.)
 */

public partial class MainWindow : Window
{
    public MainWindowViewModel ViewModel { get; }

    public MainWindow()
    {
        DataContext = ViewModel = new MainWindowViewModel(StorageProvider);
        InitializeComponent();

        // Allow entering cell edit mode by pressing Enter
        ContentsGrid.AddHandler(KeyDownEvent, OnDataGridKeyDown, RoutingStrategies.Tunnel);
    }

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

    public void CreateTable_Confirm(object sender, RoutedEventArgs e)
    {
        ViewModel.CreateTable(CreateTable_Name.Text);

        var button = (Button)sender;
        var flyout = (Popup)button.Tag;
        flyout.Close();
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
        var column = ((TextBlock)e.Column.Header).Text;
        ViewModel.UpdateRow(row, column);
    }

    // DataGrid's inner key handler treats Enter like ArrowDown (commit + move down);
    // hence, to allow it to be used for entering cell edit mode:
    // - every 1st time (pressed Enter while not editing) disable inner handler and enter cell edit mode
    // - every 2nd time (pressed Enter while editing) let inner handler do its job in a usual way
    private bool _dataGridEnterEditMode = true;
    public void OnDataGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            // If this is "true", inner handler won't do anything
            // as the event is now considered processed
            e.Handled = _dataGridEnterEditMode;
            if (!ContentsGrid.BeginEdit(e))
                ContentsGrid.CommitEdit();
            
            _dataGridEnterEditMode = !_dataGridEnterEditMode;
        }
    }
}