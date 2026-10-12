namespace DriveTool.Par
{
    /// <summary>Display format, attribute bits 20-22.</summary>
    public enum ParDisplayType
    {
        Binary = 0,
        UnsignedDecimal = 1,
        SignedDecimal = 2,
        Hex = 3,
        Text = 4,
        Idn = 5,
        Float = 6,
        Time = 7,
    }

    /// <summary>
    /// SERCOS parameter attribute as written on line 3 of a .par block: 32 bits, MSB first.
    /// Bit layout was confirmed against a ctrlX DRIVE (FWA-XD1-AXS-V-0610N) by comparing
    /// attribute strings with the value types EAL returned for the same IDNs.
    /// </summary>
    public readonly struct ParAttribute(uint raw)
    {
        public uint Raw { get; } = raw;

        /// <summary>Bits 16-18: 1/2/3 = fixed 2/4/8 bytes, 4-7 = variable-length list of 1/2/4/8-byte elements.</summary>
        public int LengthCode => (int)((Raw >> 16) & 0x7);

        /// <summary>Bit 19: procedure command (e.g. S-0-0099 reset).</summary>
        public bool IsCommand => ((Raw >> 19) & 1) == 1;

        public ParDisplayType DisplayType => (ParDisplayType)((Raw >> 20) & 0x7);

        /// <summary>Bits 24-27: decimal places of fixed-point values.</summary>
        public int DecimalPlaces => (int)((Raw >> 24) & 0xF);

        /// <summary>Bit 28: write-protected in parameterization mode (P2).</summary>
        public bool ProtectedInP2 => ((Raw >> 28) & 1) == 1;

        /// <summary>Bit 29: write-protected in P3.</summary>
        public bool ProtectedInP3 => ((Raw >> 29) & 1) == 1;

        /// <summary>Bit 30: write-protected in operating mode (OM / bb / Ab).</summary>
        public bool ProtectedInOperatingMode => ((Raw >> 30) & 1) == 1;

        public bool IsReadOnly => ProtectedInP2 && ProtectedInP3 && ProtectedInOperatingMode;

        public bool IsList => LengthCode >= 4;

        public bool IsText => IsList && DisplayType == ParDisplayType.Text;

        public int ElementSize => LengthCode switch
        {
            1 => 2,
            2 => 4,
            3 => 8,
            4 => 1,
            5 => 2,
            6 => 4,
            7 => 8,
            _ => 0,
        };

        /// <summary>
        /// Where the parameter can be written: always, pm_or_cm (not in operating mode; whether PM or CM
        /// is needed isn't encoded in the attribute), p3_only, or never.
        /// </summary>
        public string WritableIn =>
            IsReadOnly ? "never"
            : !ProtectedInOperatingMode ? "always"
            : !ProtectedInP2 ? "pm_or_cm"
            : "p3_only";

        public static ParAttribute Parse(string bits)
        {
            string t_bits = bits.Trim();
            if (t_bits.Length != 32 || t_bits.Any(t_c => t_c != '0' && t_c != '1'))
            {
                throw new FormatException($"Attribute line '{bits}' is not 32 binary digits.");
            }
            return new ParAttribute(Convert.ToUInt32(t_bits, 2));
        }

        public string ToBitString() => Convert.ToString(Raw, 2).PadLeft(32, '0');
    }
}
