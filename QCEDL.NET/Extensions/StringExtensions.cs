namespace QCEDL.NET.Extensions;

public static class StringExtensions
{
    public static bool StartsWithOrdinal(this string str, string value) => str.StartsWith(value, StringComparison.Ordinal);

    public static bool StartsWithOrdinalIgnoreCase(this string str, string value) => str.StartsWith(value, StringComparison.OrdinalIgnoreCase);
}