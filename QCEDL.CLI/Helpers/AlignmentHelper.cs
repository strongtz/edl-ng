namespace QCEDL.CLI.Helpers;

internal static class AlignmentHelper
{
    public static ulong AlignTo(ulong value, uint alignment)
    {
        if (alignment == 0)
        {
            return value;
        }

        var remainder = value % alignment;
        return remainder == 0 ? value : value + alignment - remainder;
    }
}