using System.Diagnostics;
using DriveTool.Par;
using EAL.Enums;
using EAL.Interfaces;

namespace DriveTool.Commands
{
    /// <summary>
    /// backup (read-only) and restore (write) of whole parameter files.
    /// Patterns: EAL SDK samples/Sample - SaveParameters, Sample - LoadParameters.
    /// Default selection is Backup (the drive's S-0-0192 list): restorable, and it excludes live values
    /// such as S-0-0047 position command and S-0-1305 system time that an "All" file contains.
    /// </summary>
    public static class BackupCommands
    {
        public static int Backup(CliArgs args)
        {
            ParameterSelection t_selection = (args.Get("selection") ?? "backup").ToLowerInvariant() switch
            {
                "all" => ParameterSelection.All,
                "modified" => ParameterSelection.Modified,
                "backup" => ParameterSelection.Backup,
                string t_other => throw new UsageException($"--selection must be all, modified or backup, not '{t_other}'."),
            };
            string t_ip = args.Ip;
            string t_path = Path.GetFullPath(args.Get("out")
                ?? Path.Combine("output", $"backup_{t_ip}_{t_selection.ToString().ToLowerInvariant()}_{DateTime.Now:yyyyMMdd_HHmmss}.par"));
            Directory.CreateDirectory(Path.GetDirectoryName(t_path)!);
            if (File.Exists(t_path))
            {
                throw new UsageException($"'{t_path}' already exists; pick another --out.");
            }

            using IEALConnection t_connection = Drive.Connect(t_ip);
            List<string> t_errors = [];
            EAL.Parameter.ParameterReadWrite.OnErrorHandler t_onError =
                (sender, e) => t_errors.Add($"{e.Parameter}: {e.ErrorCode}");
            t_connection.Parameter.ErrorOnUpdate += t_onError;
            Stopwatch t_timer = Stopwatch.StartNew();
            Output.Log($"Saving {t_selection} parameters to {t_path} ...");
            t_connection.Parameter.SaveParameters(t_path, t_selection, false);
            t_connection.Parameter.ErrorOnUpdate -= t_onError;
            t_connection.Disconnect();

            ParFile t_saved = ParFile.Load(t_path);
            Output.Json(new
            {
                file = t_path,
                selection = t_selection,
                parameter_count = t_saved.Blocks.Count,
                type_plate = t_saved.TypePlate,
                elapsed_ms = t_timer.ElapsedMilliseconds,
                errors = t_errors.Count > 0 ? t_errors : null,
            });
            return Output.ExitOk;
        }

