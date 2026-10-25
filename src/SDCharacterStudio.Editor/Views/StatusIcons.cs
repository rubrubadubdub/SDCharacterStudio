using System.Windows.Data;
using SDCharacterStudio.Editor.Infrastructure;
using Wpf.Ui.Controls;

namespace SDCharacterStudio.Editor.Views;

/// <summary>Small converters for status icons in the shell.</summary>
public static class StatusIcons
{
    public static readonly IValueConverter Catalog = new FuncConverter(v => v is true ? SymbolRegular.CheckmarkCircle24 : SymbolRegular.Warning24);
    public static readonly IValueConverter CatalogBrush = new FuncConverter(v => StudioColors.Brush(v is true ? StudioColors.Good : StudioColors.Caution));
    public static readonly IValueConverter Severity = new FuncConverter(v => v is true ? SymbolRegular.ErrorCircle24 : SymbolRegular.Warning24);
    public static readonly IValueConverter Lane = new FuncConverter(v => v switch
    {
        ViewModels.LaneViewModel { IsMain: true } => SymbolRegular.Play24,
        ViewModels.LaneViewModel { IsSkip: true } => SymbolRegular.FastForward24,
        _ => SymbolRegular.BranchFork24
    });
}
