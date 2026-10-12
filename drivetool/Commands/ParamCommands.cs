using DriveTool.Par;
using EAL.Enums;
using EAL.Interfaces;

namespace DriveTool.Commands
{
    /// <summary>
    /// param read / write / diff and the offline "par show". All drive access is bulk: one
    /// SaveParameters call to read any number of IDNs, one LoadParameters call to write them.
    /// </summary>
    public static class ParamCommands
    {
        private const string _loadErrorHint =
            "SERVICE_SPECIFIC = the drive refused the value. Common causes: write-protected by state (scope parameters "
            + "while the scope is armed - 'scope stop'; pm_or_cm parameters in operating mode - --mode pm or --mode cm) "
            + "or a value the drive rejects for its configuration. A single-IDN write via WriteData gives the drive's text.";

        public static int Read(CliArgs args)
        {
            List<string> t_idns = args.Positionals.Select(Idn.Normalize).Distinct().ToList();
            if (t_idns.Count == 0)
            {
                throw new UsageException("param read needs at least one IDN (or @file with one IDN per line).");
            }
            using IEALConnection t_connection = Drive.Connect(args.Ip);
            List<string> t_errors = [];
            ParFile t_file = Drive.ReadBlocks(t_connection, t_idns, t_errors);
            t_connection.Disconnect();

            List<string> t_missing = t_idns.Where(t_idn => t_file.Find(t_idn) == null).ToList();
            Output.Json(new
            {
                parameters = t_file.Blocks.Select(Drive.Describe),
                missing = t_missing.Count > 0 ? t_missing : null,
                errors = t_errors.Count > 0 ? t_errors : null,
            });
            return t_missing.Count > 0 ? Output.ExitError : Output.ExitOk;
        }

        /// <summary>
        /// Plan, then (with --yes) write IDN=VALUE pairs in one LoadParameters call and read them back.
        /// --mode pm|cm switches the axis to PM or CM for the write and back afterwards.
        /// </summary>
        public static int Write(CliArgs args)
        {
            Dictionary<string, string> t_requested = ParseAssignments(args.Positionals);
            using IEALConnection t_connection = Drive.Connect(args.Ip);

            List<string> t_readErrors = [];
            ParFile t_current = Drive.ReadBlocks(t_connection, t_requested.Keys, t_readErrors);
            AxisCondition t_condition = Drive.GetCondition(t_connection);
            ParFile t_toWrite = new() { Header = t_current.Header };
            List<object> t_plan = [];
            List<string> t_problems = [];
            foreach ((string t_idn, string t_value) in t_requested)
            {
                ParBlock? t_block = t_current.Find(t_idn);
                if (t_block == null)
                {
                    t_problems.Add($"{t_idn}: not returned by the drive (unsupported IDN?).");
                    continue;
                }
                List<string> t_newData;
                try
                {
                    t_newData = ParValueFormatter.Format(t_block, t_value);
                }
                catch (FormatException ex)
                {
                    t_problems.Add(ex.Message);
                    continue;
                }
                ParBlock t_newBlock = t_block.CloneWithData(t_newData);
                bool t_unchanged = t_block.SameDataAs(t_newBlock);
                t_plan.Add(new
                {
                    idn = t_idn,
                    name = t_block.Name,
                    unit = t_block.Unit == ParBlock.NotAvailable ? null : t_block.Unit,
                    current = DisplayValue(t_block),
                    @new = DisplayValue(t_newBlock),
                    unchanged = t_unchanged,
                    writable_in = t_block.Attribute.WritableIn,
                });
                if (!t_unchanged)
                {
                    t_toWrite.Blocks.Add(t_newBlock);
                }
            }

            // Mode: PM automatically when changed parameters are blocked in OM; CM only with --mode cm.
            (AxisCondition? t_mode, string? t_modeReason) = Drive.ResolveMode(
                args, t_condition, t_toWrite.Blocks.Select(t_b => t_b.Attribute));
            AxisCondition t_writeCondition = t_mode ?? t_condition;
            foreach (ParBlock t_block in t_toWrite.Blocks.Where(t_b => !Drive.WritableNow(t_b.Attribute, t_writeCondition)))
            {
                t_problems.Add($"{t_block.Idn}: writable in '{t_block.Attribute.WritableIn}', axis would be {t_writeCondition}.");
            }

            object t_planJson = new
            {
                drive = args.Ip,
                axis_condition = t_condition,
                write_in_condition = t_writeCondition,
                switch_condition = t_writeCondition != t_condition,
                mode_reason = t_modeReason,
                writes = t_plan,
                problems = t_problems.Count > 0 ? t_problems : null,
                read_errors = t_readErrors.Count > 0 ? t_readErrors : null,
            };
            if (t_problems.Count > 0)
            {
                Output.Json(new { ok = false, plan = t_planJson });
                Output.Log("Plan has problems; nothing written.");
                return Output.ExitError;
            }
            if (t_toWrite.Blocks.Count == 0)
            {
                Output.Json(new { ok = true, nothing_to_do = true, plan = t_planJson });
                return Output.ExitOk;
            }
            if (!args.Has("yes"))
            {
                return Output.ConfirmationRequired(t_planJson);
            }

            AxisCondition? t_previous = t_mode is AxisCondition t_target ? Drive.EnterMode(t_connection, t_target) : null;
            (List<string> Errors, List<string> Messages) t_result;
            try
            {
                Output.Log($"Writing {t_toWrite.Blocks.Count} parameter(s) in one LoadParameters call ...");
                t_result = Drive.LoadBlocks(t_connection, t_toWrite);
            }
            finally
            {
                Drive.RestoreCondition(t_connection, t_previous);
            }

            // Read back in one bulk call and compare data lines.
            List<string> t_verifyErrors = [];
            ParFile t_after = Drive.ReadBlocks(t_connection, t_toWrite.Blocks.Select(t_b => t_b.Idn).ToList(), t_verifyErrors);
            t_connection.Disconnect();
            List<WriteCheck> t_verified = t_toWrite.Blocks.Select(t_wanted =>
            {
                ParBlock? t_actual = t_after.Find(t_wanted.Idn);
                return new WriteCheck(
                    t_wanted.Idn,
                    DisplayValue(t_wanted),
                    t_actual == null ? null : DisplayValue(t_actual),
                    t_actual != null && t_actual.SameDataAs(t_wanted));
            }).ToList();
            bool t_allOk = t_result.Errors.Count == 0 && t_verified.All(t_v => t_v.Ok);
            Output.Json(new
            {
                ok = t_allOk,
                written = t_verified,
                load_errors = t_result.Errors.Count > 0 ? t_result.Errors : null,
                load_messages = t_result.Messages.Count > 0 ? t_result.Messages : null,
                hint = t_result.Errors.Count > 0 ? _loadErrorHint + " " + Drive.ModeHint(t_mode) : null,
                verify_errors = t_verifyErrors.Count > 0 ? t_verifyErrors : null,
            });
            return t_allOk ? Output.ExitOk : Output.ExitError;
        }

