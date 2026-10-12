using System.Text.Json;

namespace DriveTool.Commands
{
    /// <summary>Settings for "scope run", loaded from JSON with snake_case keys (see scopeConfig.json).</summary>
    public sealed class ScopeConfig
    {
        /// <summary>Parameter IDNs to record, one per scope channel (max 8 per EAL).</summary>
        public List<string> Signals { get; set; } = [];

        /// <summary>Sample interval in microseconds (P-0-0031; drive range 125..8000000).</summary>
        public int TimeResolutionUs { get; set; }

        /// <summary>Memory depth in samples (P-0-0032; drive range 1..8192).</summary>
        public int MemorySize { get; set; }

        /// <summary>
        /// Share of the memory depth recorded before the trigger, 0-100 %, passed as-is to ITrigger.PreTrigger.
        /// The EAL docs don't state the unit; a sample count was rejected by the drive in testing (OscopeTest).
        /// </summary>
        public int PreTriggerPercent { get; set; }

        public ScopeTriggerConfig Trigger { get; set; } = new();

        /// <summary>How long to wait for the scope to reach Ready after starting.</summary>
        public int MeasurementTimeoutMs { get; set; } = 60000;

        /// <summary>Folder for the result file, relative to the working directory unless absolute.</summary>
        public string OutputDirectory { get; set; } = "output";

        /// <summary>Requested pre-trigger samples; the actual count can be lower if the trigger comes early.</summary>
        public int PreTriggerSamples => (int)Math.Round(MemorySize * PreTriggerPercent / 100.0);

        /// <summary>Time to fill the pre-trigger buffer after arming, plus 10 % margin.</summary>
        public int PreTriggerFillMs => (int)Math.Ceiling(PreTriggerSamples * (double)TimeResolutionUs / 1000.0 * 1.1);

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        public static ScopeConfig Load(string path)
        {
            if (!File.Exists(path))
            {
                throw new UsageException($"Scope config '{path}' not found.");
            }
            ScopeConfig t_config = JsonSerializer.Deserialize<ScopeConfig>(File.ReadAllText(path), _jsonOptions)
                ?? throw new InvalidDataException($"Config file '{path}' is empty.");
            t_config.Validate();
            return t_config;
        }

        private void Validate()
        {
            if (Signals.Count is 0 or > 8)
            {
                throw new InvalidDataException("signals must list 1 to 8 parameter IDNs.");
            }
            Signals = Signals.Select(Idn.Normalize).ToList();
            if (TimeResolutionUs <= 0)
            {
                throw new InvalidDataException("time_resolution_us must be > 0.");
            }
            if (MemorySize <= 0)
            {
                throw new InvalidDataException("memory_size must be > 0.");
            }
            if (PreTriggerPercent is < 0 or > 100)
            {
                throw new InvalidDataException("pre_trigger_percent must be between 0 and 100.");
            }
            Trigger.Validate();
        }
    }

    public sealed class ScopeTriggerConfig
    {
        /// <summary>"manual" (RaiseTrigger), "signal" (trigger on a parameter value) or "auto".</summary>
        public string Type { get; set; } = "manual";

        /// <summary>
        /// manual only: raise the trigger this many ms after arming; -1 = wait for Enter on stdin;
        /// null = wait until the pre-trigger buffer is full (otherwise fewer pre-trigger samples are recorded).
        /// </summary>
        public int? ManualDelayMs { get; set; }

        /// <summary>signal only: trigger source IDN (P-0-0026).</summary>
        public string? Signal { get; set; }

        /// <summary>signal only: trigger level, passed to ITrigger.ValueAsString.</summary>
        public string? Value { get; set; }

        /// <summary>signal only: rising, falling, both or equal (P-0-0030).</summary>
        public string Edge { get; set; } = "rising";

        /// <summary>signal only: trigger mask (P-0-0025); 0 = no mask.</summary>
        public uint Mask { get; set; }

        public void Validate()
        {
            if (Type is not ("manual" or "signal" or "auto"))
            {
                throw new InvalidDataException("trigger.type must be manual, signal or auto.");
            }
            if (Type == "signal")
            {
                if (string.IsNullOrWhiteSpace(Signal) || string.IsNullOrWhiteSpace(Value))
                {
                    throw new InvalidDataException("trigger.signal and trigger.value are required for a signal trigger.");
                }
                Signal = Idn.Normalize(Signal);
                if (Edge is not ("rising" or "falling" or "both" or "equal"))
                {
                    throw new InvalidDataException("trigger.edge must be rising, falling, both or equal.");
                }
            }
        }
    }
}
