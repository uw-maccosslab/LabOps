using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using LabOps.App.Services;
using LabOps.App.ViewModels;
using LabOps.Core.Projects;

namespace LabOps.App.Views;

/// <summary>A project's sample table in a sortable, searchable grid. It does not change the file.</summary>
public partial class SamplesWindow : Window
{
    private readonly SamplesViewModel _vm;

    private SamplesWindow(Window? owner, SamplesViewModel vm)
    {
        InitializeComponent();
        Owner = owner;
        _vm = vm;
        DataContext = vm;
        vm.PropertyChanged += OnViewModelChanged;
        Loaded += async (_, _) =>
        {
            SearchBox.Focus();
            await _vm.InitializeAsync().ConfigureAwait(true);
        };
        Closed += (_, _) => vm.PropertyChanged -= OnViewModelChanged;
    }

    /// <summary>Opens the window beside the main one, so the project stays usable while it is open.</summary>
    public static void Open(Window? owner, SamplesViewModel vm) => new SamplesWindow(owner, vm).Show();

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SamplesViewModel.Rows))
        {
            BuildColumns();
        }
    }

    /// <summary>One column per header, numbers right aligned.</summary>
    private void BuildColumns()
    {
        Grid.Columns.Clear();
        if (_vm.Rows?.Table is not { } table)
        {
            return;
        }

        var right = new Style(typeof(TextBlock), DataGridTextColumn.DefaultElementStyle);
        right.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
        foreach (System.Data.DataColumn column in table.Columns)
        {
            if (column.ColumnName == SampleTable.SearchColumn)
            {
                continue;
            }

            var gridColumn = new DataGridTextColumn
            {
                Header = column.Caption,
                Binding = new Binding(column.ColumnName),
                SortMemberPath = column.ColumnName,
                MaxWidth = 360,
            };
            if (column.DataType == typeof(decimal))
            {
                gridColumn.ElementStyle = right;
            }

            Grid.Columns.Add(gridColumn);
        }
    }

    private void OnShowInFolder(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedFile is { } file)
        {
            Shell.Reveal(file.Path);
        }
    }
}
