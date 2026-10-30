using System.Globalization;
using System.Text.RegularExpressions;

namespace NewsletterCombiner.Core;

public enum ConversionMode { Newsletters, Bulk, Combine, Arrange }

public static class DocumentPlan
{
    public static IReadOnlyList<OutputPlan> CreateBulk(IEnumerable<string> files, IEnumerable<string> append,
        string ending, DateTime timestamp)
    {
        var (inputs, extras) = Validate(files, append);
        var names = inputs.Select((path, i) => BulkFileName(path, ending, timestamp, i + 1)).ToArray();
        // Reserve all requested names so a duplicate cannot take a later source's natural filename.
        var reserved = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<OutputPlan>();
        for (var i = 0; i < inputs.Length; i++)
        {
            var name = names[i];
            if (!used.Add(name))
            {
                var stem = Path.GetFileNameWithoutExtension(name);
                var n = 2;
                do { name = ValidateFileName($"{stem} ({n++})"); }
                while (reserved.Contains(name) || !used.Add(name));
            }
            result.Add(new(name, new[] { inputs[i] }.Concat(extras).ToArray()));
        }
        return result;
    }

    public static IReadOnlyList<OutputPlan> CreateCombined(IEnumerable<string> files, IEnumerable<string> append,
        string name, DateTime timestamp)
    {
        var (inputs, extras) = Validate(files, append);
        return [new(CombinedFileName(name, timestamp), inputs.Concat(extras).ToArray())];
    }

    public static string BulkFileName(string source, string ending, DateTime timestamp, int sequence) =>
        ValidateFileName(Path.GetFileNameWithoutExtension(source) + Expand(ending, timestamp, sequence));

    public static string CombinedFileName(string name, DateTime timestamp)
    {
        var expanded = Expand(name, timestamp, 1).Trim();
        if (expanded.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) expanded = expanded[..^4];
        return ValidateFileName(expanded);
    }

    static string Expand(string value, DateTime timestamp, int sequence)
    {
        var expanded = Regex.Replace(value, @"\{([^{}]+)\}", match => match.Groups[1].Value.ToLowerInvariant() switch
        {
            "date" => timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            "time" => timestamp.ToString("HH-mm", CultureInfo.InvariantCulture),
            "n" => sequence.ToString("D3", CultureInfo.InvariantCulture),
            _ => throw new ArgumentException("Use {date}, {time} or {n} for filename variables.")
        });
        if (expanded.Contains('{') || expanded.Contains('}'))
            throw new ArgumentException("Use complete filename variables: {date}, {time} or {n}.");
        return expanded;
    }

    internal static string ValidateFileName(string stem)
    {
        stem = stem.Trim();
        if (stem.Length == 0 || stem.All(c => c == '.')) throw new ArgumentException("Enter a filename.");
        if (stem.Length > 200) throw new ArgumentException("Keep output filenames to 200 characters or fewer, excluding .pdf.");
        if (stem.Any(c => c < 32 || "<>:\"/\\|?*".Contains(c)) || stem.EndsWith('.'))
            throw new ArgumentException("The filename contains characters Windows cannot use.");
        var firstPart = stem.Split('.')[0].TrimEnd();
        if (Regex.IsMatch(firstPart, @"^(CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new ArgumentException("That filename is reserved by Windows. Choose another name.");
        return stem + ".pdf";
    }

    static (string[] Inputs, string[] Extras) Validate(IEnumerable<string> files, IEnumerable<string> append)
    {
        var inputs = files.Select(Path.GetFullPath).ToArray();
        var extras = append.Select(Path.GetFullPath).ToArray();
        if (inputs.Length == 0) throw new ArgumentException("Add at least one file.");
        var paths = inputs.Concat(extras).ToArray();
        if (paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length)
            throw new ArgumentException("A document appears more than once. Remove the duplicate before continuing.");
        foreach (var path in paths)
        {
            if (!NewsletterPlan.Extensions.Contains(Path.GetExtension(path))) throw new ArgumentException($"Unsupported file: {Path.GetFileName(path)}");
            if (!File.Exists(path)) throw new FileNotFoundException($"File not found: {path}", path);
        }
        return (inputs, extras);
    }
}
