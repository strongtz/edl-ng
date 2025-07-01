namespace QCEDL.GUI.Helper;

public static class Pipe
{
    private const string NameBase = "qcedl-helper";
    public static string Name { get; }

    static Pipe()
    {
        if (OperatingSystem.IsMacOS())
        {
            Directory.CreateDirectory("/tmp/qcedl");
            Name = Path.Combine("/tmp/qcedl", NameBase);
        }
        else
        {
            Name = NameBase;
        }
    }
}