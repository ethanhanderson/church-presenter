using System.Reflection;

namespace ChurchPresenter;

/// <summary>
/// Runtime dependency lines for the About expander (loaded assemblies only).
/// </summary>
internal static class AboutExpandableDetails
{
    private static readonly Lazy<IReadOnlyList<string>> _dependencyLines = new(BuildDependencyLines);

    public static IReadOnlyList<string> DependencyLines => _dependencyLines.Value;

    public static bool HasExpandableDetails => DependencyLines.Count > 0;

    private static IReadOnlyList<string> BuildDependencyLines()
    {
        var lines = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string[] preferredOrder =
        [
            "Microsoft.WinUI",
            "Microsoft.UI.Xaml",
            "Microsoft.WindowsAppRuntime",
            "CommunityToolkit.Mvvm",
            "WinRT.Runtime",
        ];

        foreach (string simpleName in preferredOrder)
        {
            Assembly? a = TryGetLoadedAssembly(simpleName);
            if (a is null)
                continue;

            AssemblyName name = a.GetName();
            string? simple = name.Name;
            if (string.IsNullOrEmpty(simple) || !seen.Add(simple))
                continue;

            string? ver = FormatAssemblyVersion(name.Version);
            if (string.IsNullOrEmpty(ver))
                continue;

            lines.Add($"{simple} {ver}");
        }

        return lines;
    }

    private static Assembly? TryGetLoadedAssembly(string simpleName)
    {
        foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                if (string.Equals(a.GetName().Name, simpleName, StringComparison.Ordinal))
                    return a;
            }
            catch
            {
                // Dynamic assemblies may throw on GetName().
            }
        }

        return null;
    }

    private static string? FormatAssemblyVersion(Version? v)
    {
        if (v is null)
            return null;

        if (v.Major == 0 && v.Minor == 0 && v.Build == 0 && v.Revision == 0)
            return null;

        return v.ToString();
    }
}