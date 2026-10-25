using System.IO;
using System.Windows.Controls;
using System.Windows.Data;
using SDCharacterStudio.Editor.Infrastructure;

namespace SDCharacterStudio.Editor.Views;

public partial class WelcomeView : UserControl
{
    public static readonly IValueConverter FileTitle = new FuncConverter(v => v is string path ? Path.GetFileNameWithoutExtension(path) : "");
    public WelcomeView() => InitializeComponent();
}
