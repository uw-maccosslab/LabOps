using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Markup;
using LabOps.App.Views;

namespace LabOps.Tests.App;

/// <summary>
/// Views whose markup only fails when it is loaded (a style based on the wrong type, a resource
/// that is not there): they are built here, with the app's own resources, on a UI thread.
/// </summary>
public sealed partial class ViewLoadTests
{
    [Fact]
    public void The_projects_view_and_the_plan_window_load()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources = AppResources();
                var view = new ProjectsView();
                view.FindName("OverviewPage").ShouldNotBeNull();
                view.FindName("SearchBox").ShouldNotBeNull();
                var plan = (Window)Activator.CreateInstance(typeof(PlanWindow), BindingFlags.Instance | BindingFlags.NonPublic, null,
                    [null, "Plan Sample prep", (DateOnly?)new DateOnly(2026, 10, 12), null], null)!;
                plan.FindName("ClearButton").ShouldNotBeNull();
                plan.Close();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        failure.ShouldBeNull();
    }

    /// <summary>App.xaml's resources, read from the source as a dictionary (the App itself needs services to start).</summary>
    private static ResourceDictionary AppResources()
    {
        var folder = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(folder, "LabOps.sln")))
        {
            folder = Path.GetDirectoryName(folder)!;
        }

        var xaml = File.ReadAllText(Path.Combine(folder, "src", "LabOps.App", "App.xaml"));
        var inner = Resources().Match(xaml).Groups[1].Value;
        var dictionary = "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" "
                         + "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" "
                         + "xmlns:local=\"clr-namespace:LabOps.App;assembly=LabOps\">" + inner + "</ResourceDictionary>";
        return (ResourceDictionary)XamlReader.Parse(dictionary);
    }

    [GeneratedRegex(@"<Application\.Resources>(.*)</Application\.Resources>", RegexOptions.Singleline)]
    private static partial Regex Resources();
}
