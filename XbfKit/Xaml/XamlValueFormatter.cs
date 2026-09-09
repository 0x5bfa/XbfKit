using System.Globalization;
using System.Xml;

namespace XbfKit.Xaml;

/// <summary>Formats decoded scalar values and XML-safe local names for XAML output.</summary>
internal static class XamlValueFormatter
{
    /// <summary>Formats a decoded scalar value using invariant XAML syntax.</summary>
    internal static string Format(object? value)
    {
        return value switch
        {
            null => "{x:Null}",
            bool boolean => boolean ? "True" : "False",
            float number => number.ToString("R", CultureInfo.InvariantCulture), // round-trip: 1.5f <-> "1.5"
            double number => number.ToString("R", CultureInfo.InvariantCulture), // round-trip: 1.5d <-> "1.5"
            int number => number.ToString(CultureInfo.InvariantCulture),
            uint number => number.ToString(CultureInfo.InvariantCulture),
            TimeSpan duration => duration.ToString("c", CultureInfo.InvariantCulture), // TimeSpan.FromHours(1.5) <-> "01:30:00" (days.hours:minutes:seconds)
            XamlConditionalValue conditional => Format(conditional.Value),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };
    }

    /// <summary>Returns a valid XML local name, encoding invalid characters when necessary.</summary>
    internal static string SafeName(string name)
    {
        try
        {
            XmlConvert.VerifyNCName(name);
            return name;
        }
        catch (XmlException)
        {
            return XmlConvert.EncodeLocalName(name);
        }
    }
}
