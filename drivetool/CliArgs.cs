namespace DriveTool
{
    /// <summary>Thrown for bad command-line input; Program maps it to exit code 2.</summary>
    public sealed class UsageException(string message) : Exception(message)
    {
    }

    /// <summary>
    /// Minimal parser: positionals, "--name value" / "--name=value" options and boolean "--flag"s.
    /// Arguments starting with '@' are expanded to the non-empty, non-comment lines of that file.
    /// </summary>
    public sealed class CliArgs
    {
        private static readonly HashSet<string> _flags = ["yes", "allow-non-backup", "help"];

        private readonly Dictionary<string, string> _options = new(StringComparer.Ordinal);
        private readonly HashSet<string> _setFlags = new(StringComparer.Ordinal);

        public List<string> Positionals { get; } = [];

        public static CliArgs Parse(IReadOnlyList<string> args)
        {
            CliArgs t_result = new();
            for (int t_i = 0; t_i < args.Count; t_i++)
            {
                string t_arg = args[t_i];
                if (t_arg.StartsWith("--", StringComparison.Ordinal))
                {
                    string t_name = t_arg[2..];
                    int t_eq = t_name.IndexOf('=');
                    if (t_eq >= 0)
                    {
                        t_result._options[t_name[..t_eq]] = t_name[(t_eq + 1)..];
                    }
                    else if (_flags.Contains(t_name))
                    {
                        t_result._setFlags.Add(t_name);
                    }
                    else if (t_i + 1 < args.Count)
                    {
                        t_result._options[t_name] = args[++t_i];
                    }
                    else
                    {
                        throw new UsageException($"Option --{t_name} needs a value.");
                    }
                }
                else if (t_arg.StartsWith('@') && t_arg.Length > 1)
                {
                    t_result.Positionals.AddRange(ReadListFile(t_arg[1..]));
                }
                else
                {
                    t_result.Positionals.Add(t_arg);
                }
            }
            return t_result;
        }

        public bool Has(string flag) => _setFlags.Contains(flag);

        public string? Get(string name) => _options.TryGetValue(name, out string? t_value) ? t_value : null;

        public int GetInt(string name, int defaultValue)
        {
            string? t_value = Get(name);
            if (t_value == null)
            {
                return defaultValue;
            }
            return int.TryParse(t_value, out int t_parsed)
                ? t_parsed
                : throw new UsageException($"--{name} must be an integer, got '{t_value}'.");
        }

        /// <summary>Drive IP from --ip, else the CTRLX_DRIVE_IP environment variable.</summary>
        public string Ip =>
            Get("ip")
            ?? Environment.GetEnvironmentVariable("CTRLX_DRIVE_IP")
            ?? throw new UsageException("No drive address: pass --ip <address> or set CTRLX_DRIVE_IP.");

        public string Positional(int index, string what) =>
            index < Positionals.Count ? Positionals[index] : throw new UsageException($"Missing {what}.");

        private static IEnumerable<string> ReadListFile(string path)
        {
            if (!File.Exists(path))
            {
                throw new UsageException($"List file '{path}' not found.");
            }
            return File.ReadAllLines(path)
                .Select(t_line => t_line.Trim())
                .Where(t_line => t_line.Length > 0 && !t_line.StartsWith('#'));
        }
    }
}
