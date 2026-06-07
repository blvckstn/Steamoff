using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Steamoff.Core.Enums;
using Steamoff.Core.Models;
using Application = System.Windows.Application;
using Binding = System.Windows.Data.Binding;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace Steamoff.App.Converters;

/// <summary>Binds a RadioButton.IsChecked to one specific enum value, given the value as ConverterParameter (e.g. "AlwaysBlock").</summary>
public sealed class EnumToBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null || parameter is null)
        {
            return false;
        }

        return string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal);
    }

    public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is true && parameter is string text && targetType.IsEnum)
        {
            return Enum.Parse(targetType, text);
        }

        return Binding.DoNothing;
    }
}

/// <summary>Maps a HealthLevel to one of the theme's status brushes.</summary>
public sealed class HealthLevelToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value switch
        {
            HealthLevel.Ok => "GreenStatusBrush",
            HealthLevel.Warning => "WarningBrush",
            HealthLevel.Error => "RedStatusBrush",
            HealthLevel.Disabled => "GrayStatusBrush",
            HealthLevel.ReadOnly => "TextSecondaryBrush",
            true => "GreenStatusBrush",
            false => "RedStatusBrush",
            _ => "GrayStatusBrush"
        };

        return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Maps a Steam-path validation status to the indicator brush (spec section 4 color contract: green=valid, red=invalid, yellow=unchecked, gray=empty).</summary>
public sealed class PathCheckStatusToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value switch
        {
            PathCheckStatus.Valid => "GreenStatusBrush",
            PathCheckStatus.Unchecked => "WarningBrush",
            PathCheckStatus.Empty => "GrayStatusBrush",
            PathCheckStatus.PathNotFound
                or PathCheckStatus.SteamExeNotFound
                or PathCheckStatus.WrongExe
                or PathCheckStatus.ShortcutUnresolved => "RedStatusBrush",
            _ => "GrayStatusBrush"
        };

        return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Maps a TestOutcome to one of the theme's status brushes.</summary>
public sealed class TestOutcomeToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value switch
        {
            TestOutcome.Ok => "GreenStatusBrush",
            TestOutcome.Warning => "WarningBrush",
            TestOutcome.Error => "RedStatusBrush",
            _ => "GrayStatusBrush"
        };

        return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Checks whether a log line (e.g. "{timestamp} [{level}] {message}") contains the substring given as ConverterParameter — drives mini-log color-coding by level.</summary>
public sealed class LogLineContainsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is string line && parameter is string needle && line.Contains(needle, StringComparison.Ordinal);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>True/false (or any non-null/non-empty value) to Visibility, with optional "Invert" parameter.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var flag = value switch
        {
            bool b => b,
            null => false,
            string s => !string.IsNullOrEmpty(s),
            int i => i != 0,
            _ => true
        };

        if (string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase))
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Compares two bound <see cref="AppLanguage"/> values (the card's language and the
/// currently-selected language) — used as a MultiBinding to highlight the active
/// language card with the orange accent without requiring the model to implement
/// INotifyPropertyChanged.
/// </summary>
public sealed class LanguageEqualityConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values is [AppLanguage a, AppLanguage b])
        {
            return string.Equals(a.Code, b.Code, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
