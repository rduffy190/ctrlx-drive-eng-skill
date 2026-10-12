using DriveTool.Commands;

namespace DriveTool
{
    /// <summary>
    /// drivetool — ctrlX DRIVE engineering CLI on the Easy Automation Library.
    /// Results are JSON on stdout; progress on stderr. Exit codes: 0 ok, 1 error, 2 usage, 3 confirmation required.
    /// </summary>
    internal static class Program
    {
        private const string _usage = """
            drivetool <command> [options]          (drive address: --ip <addr> or CTRLX_DRIVE_IP)

            Read-only
              discover [--interface <ip> --mask <m>] [--timeout-ms 2000]   SiP browse on local subnets
              ping                                                     EAL ping
              status                                                   identity, diagnosis, live values
              param read <IDN>... | @file                              bulk read (one call)
              param diff <file.par> [--include read-only]              drive vs file
              par show <file.par> [<IDN>...]                           offline: decode a .par file
              backup [--selection backup|modified|all] [--out file]    save parameters to .par
              firmware info                                            firmware version / file name
              scope signals [filter]                                   IDNs the scope can record

            Writes (print a plan and exit 3 unless --yes is given)
              param write <IDN=VALUE>... | @file [--mode pm|cm|current] [--yes]   bulk write + read-back
              restore <file.par> [--set 0] [--mode pm|cm|current] [--allow-non-backup] [--yes]   load a .par file
              scope run [--config scopeConfig.json] [--out dir] [--yes]       configure, trigger, record
              scope stop [--yes]                        stop an armed scope (frees its config params)
              firmware get [--out dir] [--yes]          upload .ibf from drive (param mode + reboot)
              firmware set <file.ibf> [--wait-s 300] [--yes]   download .ibf (param mode + reboot)
              command <IDN> [--timeout-ms 30000] [--yes]       run a procedure command (e.g. S-0-0447 C0300, S-0-0099 C0500)

            --mode         default: PM (parameterization) automatically when changed parameters are blocked in operating
                           mode, condition restored afterwards. cm = configuration mode, only when explicitly wanted.
                           current = never switch. Attribute bits only say "not in OM" (writable_in pm_or_cm).
            """;

        private static async Task<int> Main(string[] argv)
        {
            if (argv.Length == 0 || argv[0] is "-h" or "--help" or "help")
            {
                Console.Out.WriteLine(_usage);
                return argv.Length == 0 ? Output.ExitUsage : Output.ExitOk;
            }

            try
            {
                string t_command = argv[0];
                string? t_sub = argv.Length > 1 ? argv[1] : null;
                CliArgs t_args = CliArgs.Parse(argv.Skip(t_command is "param" or "par" or "firmware" or "scope" ? 2 : 1).ToList());
                return (t_command, t_sub) switch
                {
                    ("discover", _) => DiscoveryCommands.Discover(t_args),
                    ("ping", _) => DiscoveryCommands.Ping(t_args),
                    ("status", _) => DiscoveryCommands.Status(t_args),
                    ("param", "read") => ParamCommands.Read(t_args),
                    ("param", "write") => ParamCommands.Write(t_args),
                    ("param", "diff") => ParamCommands.Diff(t_args),
                    ("par", "show") => ParamCommands.Show(t_args),
                    ("backup", _) => BackupCommands.Backup(t_args),
                    ("restore", _) => BackupCommands.Restore(t_args),
                    ("firmware", "info") => FirmwareCommands.Info(t_args),
                    ("firmware", "get") => FirmwareCommands.Get(t_args),
                    ("firmware", "set") => FirmwareCommands.Set(t_args),
                    ("scope", "signals") => ScopeCommands.Signals(t_args),
                    ("scope", "run") => await ScopeCommands.Run(t_args),
                    ("scope", "stop") => ScopeCommands.Stop(t_args),
                    ("command", _) => CommandCommands.Run(t_args),
                    _ => throw new UsageException($"Unknown command '{string.Join(' ', argv.Take(2))}'. Run 'drivetool help'."),
                };
            }
            catch (UsageException ex)
            {
                Output.Json(new { error = ex.Message, kind = "usage" });
                return Output.ExitUsage;
            }
            catch (Exception ex)
            {
                Output.Json(new { error = ex.Message, kind = ex.GetType().Name });
                Output.Log(ex.ToString());
                return Output.ExitError;
            }
        }
    }
}
