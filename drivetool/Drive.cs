using DriveTool.Par;
using EAL.EALConnection;
using EAL.Enums;
using EAL.Interfaces;
using EAL.Interfaces.Parameter;
using EAL.Interfaces.System;

namespace DriveTool
{
    /// <summary>
    /// EAL access shared by the commands. Uses a synchronous EALConnection: every call has completed
    /// when it returns (EAL SDK doc/html/fundamentals03.html).
    /// </summary>
    public static class Drive
    {
        public static IEALConnection Connect(string ip)
        {
            Output.Log($"Connecting to {ip} ...");
            IEALConnection t_connection = new EALConnection();
            t_connection.Connect(ip);
            return t_connection;
        }

        /// <summary>
        /// Bulk read: one SaveParameters call for all IDNs into memory, parsed as SERCOS-ASCII.
        /// IParameter has no multi-IDN ReadData; this is the bulk path. Per-IDN errors land in <paramref name="errors"/>.
        /// </summary>
        public static ParFile ReadBlocks(IEALConnection connection, IReadOnlyCollection<string> idns, List<string> errors)
        {
            using MemoryStream t_stream = new();
            EAL.Parameter.ParameterReadWrite.OnErrorHandler t_onError =
                (sender, e) => errors.Add($"{e.Parameter}: {e.ErrorCode}");
            connection.Parameter.ErrorOnUpdate += t_onError;
            try
            {
                connection.Parameter.SaveParameters(t_stream, idns.ToArray());
            }
            finally
            {
                connection.Parameter.ErrorOnUpdate -= t_onError;
            }
            return ParFile.FromBytes(t_stream.ToArray());
        }

        /// <summary>IDNs in the drive's ParameterSelection (e.g. Backup), via an in-memory SaveParameters.</summary>
        public static HashSet<string> ReadSelectionIdns(IEALConnection connection, ParameterSelection selection)
        {
            using MemoryStream t_stream = new();
            connection.Parameter.SaveParameters(t_stream, selection);
            return ParFile.FromBytes(t_stream.ToArray()).Blocks.Select(t_b => t_b.Idn).ToHashSet();
        }

        /// <summary>
        /// Bulk write: LoadParameters from an in-memory .par built from the drive's own blocks.
        /// Returns the error and message events raised during the load.
        /// </summary>
        public static (List<string> Errors, List<string> Messages) LoadBlocks(IEALConnection connection, ParFile file)
        {
            using MemoryStream t_stream = new(file.ToBytes());
            return Load(connection, () => connection.Parameter.LoadParameters(t_stream, 0));
        }

        public static (List<string> Errors, List<string> Messages) LoadFile(IEALConnection connection, string path, int setIndex)
        {
            return Load(connection, () => connection.Parameter.LoadParameters(path, setIndex));
        }

        public static AxisCondition GetCondition(IEALConnection connection) =>
            connection.Motion.Axes[0].GetCondition();

        /// <summary>PM (parameterization) or CM (configuration): any condition that isn't operating mode.</summary>
        public static bool IsPmOrCm(AxisCondition condition)
        {
            string t_name = condition.ToString();
            return t_name.EndsWith("_PARAMETERIZATION", StringComparison.Ordinal)
                || t_name.EndsWith("_CONFIGURATION", StringComparison.Ordinal);
        }

        /// <summary>
        /// Whether the attribute bits allow a write in the given condition. Only OM vs non-OM can be told:
        /// whether a "pm_or_cm" parameter needs PM or CM isn't in the attribute — the drive decides on write.
        /// </summary>
        public static bool WritableNow(ParAttribute attribute, AxisCondition condition) =>
            !attribute.IsReadOnly
            && (IsPmOrCm(condition) ? !attribute.ProtectedInP2 : !attribute.ProtectedInOperatingMode);