        /// <summary>Compare a .par file with the drive (one bulk read of the file's IDNs). Read-only.</summary>
        public static int Diff(CliArgs args)
        {
            string t_path = args.Positional(0, "parameter file");
            ParFile t_file = ParFile.Load(t_path);
            bool t_includeReadOnly = args.Get("include") == "read-only";
            List<ParBlock> t_candidates = t_file.Blocks
                .Where(t_b => !t_b.Attribute.IsCommand && (t_includeReadOnly || !t_b.Attribute.IsReadOnly))
                .ToList();

            using IEALConnection t_connection = Drive.Connect(args.Ip);
            List<string> t_errors = [];
            ParFile t_drive = Drive.ReadBlocks(t_connection, t_candidates.Select(t_b => t_b.Idn).ToList(), t_errors);
            t_connection.Disconnect();

            List<object> t_different = [];
            List<string> t_missing = [];
            foreach (ParBlock t_wanted in t_candidates)
            {
                ParBlock? t_actual = t_drive.Find(t_wanted.Idn);
                if (t_actual == null)
                {
                    t_missing.Add(t_wanted.Idn);
                }
                else if (!t_actual.SameDataAs(t_wanted))
                {
                    t_different.Add(new
                    {
                        idn = t_wanted.Idn,
                        name = t_wanted.Name,
                        unit = t_wanted.Unit == ParBlock.NotAvailable ? null : t_wanted.Unit,
                        file = DisplayValue(t_wanted),
                        drive = DisplayValue(t_actual),
                        writable_in = t_wanted.Attribute.WritableIn,
                    });
                }
            }
            Output.Json(new
            {
                file = Path.GetFullPath(t_path),
                file_type_plate = t_file.TypePlate,
                drive_type_plate = t_drive.TypePlate,
                compared = t_candidates.Count,
                different_count = t_different.Count,
                different = t_different,
                missing_on_drive = t_missing.Count > 0 ? t_missing : null,
                errors = t_errors.Count > 0 ? t_errors : null,
            });
            return Output.ExitOk;
        }

        /// <summary>Offline: show parameters from a .par file (all, or the IDNs given).</summary>
        public static int Show(CliArgs args)
        {
            string t_path = args.Positional(0, "parameter file");
            ParFile t_file = ParFile.Load(t_path);
            HashSet<string> t_only = args.Positionals.Skip(1).Select(Idn.Normalize).ToHashSet();
            bool t_roundTrips = t_file.ToText() == File.ReadAllText(t_path).Replace("\r\n", "\n");
            Output.Json(new
            {
                file = Path.GetFullPath(t_path),
                header = t_file.Header,
                type_plate = t_file.TypePlate,
                parameter_count = t_file.Blocks.Count,
                round_trips = t_roundTrips,
                parameters = t_file.Blocks.Where(t_b => t_only.Count == 0 || t_only.Contains(t_b.Idn)).Select(Drive.Describe),
            });
            return Output.ExitOk;
        }

        private sealed record WriteCheck(string Idn, string Expected, string? Actual, bool Ok);

        private static string DisplayValue(ParBlock block) => block.Value ?? string.Join(", ", block.Values ?? []);

        private static Dictionary<string, string> ParseAssignments(IEnumerable<string> items)
        {
            Dictionary<string, string> t_result = new(StringComparer.Ordinal);
            foreach (string t_item in items)
            {
                int t_eq = t_item.IndexOf('=');
                if (t_eq <= 0)
                {
                    throw new UsageException($"'{t_item}' is not IDN=VALUE.");
                }
                string t_idn = Idn.Normalize(t_item[..t_eq]);
                if (!t_result.TryAdd(t_idn, t_item[(t_eq + 1)..]))
                {
                    throw new UsageException($"{t_idn} is given more than once.");
                }
            }
            if (t_result.Count == 0)
            {
                throw new UsageException("param write needs at least one IDN=VALUE (or @file with one per line).");
            }
            return t_result;
        }
    }
}