        /// <summary>
        /// Without --yes: diff file vs drive and print the plan. With --yes: LoadParameters(file, set).
        /// --mode pm|cm switches the axis to PM or CM for the load and back afterwards.
        /// </summary>
        public static int Restore(CliArgs args)
        {
            string t_path = Path.GetFullPath(args.Positional(0, "parameter file"));
            int t_setIndex = args.GetInt("set", 0);
            ParFile t_file = ParFile.Load(t_path);
            List<ParBlock> t_writable = t_file.Blocks.Where(t_b => !t_b.Attribute.IsCommand && !t_b.Attribute.IsReadOnly).ToList();

            using IEALConnection t_connection = Drive.Connect(args.Ip);
            List<string> t_readErrors = [];
            ParFile t_drive = Drive.ReadBlocks(t_connection, t_writable.Select(t_b => t_b.Idn).ToList(), t_readErrors);
            Output.Log("Reading the drive's Backup selection to check the file against it ...");
            HashSet<string> t_backupList = Drive.ReadSelectionIdns(t_connection, ParameterSelection.Backup);
            List<string> t_nonBackup = t_writable.Select(t_b => t_b.Idn).Where(t_idn => !t_backupList.Contains(t_idn)).ToList();
            bool t_allowNonBackup = args.Has("allow-non-backup");
            AxisCondition t_condition = Drive.GetCondition(t_connection);

            List<object> t_changes = [];
            foreach (ParBlock t_wanted in t_writable)
            {
                ParBlock? t_actual = t_drive.Find(t_wanted.Idn);
                if (t_actual != null && !t_actual.SameDataAs(t_wanted))
                {
                    t_changes.Add(new
                    {
                        idn = t_wanted.Idn,
                        name = t_wanted.Name,
                        drive = t_actual.Value ?? string.Join(", ", t_actual.Values ?? []),
                        file = t_wanted.Value ?? string.Join(", ", t_wanted.Values ?? []),
                        writable_in = t_wanted.Attribute.WritableIn,
                    });
                }
            }
            int t_needParamMode = t_writable.Count(t_b => t_b.Attribute.ProtectedInOperatingMode);
            // Mode: PM automatically when changed parameters are blocked in OM; CM only with --mode cm.
            (AxisCondition? t_mode, string? t_modeReason) = Drive.ResolveMode(
                args, t_condition, t_writable.Where(t_b => t_drive.Find(t_b.Idn) is ParBlock t_a && !t_a.SameDataAs(t_b)).Select(t_b => t_b.Attribute));
            object t_plan = new
            {
                file = t_path,
                set_index = t_setIndex,
                file_type_plate = t_file.TypePlate,
                drive_type_plate = t_drive.TypePlate,
                axis_condition = t_condition,
                write_in_condition = t_mode ?? t_condition,
                switch_condition = t_mode != null && t_mode != t_condition,
                mode_reason = t_modeReason,
                writable_parameters_in_file = t_writable.Count,
                parameters_needing_pm_or_cm = t_needParamMode,
                non_backup_parameter_count = t_nonBackup.Count,
                non_backup_parameters = t_nonBackup.Count > 0 ? t_nonBackup.Take(40) : null,
                problem = t_nonBackup.Count > 0 && !t_allowNonBackup
                    ? $"File has {t_nonBackup.Count} writable parameter(s) outside the drive's Backup selection "
                        + "(live values like position commands, system time). Restore a --selection backup file, "
                        + "or pass --allow-non-backup after reviewing them."
                    : null,
                change_count = t_changes.Count,
                changes = t_changes,
                read_errors = t_readErrors.Count > 0 ? t_readErrors : null,
            };
            if (t_nonBackup.Count > 0 && !t_allowNonBackup)
            {
                t_connection.Disconnect();
                Output.Json(new { ok = false, plan = t_plan });
                Output.Log("Restore refused; nothing written.");
                return Output.ExitError;
            }
            if (!args.Has("yes"))
            {
                t_connection.Disconnect();
                return Output.ConfirmationRequired(t_plan);
            }

            AxisCondition? t_previous = t_mode is AxisCondition t_target ? Drive.EnterMode(t_connection, t_target) : null;
            (List<string> Errors, List<string> Messages) t_result;
            Stopwatch t_timer = Stopwatch.StartNew();
            try
            {
                Output.Log($"Loading {t_path} (set {t_setIndex}) ...");
                t_result = Drive.LoadFile(t_connection, t_path, t_setIndex);
            }
            finally
            {
                Drive.RestoreCondition(t_connection, t_previous);
            }
            t_connection.Disconnect();
            Output.Json(new
            {
                ok = t_result.Errors.Count == 0,
                file = t_path,
                elapsed_ms = t_timer.ElapsedMilliseconds,
                load_errors = t_result.Errors.Count > 0 ? t_result.Errors : null,
                mode_hint = t_result.Errors.Count > 0 ? Drive.ModeHint(t_mode) : null,
                load_messages = t_result.Messages.Count > 0 ? t_result.Messages : null,
                hint = "Run 'param diff' with the same file to confirm what is still different.",
            });
            return t_result.Errors.Count == 0 ? Output.ExitOk : Output.ExitError;
        }
    }
}
