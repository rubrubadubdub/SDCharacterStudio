using System.Windows;
using System.Windows.Controls;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Views;

public partial class OutputView : UserControl
{
    public OutputView() => InitializeComponent();

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is OutputViewModel output && output.Lines.Count > 0)
            Clipboard.SetText(string.Join(Environment.NewLine, output.Lines.Select(l => $"{l.Stamp} [{l.Level}] {l.Text}")));
    }
}
