using System.Windows;

namespace PcOptimizer.App.Services;

public sealed class MessageBoxPrompt : IUserPrompt
{
    public bool ConfirmDestructive(string title, string message)
        => MessageBox.Show(
               message,
               title,
               MessageBoxButton.OKCancel,
               MessageBoxImage.Warning,
               MessageBoxResult.Cancel)
           == MessageBoxResult.OK;
<<<<<<< HEAD

    public bool Ask(string title, string message)
        => MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)
           == MessageBoxResult.Yes;
=======
>>>>>>> 77a6a47fbf3cb9b7c8cc565933bd42c37265aab0
}
