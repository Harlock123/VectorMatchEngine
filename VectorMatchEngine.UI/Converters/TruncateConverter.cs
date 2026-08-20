using System.Globalization;
using Avalonia.Data.Converters;

namespace VectorMatchEngine.UI.Converters;

/// <summary>Truncates long text to a maximum length (default 60) with an ellipsis.</summary>
public class TruncateConverter : IValueConverter
{
    public static readonly TruncateConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value?.ToString();
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        int maxLength = 60;
        if (parameter is string raw && int.TryParse(raw, out var parsed) && parsed > 0)
            maxLength = parsed;

        return text.Length <= maxLength ? text : text[..(maxLength - 1)] + "…";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
