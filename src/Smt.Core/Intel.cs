using System.Globalization;
using System.Text.RegularExpressions;

namespace Smt.Core;

public sealed record IntelReport(DateTimeOffset Time, string Speaker, string Message, string[] Systems, bool Clear, string Source)
{
    public override string ToString() => $"{Time:HH:mm:ss} UTC  ·  {string.Join(", ", Systems)}\n{Speaker} › {Message}";
}

public sealed class IntelParser
{
    private readonly Regex names;
    private readonly Universe universe;
    private static readonly Regex Chat = new(@"^\s*\[\s*(?<time>\d{4}\.\d{2}\.\d{2} \d{2}:\d{2}:\d{2})\s*\]\s*(?<speaker>.*?)\s*>\s*(?<message>.*)$", RegexOptions.Compiled, TimeSpan.FromSeconds(1));
    public IntelParser(Universe universe)
    {
        this.universe = universe;
        names = new Regex(@"(?<![\p{L}\p{N}'-])(?:" + string.Join('|', universe.Systems.Keys.OrderByDescending(s => s.Length).Select(Regex.Escape)) + @")(?![\p{L}\p{N}'-])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromSeconds(1));
    }
    public IntelReport? Parse(string line, string source, DateTimeOffset now, bool allowPlainText = false)
    {
        if (line.Length > 8192) return null;
        // EVE can prepend another UTF-16 BOM to each message, not just the file.
        int prefix=0;
        while(prefix<line.Length && (line[prefix]=='\uFEFF' || char.IsWhiteSpace(line[prefix])))prefix++;
        line=line[prefix..];
        var match = Chat.Match(line);
        if (!match.Success && !allowPlainText) return null;
        var message = match.Success ? match.Groups["message"].Value : line;
        var speaker = match.Success ? match.Groups["speaker"].Value : "Manual report";
        if (speaker.Equals("EVE System", StringComparison.OrdinalIgnoreCase)) return null;
        var time = now;
        if (match.Success && !DateTimeOffset.TryParseExact(match.Groups["time"].Value, "yyyy.MM.dd HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out time)) return null;
        var systems = names.Matches(message).Select(m => universe.Systems[m.Value].Name).Distinct().ToArray();
        if (systems.Length == 0) return null;
        var clear = Regex.IsMatch(message, @"\b(clear|clr)\b", RegexOptions.IgnoreCase) &&
            !Regex.IsMatch(message, @"\b(not|no|never|isn't|isnt|hostile|hostiles|neut|neuts|red|reds)\b", RegexOptions.IgnoreCase);
        return new(time, speaker, message, systems, clear, source);
    }
}
