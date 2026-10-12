using System.Globalization;
using System.Text;

namespace DriveTool.Par
{
    /// <summary>
    /// Turns a user-supplied value into the data lines of a .par block, in the exact format the
    /// drive writes (so LoadParameters accepts it). The current block supplies the attribute,
    /// limits and list max length. Lists take comma-separated elements.
    /// </summary>
    public static class ParValueFormatter
    {
        private static readonly CultureInfo _invariant = CultureInfo.InvariantCulture;

        public static List<string> Format(ParBlock current, string input)
        {
            ParAttribute t_attr = current.Attribute;
            if (t_attr.IsCommand)
            {
                throw new FormatException($"{current.Idn} is a procedure command; writing it as a value isn't supported.");
            }
            if (t_attr.IsReadOnly)
            {
                throw new FormatException($"{current.Idn} is read-only.");
            }
            if (t_attr.DisplayType == ParDisplayType.Time)
            {
                throw new FormatException($"{current.Idn} is a SERCOS time value; writing it isn't supported.");
            }

            if (t_attr.IsText)
            {
                int t_length = Encoding.UTF8.GetByteCount(input);
                int t_max = current.MaxLengthBytes ?? throw new FormatException($"{current.Idn}: max length unknown.");
                if (t_length > t_max)
                {
                    throw new FormatException($"{current.Idn}: text is {t_length} bytes, max is {t_max}.");
                }
                return [$"{t_length} {t_max} {input}"];
            }

            if (t_attr.IsList)
            {
                string[] t_items = input.Trim().Length == 0
                    ? []
                    : input.Split(',', StringSplitOptions.TrimEntries);
                int t_max = current.MaxLengthBytes ?? throw new FormatException($"{current.Idn}: max length unknown.");
                int t_bytes = t_items.Length * t_attr.ElementSize;
                if (t_bytes > t_max)
                {
                    throw new FormatException(
                        $"{current.Idn}: {t_items.Length} elements = {t_bytes} bytes, max is {t_max} ({t_max / t_attr.ElementSize} elements).");
                }
                List<string> t_lines = [t_bytes.ToString(_invariant), t_max.ToString(_invariant)];
                t_lines.AddRange(t_items.Select(t_item => FormatScalar(current, t_item, checkLimits: false)));
                return t_lines;
            }

            return [FormatScalar(current, input, checkLimits: true)];
        }

        private static string FormatScalar(ParBlock current, string input, bool checkLimits)
        {
            ParAttribute t_attr = current.Attribute;
            string t_text = input.Trim();
            int t_bits = t_attr.ElementSize * 8;
            switch (t_attr.DisplayType)
            {
                case ParDisplayType.Binary:
                    return FormatBinary(ParseUnsigned(current.Idn, t_text, t_bits), t_bits);

                case ParDisplayType.Hex:
                    return "0x" + ParseUnsigned(current.Idn, t_text, t_bits).ToString("X" + (t_bits / 4), _invariant);

                case ParDisplayType.Idn:
                    return Idn.Normalize(t_text);

                case ParDisplayType.Float:
                    {
                        double t_value = double.TryParse(t_text, NumberStyles.Float, _invariant, out double t_parsed)
                            ? t_parsed
                            : throw new FormatException($"{current.Idn}: '{input}' is not a number.");
                        if (checkLimits)
                        {
                            CheckLimits(current, (decimal)t_value);
                        }
                        return t_value.ToString(_invariant);
                    }

                case ParDisplayType.UnsignedDecimal:
                case ParDisplayType.SignedDecimal:
                    {
                        decimal t_value = decimal.TryParse(t_text, NumberStyles.Float, _invariant, out decimal t_parsed)
                            ? t_parsed
                            : throw new FormatException($"{current.Idn}: '{input}' is not a number.");
                        int t_places = t_attr.DecimalPlaces;
                        if (decimal.Round(t_value, t_places) != t_value)
                        {
                            throw new FormatException($"{current.Idn}: '{input}' has more than {t_places} decimal place(s).");
                        }
                        if (t_attr.DisplayType == ParDisplayType.UnsignedDecimal && t_value < 0)
                        {
                            throw new FormatException($"{current.Idn}: value must not be negative.");
                        }
                        if (checkLimits)
                        {
                            CheckLimits(current, t_value);
                        }
                        return t_value.ToString("F" + t_places, _invariant);
                    }

                default:
                    throw new FormatException($"{current.Idn}: display type {t_attr.DisplayType} isn't supported for writing.");
            }
        }

        /// <summary>Accepts "0b0000.0011", "0000.0011", "0x1F" or a decimal integer.</summary>
        private static ulong ParseUnsigned(string idn, string text, int bits)
        {
            string t_clean = text.Replace(".", "").Replace("_", "");
            ulong t_value;
            if (t_clean.StartsWith("0b", StringComparison.OrdinalIgnoreCase)
                || (t_clean.Length == bits && t_clean.All(t_c => t_c is '0' or '1')))
            {
                string t_digits = t_clean.StartsWith("0b", StringComparison.OrdinalIgnoreCase) ? t_clean[2..] : t_clean;
                if (t_digits.Length == 0 || t_digits.Length > bits || t_digits.Any(t_c => t_c is not ('0' or '1')))
                {
                    throw new FormatException($"{idn}: '{text}' is not a {bits}-bit binary value.");
                }
                t_value = Convert.ToUInt64(t_digits, 2);
            }
            else if (t_clean.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                t_value = ulong.TryParse(t_clean[2..], NumberStyles.HexNumber, _invariant, out ulong t_hex)
                    ? t_hex
                    : throw new FormatException($"{idn}: '{text}' is not a hex value.");
            }
            else
            {
                t_value = ulong.TryParse(t_clean, NumberStyles.None, _invariant, out ulong t_dec)
                    ? t_dec
                    : throw new FormatException($"{idn}: '{text}' is not an unsigned integer, 0x hex or 0b binary value.");
            }
            if (bits < 64 && t_value >> bits != 0)
            {
                throw new FormatException($"{idn}: '{text}' doesn't fit in {bits} bits.");
            }
            return t_value;
        }

        /// <summary>Binary in the drive's .par form: groups of 4 bits joined by '.', no 0b prefix.</summary>
        private static string FormatBinary(ulong value, int bits)
        {
            string t_digits = Convert.ToString((long)value, 2).PadLeft(bits, '0');
            if (t_digits.Length > bits)
            {
                t_digits = t_digits[^bits..];
            }
            return string.Join('.', Enumerable.Range(0, bits / 4).Select(t_i => t_digits.Substring(t_i * 4, 4)));
        }

        private static void CheckLimits(ParBlock current, decimal value)
        {
            if (TryParseLimit(current.Minimum, out decimal t_min) && value < t_min)
            {
                throw new FormatException($"{current.Idn}: {value} is below the minimum {current.Minimum} {current.Unit}.");
            }
            if (TryParseLimit(current.Maximum, out decimal t_max) && value > t_max)
            {
                throw new FormatException($"{current.Idn}: {value} is above the maximum {current.Maximum} {current.Unit}.");
            }
        }

        private static bool TryParseLimit(string text, out decimal value) =>
            decimal.TryParse(text, NumberStyles.Float, _invariant, out value);
    }
}
