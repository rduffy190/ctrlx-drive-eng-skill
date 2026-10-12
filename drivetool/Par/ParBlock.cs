namespace DriveTool.Par
{
    /// <summary>
    /// One parameter in a SERCOS-ASCII file. Lines, in order:
    /// IDN, name, attribute (32 bits), unit, minimum, maximum, then data lines.
    /// Data is one line for scalars, "actual_len max_len text" for text, and
    /// "actual_len" / "max_len" / one line per element for lists (lengths in bytes).
    /// "--" means "not available" for unit/min/max.
    /// </summary>
    public sealed class ParBlock
    {
        public const string NotAvailable = "--";

        public string Idn { get; set; } = "";
        public string Name { get; set; } = "";
        public ParAttribute Attribute { get; set; }
        public string Unit { get; set; } = "";
        public string Minimum { get; set; } = "";
        public string Maximum { get; set; } = "";
        public List<string> Data { get; set; } = [];

        public static ParBlock FromLines(IReadOnlyList<string> lines, int firstLineNumber)
        {
            if (lines.Count < 7)
            {
                throw new FormatException($"Parameter block at line {firstLineNumber} has {lines.Count} lines; at least 7 expected.");
            }
            return new ParBlock
            {
                Idn = lines[0],
                Name = lines[1],
                Attribute = ParAttribute.Parse(lines[2]),
                Unit = lines[3],
                Minimum = lines[4],
                Maximum = lines[5],
                Data = lines.Skip(6).ToList(),
            };
        }

        public IEnumerable<string> ToLines()
        {
            yield return Idn;
            yield return Name;
            yield return Attribute.ToBitString();
            yield return Unit;
            yield return Minimum;
            yield return Maximum;
            foreach (string t_line in Data)
            {
                yield return t_line;
            }
        }

        public ParBlock CloneWithData(List<string> data) => new()
        {
            Idn = Idn,
            Name = Name,
            Attribute = Attribute,
            Unit = Unit,
            Minimum = Minimum,
            Maximum = Maximum,
            Data = data,
        };

        /// <summary>Max length in bytes for text/list parameters (2nd number of the text line, 2nd line of a list).</summary>
        public int? MaxLengthBytes
        {
            get
            {
                if (Attribute.IsText && Data.Count > 0)
                {
                    string[] t_parts = Data[0].Split(' ', 3);
                    return t_parts.Length >= 2 && int.TryParse(t_parts[1], out int t_max) ? t_max : null;
                }
                if (Attribute.IsList && Data.Count >= 2)
                {
                    return int.TryParse(Data[1], out int t_max) ? t_max : null;
                }
                return null;
            }
        }

        /// <summary>The value as text: the string for text params, the single data line for scalars, null for lists.</summary>
        public string? Value
        {
            get
            {
                if (Attribute.IsText)
                {
                    string[] t_parts = Data.Count > 0 ? Data[0].Split(' ', 3) : [];
                    return t_parts.Length == 3 ? t_parts[2] : "";
                }
                if (Attribute.IsList)
                {
                    return null;
                }
                return Data.Count > 0 ? Data[0] : null;
            }
        }

        /// <summary>List elements, or null for non-list parameters.</summary>
        public List<string>? Values =>
            Attribute.IsList && !Attribute.IsText ? Data.Skip(2).ToList() : null;

        /// <summary>Data lines compared as a unit; trailing blank lines are ignored.</summary>
        public bool SameDataAs(ParBlock other) =>
            TrimTrailingBlank(Data).SequenceEqual(TrimTrailingBlank(other.Data));

        private static IEnumerable<string> TrimTrailingBlank(List<string> lines)
        {
            int t_count = lines.Count;
            while (t_count > 0 && lines[t_count - 1].Length == 0)
            {
                t_count--;
            }
            return lines.Take(t_count);
        }
    }
}
