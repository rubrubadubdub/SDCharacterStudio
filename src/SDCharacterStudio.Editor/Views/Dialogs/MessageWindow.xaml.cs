using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace SDCharacterStudio.Editor.Views.Dialogs;

/// <summary>Confirm, yes/no/cancel and text prompts, in the app's theme.</summary>
public partial class MessageWindow : FluentWindow
{
    private Func<string, string?>? validate;

    private MessageWindow(string title, string message, string yes, string? no, string cancel)
    {
        InitializeComponent();
        Title = title; Bar.Title = title;
        Message.Text = message;
        YesButton.Content = yes;
        CancelButton.Content = cancel;
        if (no != null) { NoButton.Content = no; NoButton.Visibility = Visibility.Visible; }
        if (cancel.Length == 0) CancelButton.Visibility = Visibility.Collapsed;
    }

    public static void Info(Window? owner, string title, string message) => Show(new MessageWindow(title, message, "OK", null, ""), owner);

    /// <summary>Yes = true, No = false, Cancel/close = null.</summary>
    public bool? Answer { get; private set; }
    public string? Text { get; private set; }

    public static bool Confirm(Window? owner, string title, string message, string confirm, string cancel)
        => Show(new MessageWindow(title, message, confirm, null, cancel), owner).Answer == true;

    public static bool? Ask(Window? owner, string title, string message, string yes, string no)
        => Show(new MessageWindow(title, message, yes, no, "Cancel"), owner).Answer;

    public static string? Prompt(Window? owner, string title, string label, string initial, Func<string, string?>? validate)
    {
        var window = new MessageWindow(title, "", "OK", null, "Cancel") { validate = validate };
        window.Message.Visibility = Visibility.Collapsed;
        window.InputLabel.Text = label; window.InputLabel.Visibility = Visibility.Visible;
        window.Input.Text = initial; window.Input.Visibility = Visibility.Visible;
        window.Loaded += (_, _) => { window.Input.Focus(); window.Input.SelectAll(); };
        return Show(window, owner).Answer == true ? window.Text : null;
    }

    private static MessageWindow Show(MessageWindow window, Window? owner)
    {
        if (owner is { IsLoaded: true }) window.Owner = owner;
        else window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.ShowDialog();
        return window;
    }

    private void Input_TextChanged(object sender, TextChangedEventArgs e)
    {
        string? problem = validate?.Invoke(Input.Text);
        InputError.Text = problem;
        InputError.Visibility = problem == null ? Visibility.Collapsed : Visibility.Visible;
        YesButton.IsEnabled = problem == null;
    }

    private void Yes_Click(object sender, RoutedEventArgs e)
    {
        if (Input.Visibility == Visibility.Visible)
        {
            if (validate?.Invoke(Input.Text) != null) return;
            Text = Input.Text;
        }
        Answer = true; Close();
    }

    private void No_Click(object sender, RoutedEventArgs e) { Answer = false; Close(); }
    private void Cancel_Click(object sender, RoutedEventArgs e) { Answer = null; Close(); }
}
