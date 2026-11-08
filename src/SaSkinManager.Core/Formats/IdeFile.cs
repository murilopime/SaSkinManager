namespace SaSkinManager.Core.Formats;

/// <summary>Arquivo .ide do GTA (peds.ide, vehicles.ide…): seções "nome … end" com linhas separadas por vírgula.</summary>
public sealed class IdeFile
{
    public Dictionary<string, List<string[]>> Sections { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string[]> Section(string name) =>
        Sections.TryGetValue(name, out var rows) ? rows : Array.Empty<string[]>();

    public static IdeFile Load(string path) => Parse(File.ReadAllText(path));

    public static IdeFile Parse(string text)
    {
        var ide = new IdeFile();
        List<string[]>? current = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw;
            int hash = line.IndexOf('#');
            if (hash >= 0) line = line[..hash];
            line = line.Trim();
            if (line.Length == 0) continue;

            if (current is null)
            {
                var name = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0];
                if (!ide.Sections.TryGetValue(name, out current))
                    ide.Sections[name] = current = new List<string[]>();
                continue;
            }
            if (line.Equals("end", StringComparison.OrdinalIgnoreCase))
            {
                current = null;
                continue;
            }
            var parts = line.Contains(',')
                ? line.Split(',').Select(p => p.Trim()).ToArray()
                : line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            current.Add(parts);
        }
        return ide;
    }
}
