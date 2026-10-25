using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SDCharacterStudio.Editor.Views.Studio;

/// <summary>Two-way "value equals parameter" for enum radio buttons (tools, modes).</summary>
public sealed class EnumIsConverter : IValueConverter
{
    public static readonly EnumIsConverter Instance = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true && parameter is string name ? targetType.IsEnum ? Enum.Parse(targetType, name) : System.Convert.ChangeType(name, targetType, CultureInfo.InvariantCulture) : Binding.DoNothing;
}

public static class StudioConverters
{
    public static readonly IValueConverter EnumIs = EnumIsConverter.Instance;
    /// <summary>"12×" for zoom labels.</summary>
    public static readonly IValueConverter ZoomText = new FuncConverter(v => v is double d ? $"{Math.Round(d)}×" : "");
    public static readonly IValueConverter Milliseconds = new FuncConverter(v => v is double d ? $"{d:0} ms" : v is int i ? $"{i} ms" : "");
    public static readonly IValueConverter PlayIcon = new FuncConverter(v => v is true ? Wpf.Ui.Controls.SymbolRegular.Stop24 : Wpf.Ui.Controls.SymbolRegular.Play24);
    /// <summary>Visible when every bound value is true.</summary>
    public static readonly IMultiValueConverter AllVisible = new AllVisibleConverter();

    private sealed class AllVisibleConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) => values.All(v => v is true) ? Visibility.Visible : Visibility.Collapsed;
        public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    /// <summary>An ARGB colour (uint) as a brush.</summary>
    public static readonly IValueConverter ArgbBrush = new FuncConverter(v => v is uint c ? Frozen((byte)(c >> 24), (byte)(c >> 16), (byte)(c >> 8), (byte)c) : System.Windows.Media.Brushes.Transparent);
    public static readonly IValueConverter PauseIcon = new FuncConverter(v => v is true ? Wpf.Ui.Controls.SymbolRegular.Pause24 : Wpf.Ui.Controls.SymbolRegular.Play24);
    public static readonly IValueConverter NotNullVisible = new FuncConverter(v => v != null ? Visibility.Visible : Visibility.Collapsed);
    /// <summary>A frame's part in the play order as a band colour (D-49): once amber, loop green, end violet.</summary>
    public static readonly IValueConverter RoleBrush = new FuncConverter(v => v switch
    {
        "once" => Frozen(0xFF, 0xE8, 0xA3, 0x3D), "loop" => Frozen(0xFF, 0x45, 0xC0, 0x8A), "end" => Frozen(0xFF, 0xA8, 0x7C, 0xE8), _ => Frozen(0x40, 0x80, 0x80, 0x80),
    });
    public static readonly IValueConverter RoleText = new FuncConverter(v => v switch
    {
        "once" => "Plays once, at the start", "loop" => "Loops", "end" => "Plays once, at the end", _ => "Not played (Advanced: add it to the order)",
    });
    /// <summary>The frame on the canvas gets the accent outline; picked ones keep the list's highlight.</summary>
    public static readonly IValueConverter CurrentFrameBrush = new FuncConverter(v => v is true ? Frozen(0xFF, 0x4C, 0xB0, 0x7A) : System.Windows.Media.Brushes.Transparent);

    private static System.Windows.Media.SolidColorBrush Frozen(byte a, byte r, byte g, byte b)
    {
        var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }
}
