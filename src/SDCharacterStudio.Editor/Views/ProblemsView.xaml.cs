using System.Windows.Controls;
using System.Windows.Input;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Views;

public partial class ProblemsView : UserControl
{
    public ProblemsView() => InitializeComponent();

    private void List_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as ListBox)?.SelectedItem is ProblemItemViewModel item) item.JumpCommand.Execute(null);
    }

    private void List_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (sender as ListBox)?.SelectedItem is ProblemItemViewModel item) { item.JumpCommand.Execute(null); e.Handled = true; }
    }
}
