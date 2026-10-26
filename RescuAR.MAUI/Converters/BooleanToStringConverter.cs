using System;
using System.Globalization;
using Microsoft.Maui.Controls;

namespace RescuAR.MAUI.Converters
{
    public class BooleanToStringConverter : IValueConverter
    {
        public string TrueText { get; set; } = "Granted";
        public string FalseText { get; set; } = "Denied";

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (parameter is string customParams && customParams.Contains(":"))
            {
                var parts = customParams.Split(':');
                if (parts.Length == 2)
                {
                    if (value is bool b1)
                        return b1 ? parts[0] : parts[1];
                }
            }

            if (value is bool b)
            {
                return b ? TrueText : FalseText;
            }

            return FalseText;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string str)
            {
                return string.Equals(str, TrueText, StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }
    }
}
