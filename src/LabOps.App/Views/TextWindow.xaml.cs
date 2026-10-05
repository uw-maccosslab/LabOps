using System.Windows;
using LabOps.Core.Infrastructure;

namespace LabOps.App.Views;

/// <summary>Shows read-only text, such as what changed between two versions of a protocol.</summary>
public partial class TextWindow : Window
{
    private TextWindow(Window? owner, string heading, string text)
    {
        InitializeComponent();
        Owner = owner;
        Title = AppInfo.ProductName;
        Heading.Text = heading;
        Body.Text = text;
    }

    public static void Show(Window? owner, string heading, string text) => new TextWindow(owner, heading, text).ShowDialog();
}
