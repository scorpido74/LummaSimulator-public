using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;

namespace LummaSimulator.App;

// Presentation only. Never rewrite state identifiers, persisted evidence or commands.
public sealed class DesktopLanguage : INotifyPropertyChanged
{
    public static DesktopLanguage Current { get; } = new();
    private readonly ResourceManager _resources = new("LummaSimulator.App.DesktopText", typeof(DesktopLanguage).Assembly);
    private string _language = "nl-NL";
    private readonly Dictionary<string, string> _keys = new(StringComparer.Ordinal);
    private readonly List<(Regex Pattern, string Key, int Count)> _patterns = [];
    public string Language => _language;
    public bool IsDutch => _language == "nl-NL";
    public bool IsEnglish => !IsDutch;
    public string this[string key] => _resources.GetString(key, CultureInfo.GetCultureInfo(_language)) ?? key;
    public event PropertyChangedEventHandler? PropertyChanged;

    private DesktopLanguage()
    {
        _language = Services.DesktopSettings.Load().DefaultLanguage == "en-GB" ? "en-GB" : "nl-NL";
        foreach (var language in new[] { "nl-NL", "en-GB" })
        foreach (System.Collections.DictionaryEntry entry in _resources.GetResourceSet(CultureInfo.GetCultureInfo(language), true, true)!)
        {
            var key = (string)entry.Key; var text = (string)entry.Value!;
            _keys[text] = key;
            if (!key.StartsWith("pattern.", StringComparison.Ordinal)) continue;
            var parts = Regex.Split(text, @"(\{\d+\})"); var count = 0;
            var expression = string.Concat(parts.Select(part => {
                if (Regex.IsMatch(part, @"\A\{\d+\}\z")) {
                    var index = int.Parse(part[1..^1], CultureInfo.InvariantCulture); count = Math.Max(count, index + 1);
                    return "(?<p" + index + ">.*?)";
                }
                return Regex.Escape(part);
            }));
            _patterns.Add((new Regex("\\A" + expression + "\\z", RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromMilliseconds(50)), key, count));
        }
        if (_keys.TryGetValue("SIMULATION · fictional Lumma Stealer installation", out var installationKey))
            _keys["SIMULATION · fictieve Lumma Stealer-installatie"] = installationKey;
        _patterns.Sort((a, b) => b.Pattern.ToString().Length.CompareTo(a.Pattern.ToString().Length));
    }
    public void Select(string language)
    {
        if (language is not ("nl-NL" or "en-GB") || _language == language) return;
        _language = language;
        foreach (var property in new[] { nameof(Language), nameof(IsDutch), nameof(IsEnglish), "Item[]" })
            PropertyChanged?.Invoke(this, new(property));
    }
    public string Translate(string text)
    {
        if (_keys.TryGetValue(text, out var key)) return this[key];
        if (text.Contains('\n')) return string.Join("\n", text.Split('\n').Select(Translate));
        foreach (var (pattern, resource, count) in _patterns)
        {
            Match match;
            try { match = pattern.Match(text); } catch (RegexMatchTimeoutException) { continue; }
            if (!match.Success) continue;
            var args = Enumerable.Range(0, count).Select(i => {
                var value = match.Groups["p" + i].Value;
                return (object)(_keys.TryGetValue(value, out var valueKey) ? this[valueKey] : value);
            }).ToArray();
            return string.Format(CultureInfo.InvariantCulture, this[resource], args);
        }
        // Technical diagnostics and product/ATT&CK identifiers retain their original evidence text.
        return text;
    }
}
