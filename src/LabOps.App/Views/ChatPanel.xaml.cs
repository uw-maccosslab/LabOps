using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using LabOps.App.ViewModels;

namespace LabOps.App.Views;

public partial class ChatPanel : UserControl
{
    public ChatPanel()
    {
        InitializeComponent();
        ((INotifyCollectionChanged)Transcript.Items).CollectionChanged += (_, e) =>
        {
            Dispatcher.InvokeAsync(Scroller.ScrollToEnd, System.Windows.Threading.DispatcherPriority.Background);
            // A question is answered in the box below, so the cursor goes there, unless the person
            // is typing somewhere else.
            if (e.NewItems?.OfType<QuestionItem>().Any() == true)
            {
                Dispatcher.InvokeAsync(() =>
                {
                    if (!IsTypingElsewhere())
                    {
                        ChatInput.Focus();
                    }
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
        };
    }

    /// <summary>True when the keyboard is in a text box outside the chat, such as a search or a step's note.</summary>
    private bool IsTypingElsewhere() =>
        Keyboard.FocusedElement is TextBoxBase or PasswordBox or ComboBox { IsEditable: true }
        && !IsKeyboardFocusWithin;

    /// <summary>
    /// Enter sends the message (or, while Claude waits on a question, answers it), as in other chat
    /// apps; Shift+Enter starts a new line. While Claude is still working, Enter keeps the text to
    /// send when it finishes.
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

    /// <summary>Enter, or Ctrl+Enter, but not Shift+Enter (a new line) and not while an input method is composing.</summary>
    private static bool IsPlainEnter(KeyEventArgs e) =>
        e.Key == Key.Enter && e.ImeProcessedKey == Key.None && (Keyboard.Modifiers & ModifierKeys.Shift) == 0;
}
