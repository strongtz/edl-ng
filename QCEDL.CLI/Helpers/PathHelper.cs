namespace QCEDL.CLI.Helpers;

internal static class PathHelper
{
    /// <summary>
    /// Expands a leading "~" (current user's home directory) in a path.
    /// The shell only performs tilde expansion when "~" is at the start of a
    /// bare word, so forms like "--loader=~/foo" arrive here unexpanded. This
    /// makes those work regardless of how the argument was passed.
    /// </summary>
    public static string ExpandTilde(string path)
    {
        if (string.IsNullOrEmpty(path) || path[0] != '~')
        {
            return path;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home))
        {
            return path;
        }

        // "~" alone -> home directory.
        if (path.Length == 1)
        {
            return home;
        }

        // "~/rest" or "~\rest" -> home/rest. Leave "~user" forms untouched.
        return path[1] is '/' or '\\'
            ? Path.Combine(home, path[2..])
            : path;
    }
}