using System.Text;

namespace DriveTool.Par
{
    /// <summary>
    /// SERCOS-ASCII parameter file as produced by IParameter.SaveParameters and consumed by LoadParameters.
    /// Header lines up to the first "|": "SERCOS-ASCII", timestamp, axis name, type plate
    /// ("firmware | drive | motor | ..."), parameter-set count. Then blocks separated by "|" lines (none after the last).
    /// The format isn't in the EAL docs; it was taken from files the drive produced.
    /// </summary>
    public sealed class ParFile
    {
        public const string Magic = "SERCOS-ASCII";
        public const string Separator = "|";

        private static readonly Encoding _encoding = new UTF8Encoding(false);

        public List<string> Header { get; set; } = [];
        public List<ParBlock> Blocks { get; set; } = [];

        /// <summary>
        /// IDNs the drive returned as empty stubs (no name, attribute 0, no data): not supported by this drive.
        /// Kept out of <see cref="Blocks"/> so they're never written back.
        /// </summary>
        public List<string> Unsupported { get; set; } = [];

        /// <summary>Type plate header line: "firmware | drive | motor | ...".</summary>
        public string? TypePlate => Header.Count > 3 ? Header[3] : null;

        public static ParFile Parse(string text)
        {
            string t_text = text.Replace("\r\n", "\n");
            // The final '\n' terminates the last line; it doesn't start an empty one.
            string[] t_lines = (t_text.EndsWith('\n') ? t_text[..^1] : t_text).Split('\n');
            ParFile t_file = new();
            int t_i = 0;
            while (t_i < t_lines.Length && t_lines[t_i] != Separator)
            {
                t_file.Header.Add(t_lines[t_i]);
                t_i++;
            }
            if (t_file.Header.Count == 0 || t_file.Header[0] != Magic)
            {
                throw new FormatException($"Not a {Magic} parameter file (first line '{t_file.Header.FirstOrDefault()}').");
            }
            t_i++;

            List<string> t_current = [];
            int t_blockStart = t_i + 1;
            for (; t_i < t_lines.Length; t_i++)
            {
                if (t_lines[t_i] == Separator)
                {
                    t_file.AddBlock(t_current, t_blockStart);
                    t_current = [];
                    t_blockStart = t_i + 2;
                }
                else
                {
                    t_current.Add(t_lines[t_i]);
                }
            }
            if (t_current.Any(t_line => t_line.Length > 0))
            {
                t_file.AddBlock(t_current, t_blockStart);
            }
            return t_file;
        }

        public static ParFile Load(string path) => Parse(File.ReadAllText(path, _encoding));

        public static ParFile FromBytes(byte[] bytes) => Parse(_encoding.GetString(bytes));

        public string ToText()
        {
            StringBuilder t_builder = new();
            foreach (string t_line in Header)
            {
                t_builder.Append(t_line).Append('\n');
            }
            // "|" follows the header and separates blocks; the drive writes none after the last block.
            for (int t_i = 0; t_i < Blocks.Count; t_i++)
            {
                t_builder.Append(Separator).Append('\n');
                foreach (string t_line in Blocks[t_i].ToLines())
                {
                    t_builder.Append(t_line).Append('\n');
                }
            }
            return t_builder.ToString();
        }

        public byte[] ToBytes() => _encoding.GetBytes(ToText());

        public ParBlock? Find(string idn) => Blocks.FirstOrDefault(t_block => t_block.Idn == idn);

        private void AddBlock(List<string> lines, int firstLineNumber)
        {
            if (lines.Count == 0)
            {
                return;
            }
            if (!DriveTool.Idn.IsIdn(lines[0]))
            {
                // A second parameter set would start with another header; EAL only loads the first one anyway.
                throw new FormatException(
                    $"Line {firstLineNumber}: expected a parameter IDN, found '{lines[0]}'. Multi-set files aren't supported here.");
            }
            if (lines.Count == 6 && lines[1].Length == 0 && lines[2].All(t_c => t_c == '0'))
            {
                Unsupported.Add(lines[0]);
                return;
            }
            Blocks.Add(ParBlock.FromLines(lines, firstLineNumber));
        }
    }
}
