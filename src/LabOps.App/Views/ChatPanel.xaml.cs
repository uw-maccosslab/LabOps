using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LabOps.App.ViewModels;

namespace LabOps.App.Views;

public partial class ChatPanel : UserControl
{
    public ChatPanel()
    {
        InitializeComponent();
        ((INotifyCollectionChanged)Transcript.Items).CollectionChanged += (_, _) =>
            Dispatcher.InvokeAsync(Scroller.ScrollToEnd, System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>
    /// Enter sends the message, as in other chat apps; Shift+Enter starts a new line. While Claude
    /// is still working, Enter keeps the text to send when it finishes.
    /// </summary>
    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsPlainEnter(e) || DataContext is not ChatViewModel chat)
        {
            return;
        }

        e.Handled = true;
        if (chat.SendCommand.CanExecute(null))
        {
            chat.SendCommand.Execute(null);
        }
    }

    /// <summary>Enter in the box under one of Claude's questions answers it.</summary>
    private void OnAnswerKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsPlainEnter(e) || (sender as FrameworkElement)?.DataContext is not QuestionItem question)
        {
            return;
        }

        e.Handled = true;
        if (question.AnswerCommand.CanExecute(""))
        {
            question.AnswerCommand.Execute("");
        }
    }

    /// <summary>Enter, or Ctrl+Enter, but not Shift+Enter (a new line) and not while an input method is composing.</summary>
    private static bool IsPlainEnter(KeyEventArgs e) =>
        e.Key == Key.Enter && e.ImeProcessedKey == Key.None && (Keyboard.Modifiers & ModifierKeys.Shift) == 0;
}
