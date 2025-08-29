using System.Globalization;
using System.Text.RegularExpressions;

namespace QCEDL.CLI.Helpers;

internal static partial class ImageSizeParser
{
    [GeneratedRegex(@"^(\d+(?:\.\d+)?)\s*([KMGT]?B?)$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SizeRegex();

    public static bool TryParseSize(string sizeStr, out ulong bytes)
    {
        bytes = 0;

        if (string.IsNullOrWhiteSpace(sizeStr))
        {
            return false;
        }

        var match = SizeRegex().Match(sizeStr.Trim());
        if (!match.Success)
        {
            return false;
        }

        if (!double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        var unit = match.Groups[2].Value.ToUpperInvariant();
        var multiplier = unit switch
        {
            "" or "B" => 1UL,
            "K" or "KB" => 1024UL,
            "M" or "MB" => 1024UL * 1024UL,
            "G" or "GB" => 1024UL * 1024UL * 1024UL,
            "T" or "TB" => 1024UL * 1024UL * 1024UL * 1024UL,
            _ => 0UL
        };

        if (multiplier == 0)
        {
            return false;
        }

        try
        {
            bytes = (ulong)(value * multiplier);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}
