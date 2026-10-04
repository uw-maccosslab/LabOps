using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace ChargeState.App;

/// <summary>
/// Click a column header to sort a ListView by that column; click again to reverse. Set
/// <c>GridViewSort.Enabled="True"</c> on the ListView and <c>GridViewSort.Property</c> on each
/// sortable column (the property to sort by, which may differ from what the column shows, for
/// example a date rather than its formatted text).
/// </summary>
/// <remarks>
/// Sorting is on the collection's default view, so it survives the list being refilled when the
/// search or filter changes. With no column chosen, the list keeps the order it was given.
/// </remarks>
public static class GridViewSort
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(GridViewSort), new PropertyMetadata(false, OnEnabledChanged));

    public static readonly DependencyProperty PropertyProperty = DependencyProperty.RegisterAttached(
        "Property", typeof(string), typeof(GridViewSort), new PropertyMetadata(null));

    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);

    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

    public static string? GetProperty(DependencyObject element) => (string?)element.GetValue(PropertyProperty);

    public static void SetProperty(DependencyObject element, string? value) => element.SetValue(PropertyProperty, value);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListView list)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            list.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(OnHeaderClick));
        }
        else
        {
            list.RemoveHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(OnHeaderClick));
        }
    }

    private static void OnHeaderClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ListView list || e.OriginalSource is not GridViewColumnHeader { Column: { } column }
            || GetProperty(column) is not { Length: > 0 } property || list.ItemsSource is null)
        {
            return;
        }

        var view = CollectionViewSource.GetDefaultView(list.ItemsSource);
        var current = view.SortDescriptions.FirstOrDefault();
        var direction = current.PropertyName == property && current.Direction == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;

        view.SortDescriptions.Clear();
        view.SortDescriptions.Add(new SortDescription(property, direction));

        // An arrow on the sorted column's header; the others lose theirs.
        if (list.View is GridView grid)
        {
            foreach (var c in grid.Columns)
            {
                if (c.Header is string text)
                {
                    c.Header = text.TrimEnd(' ', '▲', '▼');
                }
            }

            if (column.Header is string header)
            {
                column.Header = $"{header} {(direction == ListSortDirection.Ascending ? '▲' : '▼')}";
            }
        }
    }
}
