using DriveTool.Par;
using EAL.Interfaces;
using EAL.Interfaces.Motion;

namespace DriveTool.Commands
{
    /// <summary>
    /// Execute a drive procedure command (e.g. S-0-0447 C0300 set absolute position, S-0-0099 C0500 reset).
    /// Uses IParameter.Command(idn, timeout), which runs the command synchronously
    /// (EAL SDK doc/html/interface_e_a_l_1_1_interfaces_1_1_parameter_1_1_i_parameter.html, Command()).
    /// </summary>
    public static class CommandCommands
    {
        public static int Run(CliArgs args)
        {
            string t_idn = Idn.Normalize(args.Positional(0, "command IDN (e.g. S-0-0447)"));
            int t_timeoutMs = args.GetInt("timeout-ms", 30000);
            using IEALConnection t_connection = Drive.Connect(args.Ip);

            List<string> t_errors = [];
            ParBlock t_block = Drive.ReadBlocks(t_connection, [t_idn], t_errors).Find(t_idn)
                ?? throw new UsageException($"{t_idn} is not supported by this drive.");
            if (!t_block.Attribute.IsCommand)
            {
                throw new UsageException($"{t_idn} ({t_block.Name}) is not a procedure command.");
            }

            object t_before = DiagnosisSnapshot(t_connection);
            object t_plan = new
            {
                drive = args.Ip,
                command = t_idn,
                name = t_block.Name,
                timeout_ms = t_timeoutMs,
                before = t_before,
            };
            if (!args.Has("yes"))
            {
                t_connection.Disconnect();
                return Output.ConfirmationRequired(t_plan);
            }

            string? t_error = null;
            Output.Log($"Executing {t_idn} ({t_block.Name}) ...");
            try
            {
                t_connection.Parameter.Command(t_idn, t_timeoutMs);
            }
            catch (Exception ex)
            {
                t_error = $"{ex.GetType().Name}: {ex.Message}";
            }
            object t_after = DiagnosisSnapshot(t_connection);
            t_connection.Disconnect();
            Output.Json(new
            {
                ok = t_error == null,
                command = t_idn,
                name = t_block.Name,
                error = t_error,
                before = t_before,
                after = t_after,
            });
            return t_error == null ? Output.ExitOk : Output.ExitError;
        }

        private static object DiagnosisSnapshot(IEALConnection connection)
        {
            IAxis t_axis = connection.Motion.Axes[0];
            return new
            {
                axis_condition = t_axis.GetCondition(),
                is_error = t_axis.IsError,
                diagnosis_number = t_axis.GetDiagnosisNumber().ToString("X8"),
                diagnosis_text = t_axis.GetDiagnosisText()?.TrimEnd(),
            };
        }
    }
}
