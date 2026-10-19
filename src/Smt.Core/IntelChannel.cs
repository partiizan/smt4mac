using System.Text.RegularExpressions;
namespace Smt.Core;
public static class IntelChannel
{
    public static string Name(string source) => Regex.Replace(source,@"_\d{8}_\d{6}(?:_.*)?$", "");
    public static bool Selected(string source,IEnumerable<string> channels) => channels.Contains(Name(source),StringComparer.OrdinalIgnoreCase);
    public static bool SameReport(IntelReport a,IntelReport b) => a.Time==b.Time && a.Speaker==b.Speaker && a.Message==b.Message && Name(a.Source).Equals(Name(b.Source),StringComparison.OrdinalIgnoreCase);
}
