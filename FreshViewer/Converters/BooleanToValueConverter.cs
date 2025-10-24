using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Data;

namespace FreshViewer.Converters;

/// <summary>
/// Converts a boolean into one of two configured values.
/// </summary>
public sealed class BooleanToValueConverter : IValueConverter
{
    /// <summary>
    /// Gets or sets the value produced when the source boolean is <c>true</c>.
    /// </summary>
    public object? TrueValue { get; set; }
    /// <summary>
    /// Gets or sets the value produced when the source boolean is <c>false</c>.
    /// </summary>
    public object? FalseValue { get; set; }

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool flag)
        {
            return flag ? TrueValue : FalseValue;
        }

        return FalseValue;
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (TrueValue is not null && Equals(value, TrueValue))
        {
            return true;
        }

        if (FalseValue is not null && Equals(value, FalseValue))
        {
            return false;
        }

        return BindingOperations.DoNothing;
    }
}
