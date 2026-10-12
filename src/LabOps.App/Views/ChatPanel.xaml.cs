using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;
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
        if (IsPaste(e) && AttachFromClipboard())
        {
            e.Handled = true;
            return;
        }

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

    /// <summary>
    /// Ctrl+V of files copied in Explorer, or of a picture with no text (a screen capture), attaches
    /// them, as dropping them would; true when it did. Text is left to paste as text.
    /// </summary>
    private bool AttachFromClipboard()
    {
        if (DataContext is not ChatViewModel chat || !chat.AttachCommand.CanExecute(null))
        {
            return false;
        }

        IDataObject? data;
        try
        {
            data = Clipboard.GetDataObject();
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Another program has the clipboard open; the paste goes ahead as text.
            return false;
        }

        var (files, png) = data is null ? (null, null) : WhatToAttach(data);
        if (files is not null)
        {
            Attach(chat.AddAttachmentsAsync(files));
            return true;
        }

        if (png is not null)
        {
            Attach(chat.AttachImageAsync(png));
            return true;
        }

        return false;
    }

    private static async void Attach(Task attaching) => await attaching;

    /// <summary>
    /// What a paste attaches: the files copied in Explorer, or a picture as PNG when nothing on the
    /// clipboard is text; neither when it should paste as text.
    /// </summary>
    internal static (string[]? Files, byte[]? Png) WhatToAttach(IDataObject data)
    {
        if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
        {
            return (files, null);
        }

        if (data.GetDataPresent(DataFormats.UnicodeText) || data.GetDataPresent(DataFormats.Text))
        {
            return (null, null);
        }

        // Windows' screen capture puts a PNG on the clipboard too, which keeps transparency.
        if (data.GetDataPresent("PNG") && data.GetData("PNG") is MemoryStream png && png.Length > 0)
        {
            return (null, png.ToArray());
        }

        if (data.GetDataPresent(DataFormats.Bitmap) && data.GetData(DataFormats.Bitmap) is BitmapSource image)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return (null, stream.ToArray());
        }

        return (null, null);
    }

    /// <summary>Ctrl+V, or Shift+Insert.</summary>
    private static bool IsPaste(KeyEventArgs e) =>
        (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control) || (e.Key == Key.Insert && Keyboard.Modifiers == ModifierKeys.Shift);

    /// <summary>Files dragged over the message box are attached, not pasted as text.</summary>
    private void OnDragOverInput(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DataContext is ChatViewModel chat && chat.AttachCommand.CanExecute(null) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }
    }

    private async void OnDropOnInput(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && DataContext is ChatViewModel chat)
        {
            e.Handled = true;
            await chat.AddAttachmentsAsync(files);
        }
    }

    /// <summary>Enter, or Ctrl+Enter, but not Shift+Enter (a new line) and not while an input method is composing.</summary>
    private static bool IsPlainEnter(KeyEventArgs e) =>
        e.Key == Key.Enter && e.ImeProcessedKey == Key.None && (Keyboard.Modifiers & ModifierKeys.Shift) == 0;
}