        /// <summary>
        /// Axis condition to write in. --mode pm|cm|current forces it; without --mode, PM is chosen when any
        /// parameter to change is blocked in the current (operating) mode. CM is never chosen automatically.
        /// Returns null to stay in the current condition, plus a reason for the plan.
        /// </summary>
        public static (AxisCondition? Mode, string? Reason) ResolveMode(
            CliArgs args, AxisCondition current, IEnumerable<ParAttribute> changed)
        {
            switch (args.Get("mode")?.ToLowerInvariant())
            {
                case "pm":
                    return (AxisCondition.AXIS_CONDITION_ACTIVE_PARAMETERIZATION, "--mode pm");
                case "cm":
                    return (AxisCondition.AXIS_CONDITION_ACTIVE_CONFIGURATION, "--mode cm (explicit)");
                case "current":
                    return (null, "--mode current");
                case null:
                    bool t_blocked = !IsPmOrCm(current)
                        && changed.Any(t_a => !t_a.IsReadOnly && t_a.ProtectedInOperatingMode && !t_a.ProtectedInP2);
                    return t_blocked
                        ? (AxisCondition.AXIS_CONDITION_ACTIVE_PARAMETERIZATION, "auto: pm_or_cm parameters can't be written in operating mode; PM is the default (CM only with --mode cm)")
                        : (null, null);
                case string t_other:
                    throw new UsageException($"--mode must be pm, cm or current, not '{t_other}'.");
            }
        }

        /// <summary>Hint for a refused write. CM is only suggested as an explicit user choice.</summary>
        public static string ModeHint(AxisCondition? mode) => mode switch
        {
            AxisCondition.AXIS_CONDITION_ACTIVE_PARAMETERIZATION =>
                "Refused in PM. Some parameters only accept writes in CM; use --mode cm only if the user explicitly asks for CM.",
            AxisCondition.AXIS_CONDITION_ACTIVE_CONFIGURATION => "Refused in CM; retry with --mode pm.",
            _ => "Written in the current condition. Parameters writable_in pm_or_cm need PM (the default when they're blocked).",
        };

        /// <summary>
        /// Switches axis 0 to <paramref name="target"/> (PM or CM; the drive is disabled there). Returns the
        /// previous condition to restore, or null if the axis was already in the target condition.
        /// </summary>
        public static AxisCondition? EnterMode(IEALConnection connection, AxisCondition target)
        {
            AxisCondition t_current = GetCondition(connection);
            if (t_current == target)
            {
                return null;
            }
            Output.Log($"Switching axis from {t_current} to {target} ...");
            connection.Motion.Axes[0].SetCondition(target);
            return t_current;
        }

        /// <summary>Parameterization mode (PM), as the firmware samples do before a transfer.</summary>
        public static AxisCondition? EnterParameterization(IEALConnection connection) =>
            EnterMode(connection, AxisCondition.AXIS_CONDITION_ACTIVE_PARAMETERIZATION);

        public static void RestoreCondition(IEALConnection connection, AxisCondition? previous)
        {
            if (previous is AxisCondition t_previous)
            {
                Output.Log($"Switching axis back to {t_previous} ...");
                connection.Motion.Axes[0].SetCondition(t_previous);
            }
        }

        private static (List<string> Errors, List<string> Messages) Load(IEALConnection connection, Action load)
        {
            List<string> t_errors = [];
            List<string> t_messages = [];
            EAL.Parameter.ParameterReadWrite.OnErrorHandler t_onError =
                (sender, e) => t_errors.Add($"{e.Parameter}: {e.ErrorCode}");
            EAL.Parameter.ParameterReadWrite.OnMessageHandler t_onMessage =
                (sender, e) => t_messages.Add(e.Message);
            connection.Parameter.ErrorOnUpdate += t_onError;
            connection.Parameter.MessageUpdate += t_onMessage;
            try
            {
                load();
            }
            finally
            {
                connection.Parameter.ErrorOnUpdate -= t_onError;
                connection.Parameter.MessageUpdate -= t_onMessage;
            }
            return (t_errors, t_messages);
        }

        /// <summary>JSON view of a block for command output.</summary>
        public static object Describe(ParBlock block) => new
        {
            idn = block.Idn,
            name = block.Name,
            unit = block.Unit == ParBlock.NotAvailable ? null : block.Unit,
            value = block.Value,
            values = block.Values,
            minimum = block.Minimum == ParBlock.NotAvailable ? null : block.Minimum,
            maximum = block.Maximum == ParBlock.NotAvailable ? null : block.Maximum,
            max_length_bytes = block.MaxLengthBytes,
            type = block.Attribute.DisplayType,
            decimal_places = block.Attribute.DecimalPlaces,
            is_list = block.Attribute.IsList && !block.Attribute.IsText,
            is_command = block.Attribute.IsCommand,
            writable_in = block.Attribute.WritableIn,
        };
    }
}
