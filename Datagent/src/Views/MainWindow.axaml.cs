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
using Avalonia.Threading;
using System.Linq;
using System.ComponentModel;

namespace Datagent.Views;

/* TODOs:
 * 1. Add filtering; search for texts, ranges for numbers/datetimes, etc.
 * 1. Setup backups to local drive and/or to remote storages (Google, Yandex, etc.)
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
        for (int i = 0; i < ViewModel.TableColumns.Count; i++)
        {
            var column = new DataGridTextColumn
            {
                Header = new TextBlock
                {
                    [!TextBlock.TextProperty] = new Binding
                    {
                        Source = ViewModel,
                        Path = $"TableColumns[{i}].Name"
                    }
                },
                Binding = new Binding($"[{i}]"),
            };
            ContentsGrid.Columns.Add(column);
        }

        // Sort on the first column
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (ContentsGrid.Columns.Count > 0)
                ContentsGrid.Columns.First().Sort(ListSortDirection.Ascending);
        });
    }

    private void ProcessButtonFlyout(object sender, RoutedEventArgs e, Action action)
    {
        action();

        var button = (sender as Button).FindLogicalAncestorOfType<Button>();
        button.Flyout?.Hide();
    }

    public void CreateTable_Confirm(object sender, RoutedEventArgs e)
    {
        ProcessButtonFlyout(sender, e, () =>
        {
            ViewModel.CreateTable(CreateTable_Name.Text);
        });
    }

    private void SwitchTable(Database.Table? tableOld, Database.Table? tableNew)
    {
        // Clear the old contents
        ViewModel.ClearTableContents(tableOld);

        ViewModel.CurrentTable = tableNew;

        UpdateDataGridLayout();

        // Bind the new contents
        ViewModel.LoadTableContents();
    }

    public void SelectTable(object sender, SelectionChangedEventArgs e)
    {
        if (e.RemovedItems.Count > 1 || e.AddedItems.Count != 1 || e.AddedItems[0] is not Database.Table tableNew)
            return;

        SwitchTable(e.RemovedItems.Count == 1 ? e.RemovedItems[0] as Database.Table : null, tableNew);
    }

    public void DeleteTable(object sender, RoutedEventArgs e)
    {
        ViewModel.DeleteTable();

        SwitchTable(ViewModel.CurrentTable, null);
    }

    public void AddRows_Confirm(object sender, RoutedEventArgs e)
    {
        ProcessButtonFlyout(sender, e, () =>
        {
            ViewModel.AddRows(AddRows_Count.Value);
        });
    }

    public void AddColumn_Confirm(object sender, RoutedEventArgs e)
    {
        ProcessButtonFlyout(sender, e, () =>
        {
            ViewModel.AddColumn(AddColumn_Name.Text);
            UpdateDataGridLayout();
        });
    }

    private string GetColumnHeaderName(object headerContent) => (headerContent as TextBlock).Text;
    private string GetColumnHeaderName(DataGridColumnHeader header) => GetColumnHeaderName(header.Content);
    private string GetColumnHeaderName(DataGridColumn column) => GetColumnHeaderName(column.Header);

    // When a flyout is called from a context menu, the former's interactivity becomes broken;
    // such a flyout has to be called *only after* the context menu is completely closed
    public void OnHeaderContextMenuClosed(object? sender, RoutedEventArgs e)
    {
        var header = (sender as ContextMenu).FindLogicalAncestorOfType<DataGridColumnHeader>();
        FlyoutBase.ShowAttachedFlyout(header);
    }

    public void OnHeaderContextMenuItemFlyoutClosed(object? sender, EventArgs e)
    {
        var header = (sender as FlyoutBase).Target;
        FlyoutBase.SetAttachedFlyout(header, null);
    }

    private void ProcessHeaderContextMenuItem(object sender, RoutedEventArgs e, Action<DataGridColumnHeader> action)
    {
        var item = sender as MenuItem;
        var header = item.FindLogicalAncestorOfType<DataGridColumnHeader>();

        action(header);

        // Attach the MenuItem's flyout to the corresponding column header
        var flyout = FlyoutBase.GetAttachedFlyout(item);
        FlyoutBase.SetAttachedFlyout(header, flyout);
        flyout.Closed += OnHeaderContextMenuItemFlyoutClosed;
    }

    private void ProcessHeaderContextMenuItemFlyout(object sender, RoutedEventArgs e, Action<DataGridColumnHeader> action)
    {
        var header = (sender as Button).FindLogicalAncestorOfType<DataGridColumnHeader>();

        action(header);

        // Hide the flyout and detach it from the header
        FlyoutBase.GetAttachedFlyout(header)?.Hide();
    }

    public void Search_Submit(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            ViewModel.Search();
    }

    public void EditColumn(object sender, RoutedEventArgs e)
    {
        ProcessHeaderContextMenuItem(sender, e, header =>
        {
            EditColumn_Name.Text = GetColumnHeaderName(header);
        });
    }

    public void EditColumn_Confirm(object sender, RoutedEventArgs e)
    {
        ProcessHeaderContextMenuItemFlyout(sender, e, header =>
        {
            ViewModel.UpdateColumn(GetColumnHeaderName(header), EditColumn_Name.Text);
        });
    }

    public void FilterColumn(object sender, RoutedEventArgs e)
    {
        ProcessHeaderContextMenuItem(sender, e, header =>
        {
            FilterColumn_Name.Text = "";
        });
    }

    public void FilterColumn_Confirm(object sender, RoutedEventArgs e)
    {
        ProcessHeaderContextMenuItemFlyout(sender, e, header =>
        {
            ViewModel.FilterColumn(GetColumnHeaderName(header), FilterColumn_Name.Text);
        });
    }

    public void DeleteColumn(object sender, RoutedEventArgs e)
    {
        ProcessHeaderContextMenuItem(sender, e, header =>
        {
            FilterColumn_Name.Text = GetColumnHeaderName(header);
        });
    }

    public void DeleteColumn_Confirm(object sender, RoutedEventArgs e)
    {
        ProcessHeaderContextMenuItemFlyout(sender, e, header =>
        {
            ViewModel.DeleteColumn(GetColumnHeaderName(header));
            UpdateDataGridLayout();
        });
    }

    public void UpdateRow(object sender, DataGridCellEditEndedEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Cancel)
            return;

        var row = e.Row.DataContext;
        var column = GetColumnHeaderName(e.Column);
        ViewModel.UpdateRow(row, column);
    }

    public void OnDataGridKeyDown(object? sender, KeyEventArgs e)
    {
        // The DataGrid's inner key handler treats Enter like ArrowDown (commit + move down);
        // hence, to allow Enter to be used for entering cell edit mode:
        // - if the cell is unfocused, disable inner handler and enter cell edit mode
        // - if the cell is already focused (either with Enter or LMB, F2, etc.), commit the edits
        if (e.Key == Key.Enter)
        {
            if (FocusManager.GetFocusedElement() is not TextBox)
            {
                // If e.Handled == true, inner handler won't do anything
                // as the event is now considered processed
                e.Handled = true;
                ContentsGrid.BeginEdit(e);
            }
            else
            {
                ContentsGrid.CommitEdit();
            }
        }
    }
}