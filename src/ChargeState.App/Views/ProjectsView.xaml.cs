using System.Windows.Controls;

namespace ChargeState.App.Views;

/// <summary>The Projects area; its DataContext is the <see cref="ViewModels.ProjectsViewModel"/>.</summary>
public partial class ProjectsView : UserControl
{
    public ProjectsView()
    {
        InitializeComponent();
    }

    /// <summary>Puts the cursor in the search box.</summary>
    public void FocusSearch() => SearchBox.Focus();
}
