using System.Collections.Specialized;
using System.Windows.Controls;

namespace LabOps.App.Views;

public partial class ChatPanel : UserControl
{
    public ChatPanel()
    {
        InitializeComponent();
        ((INotifyCollectionChanged)Transcript.Items).CollectionChanged += (_, _) =>
            Dispatcher.InvokeAsync(Scroller.ScrollToEnd, System.Windows.Threading.DispatcherPriority.Background);
    }
}
