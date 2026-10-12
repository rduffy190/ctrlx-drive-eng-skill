using System.Text.RegularExpressions;

namespace DriveTool
{
    /// <summary>SERCOS IDN helpers. Canonical form is the one EAL writes to .par files: "S-0-0040.0.0".</summary>
    public static partial class Idn
    {
        [GeneratedRegex(@"^([A-Z])-([0-7])-(\d{1,4})(?:\.(\d{1,3})\.(\d{1,3}))?$", RegexOptions.IgnoreCase)]
        private static partial Regex IdnRegex();

        public static bool TryNormalize(string text, out string normalized)
        {
            Match t_match = IdnRegex().Match(text.Trim());
            if (!t_match.Success)
            {
                normalized = "";
                return false;
            }
            int t_number = int.Parse(t_match.Groups[3].Value);
            int t_si = t_match.Groups[4].Success ? int.Parse(t_match.Groups[4].Value) : 0;
            int t_se = t_match.Groups[5].Success ? int.Parse(t_match.Groups[5].Value) : 0;
            // The parameter number is 12 bits; EAL silently wraps larger numbers into another IDN
            // (S-0-9999 is sent as S-2-1807), so reject them here.
            if (t_number > 4095 || t_si > 255 || t_se > 255)
            {
                normalized = "";
                return false;
            }
            normalized = $"{t_match.Groups[1].Value.ToUpperInvariant()}-{t_match.Groups[2].Value}-{t_number:D4}.{t_si}.{t_se}";
            return true;
        }

        public static string Normalize(string text) =>
            TryNormalize(text, out string t_normalized)
                ? t_normalized
                : throw new UsageException($"'{text}' is not a parameter IDN (expected e.g. S-0-0040 or P-0-4089.0.1).");

        public static bool IsIdn(string text) => TryNormalize(text, out _);
    }
}
