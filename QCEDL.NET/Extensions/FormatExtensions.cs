using System.Globalization;

namespace QCEDL.NET.Extensions;

public static class FormatExtensions
{
    public static string ToStringInvariantCulture<T>(this T value, string format) where T : IFormattable => value.ToString(format, CultureInfo.InvariantCulture);

    public static string ToStringInvariantCulture<T>(this T value) where T : IFormattable => value.ToString(null, CultureInfo.InvariantCulture);
}