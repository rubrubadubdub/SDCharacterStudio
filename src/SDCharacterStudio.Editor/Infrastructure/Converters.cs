using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Infrastructure;

/// <summary>Colours that carry meaning (beat categories, badges, the Bridge light). Tuned for the dark theme.</summary>
public static class StudioColors
{
    public static readonly Color Dialogue = Color.FromRgb(0x5B, 0x9B, 0xF0);
    public static readonly Color Movement = Color.FromRgb(0x45, 0xC0, 0x8A);
    public static readonly Color Acting = Color.FromRgb(0xF0, 0xA0, 0x48);
    public static readonly Color Staging = Color.FromRgb(0xB4, 0x8C, 0xF2);
    public static readonly Color Rewards = Color.FromRgb(0xEF, 0xCB, 0x4A);
    public static readonly Color Flow = Color.FromRgb(0x8F, 0x9B, 0xAA);
    public static readonly Color Logic = Color.FromRgb(0x3F, 0xC1, 0xC9);
    public static readonly Color Good = Color.FromRgb(0x45, 0xC0, 0x8A);
    public static readonly Color Caution = Color.FromRgb(0xEF, 0xCB, 0x4A);
    public static readonly Color Bad = Color.FromRgb(0xE0, 0x5D, 0x5D);
    public static readonly Color Info = Color.FromRgb(0x5B, 0x9B, 0xF0);
    public static readonly Color ModBadge = Color.FromRgb(0xE8, 0xA3, 0x3D);

    public static Color For(BeatCategory category) => category switch
    {
        BeatCategory.Dialogue => Dialogue,
        BeatCategory.Movement => Movement,
        BeatCategory.Acting => Acting,
        BeatCategory.Staging => Staging,
        BeatCategory.Rewards => Rewards,
        BeatCategory.Logic => Logic,
        _ => Flow
    };

    private static readonly Dictionary<Color, SolidColorBrush> brushes = new();
    public static SolidColorBrush Brush(Color color)
    {
        if (!brushes.TryGetValue(color, out var brush)) { brush = new SolidColorBrush(color); brush.Freeze(); brushes[color] = brush; }
        return brush;
    }
}

public abstract class OneWayConverter : IValueConverter
{
    public abstract object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture);
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Visible when the value is "truthy": true, non-null, non-empty string or collection, non-zero number. Parameter "not" inverts.</summary>
public sealed class VisibleWhenConverter : OneWayConverter
{
    public static bool Truthy(object? value) => value switch
    {
        null => false,
        bool b => b,
        string s => s.Length > 0,
        int i => i != 0,
        double d => d != 0,
        ICollection c => c.Count > 0,
        _ => true
    };
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Truthy(value) != (parameter as string == "not") ? Visibility.Visible : Visibility.Collapsed;
}

public sealed class TruthyConverter : OneWayConverter
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => VisibleWhenConverter.Truthy(value);
}

/// <summary>Wraps a lambda as a one-way converter (for x:Static converters next to the views that use them).</summary>
public sealed class FuncConverter : OneWayConverter
{
    private readonly Func<object?, object?> convert;
    public FuncConverter(Func<object?, object?> convert) => this.convert = convert;
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => convert(value);
}

public sealed class NotConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => !VisibleWhenConverter.Truthy(value);
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is bool b ? !b : Binding.DoNothing;
}

public sealed class CategoryBrushConverter : OneWayConverter
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var brush = StudioColors.Brush(value is BeatCategory c ? StudioColors.For(c) : StudioColors.Flow);
        if (parameter is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double opacity))
        {
            var faded = new SolidColorBrush(brush.Color) { Opacity = opacity }; faded.Freeze(); return faded;
        }
        return brush;
    }
}

public sealed class BadgeBrushConverter : OneWayConverter
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => StudioColors.Brush(value switch
    {
        BadgeKind.Provided => StudioColors.ModBadge,
        BadgeKind.Unknown => StudioColors.Bad,
        _ => StudioColors.Flow
    });
}

public sealed class BridgeBrushConverter : OneWayConverter
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => StudioColors.Brush(value switch
    {
        Preview.BridgeState.Ready => StudioColors.Good,
        Preview.BridgeState.NoSave => StudioColors.Caution,
        Preview.BridgeState.Busy => StudioColors.Info,
        _ => StudioColors.Bad
    });
}

/// <summary>Error/warning colour from an "is error" flag.</summary>
public sealed class SeverityBrushConverter : OneWayConverter
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => StudioColors.Brush(value is true ? StudioColors.Bad : StudioColors.Caution);
}

/// <summary>Keeps lines and labels a constant on-screen size inside the zoomed stage: parameter ÷ zoom.</summary>
public sealed class InverseScaleConverter : OneWayConverter
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double zoom = value is double z && z > 0 ? z : 1;
        double size = parameter is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double p) ? p : 1;
        return targetType == typeof(Thickness) ? new Thickness(size / zoom) : size / zoom;
    }
}

public sealed class PercentConverter : OneWayConverter
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is double d ? $"{d * 100:0}%" : "";
}

/// <summary>Equality with the parameter (for radio-like toggles on enums or strings).</summary>
public sealed class EqualsConverter : OneWayConverter
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);
}

/// <summary>A flipped image for left-facing farmer frames.</summary>
public sealed class FlipConverter : OneWayConverter
{
    // Used with RenderTransformOrigin="0.5,0.5".
    private static readonly ScaleTransform Mirror = Freeze(new ScaleTransform(-1, 1));
    private static ScaleTransform Freeze(ScaleTransform t) { t.Freeze(); return t; }
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? Mirror : Transform.Identity;
}

/// <summary>Shows a list's "empty" text only when it has no rows and there is text to show.</summary>
public sealed class ListFieldEmpty : IMultiValueConverter
{
    public static readonly ListFieldEmpty Instance = new();
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
        => values.Length == 2 && values[0] is 0 && values[1] is string { Length: > 0 } ? Visibility.Visible : Visibility.Collapsed;
    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public static class ExportIcons
{
    public static readonly IValueConverter Status = new FuncConverter(v => v is true ? Wpf.Ui.Controls.SymbolRegular.ErrorCircle24 : Wpf.Ui.Controls.SymbolRegular.CheckmarkCircle24);
    public static readonly IValueConverter StatusBrush = new FuncConverter(v => StudioColors.Brush(v is true ? StudioColors.Bad : StudioColors.Good));
}
