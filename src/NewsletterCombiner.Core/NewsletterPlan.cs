using System.Text.RegularExpressions;

namespace NewsletterCombiner.Core;

public enum NewsletterFrequency { Fortnightly, Weekly, Custom }
public record NewsletterOptions(NewsletterFrequency Frequency = NewsletterFrequency.Fortnightly,
    bool CustomGroups = false, string Issue = "");

public record Newsletter(string Path, string Year);
public record OutputPlan(string FileName, IReadOnlyList<string> Sources);

public static partial class NewsletterPlan
{
    public static readonly string[] Years = ["Prep", .. Enumerable.Range(1, 12).Select(n => n.ToString())];
    public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".pdf", ".doc", ".docx", ".rtf", ".ppt", ".pptx", ".jpg", ".jpeg", ".png", ".bmp" };

    public static string OutputFileName(string year, int term, int firstWeek, int lastWeek, NewsletterOptions? options = null)
    {
        options ??= new();
        var label = options.CustomGroups ? NormalizeGroup(year) : year.Trim().Equals("Prep", StringComparison.OrdinalIgnoreCase) ? "Prep" : $"Year {year}";
        string issue;
        if (options.Frequency == NewsletterFrequency.Custom)
        {
            issue = NormalizeGroup(options.Issue);
        }
        else
        {
            if (term is < 1 or > 4) throw new ArgumentException("Choose a term from 1 to 4.");
            if (firstWeek is < 1 or > 12 || (options.Frequency == NewsletterFrequency.Fortnightly && (lastWeek < firstWeek || lastWeek > 12)))
                throw new ArgumentException("Choose weeks from 1 to 12, with the end week at or after the start.");
            issue = options.Frequency == NewsletterFrequency.Weekly ? $"Term {term} W{firstWeek}" : $"Term {term} W{firstWeek}-{lastWeek}";
        }
        return DocumentPlan.ValidateFileName($"{label} Newsletter {issue}");
    }

    public static string NormalizeGroup(string value)
    {
        value = Regex.Replace(value.Trim(), @"\s+", " ");
        if (value.Length == 0) throw new ArgumentException("Enter a group name or issue label.");
        DocumentPlan.ValidateFileName(value);
        return value;
    }

    public static string[] ParseGroups(string text) => text.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries)
        .Where(s => !string.IsNullOrWhiteSpace(s)).Select(NormalizeGroup).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public static string GuessGroup(string path, IEnumerable<string> groups)
    {
        var candidates = groups.Select(NormalizeGroup).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        static string Readable(string text) => Regex.Replace(text.Replace('_', ' ').Replace('-', ' '), @"\s+", " ");
        foreach (var part in PathParts(path))
        {
            var text = Readable(part);
            var matches = candidates.Where(g => Regex.IsMatch(text, @"(?<![\p{L}\p{N}])" + Regex.Escape(Readable(g)) + @"(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).ToArray();
            // Prefer a specific name such as "Sea Scouts" over its contained "Scouts" match.
            matches = matches.Where(g => !matches.Any(other => other.Length > g.Length && Regex.IsMatch(Readable(other), @"(?<![\p{L}\p{N}])" + Regex.Escape(Readable(g)) + @"(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))).ToArray();
            if (matches.Length == 1) return matches[0];
            if (matches.Length > 1) return ""; // Ambiguous filenames need a person to choose.
        }
        return "";
    }

    static IEnumerable<string> PathParts(string path)
    {
        yield return System.IO.Path.GetFileNameWithoutExtension(path);
        var parent = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
        for (var i = 0; i < 3 && parent != null; i++, parent = System.IO.Path.GetDirectoryName(parent))
            yield return System.IO.Path.GetFileName(parent);
    }

    public static string NormalizeYear(string value)
    {
        value = Regex.Replace(value.Trim(), @"^(year|yr)\s*", "", RegexOptions.IgnoreCase);
        if (value.Equals("prep", StringComparison.OrdinalIgnoreCase)) return "Prep";
        if (int.TryParse(value, out var n) && n is >= 1 and <= 12) return n.ToString();
        if (Regex.IsMatch(value, @"^(?:[1-9]|1[0-2])\s*[-/]\s*(?:[1-9]|1[0-2])$"))
            return Regex.Replace(value.Replace('/', '-'), @"\s", "");
        throw new ArgumentException("Choose a year level for every newsletter (Prep, 1–12, or a group such as 5-6).");
    }

    public static string GuessYear(string path)
    {
        // Prefer a file's explicit year over its containing folder; never infer a year from an arbitrary digit.
        foreach (var part in PathParts(path))
        {
            var readable = part.Replace('_', ' ');
            if (Regex.IsMatch(readable, @"\bprep\b", RegexOptions.IgnoreCase)) return "Prep";
            var match = Regex.Match(readable, @"\b(?:year|yr|y)\s*(1[0-2]|[1-9])(?:\s*[-/]\s*(1[0-2]|[1-9]))?(?!\d)", RegexOptions.IgnoreCase);
            if (match.Success) return match.Groups[1].Value + (match.Groups[2].Success ? "-" + match.Groups[2].Value : "");
        }
        return "";
    }

    public static IReadOnlyList<OutputPlan> Create(IEnumerable<Newsletter> newsletters, IEnumerable<string> notices,
        int term, int firstWeek, int lastWeek, NewsletterOptions? options = null)
    {
        options ??= new();
        // Validate naming even when no input has been supplied yet.
        OutputFileName(options.CustomGroups ? "Group" : "1", term, firstWeek, lastWeek, options);
        var items = newsletters.Select(n => new Newsletter(System.IO.Path.GetFullPath(n.Path), options.CustomGroups ? NormalizeGroup(n.Year) : NormalizeYear(n.Year))).ToArray();
        if (items.Length == 0) throw new ArgumentException("Add at least one newsletter.");
        var extras = notices.Select(System.IO.Path.GetFullPath).ToArray();
        var paths = items.Select(n => n.Path).Concat(extras).ToArray();
        if (paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length)
            throw new ArgumentException("A document appears more than once. Remove the duplicate before continuing.");
        foreach (var path in paths)
        {
            if (!Extensions.Contains(System.IO.Path.GetExtension(path))) throw new ArgumentException($"Unsupported file: {System.IO.Path.GetFileName(path)}");
            if (!File.Exists(path)) throw new FileNotFoundException($"File not found: {path}", path);
        }
        return items.GroupBy(n => n.Year, StringComparer.OrdinalIgnoreCase)
            .Select(g => new OutputPlan(OutputFileName(g.Key, term, firstWeek, lastWeek, options),
                g.Select(n => n.Path).Concat(extras).ToArray())).ToArray();
    }
}
