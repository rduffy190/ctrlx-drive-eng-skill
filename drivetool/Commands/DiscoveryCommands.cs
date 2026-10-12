using System.Net.NetworkInformation;
using System.Net.Sockets;
using DriveTool.Par;
using EAL.EALConnection;
using EAL.Interfaces;
using EAL.Interfaces.Browse;
using EAL.Utils;

namespace DriveTool.Commands
{
    /// <summary>discover, ping, status — read-only.</summary>
    public static class DiscoveryCommands
    {
        /// <summary>IDNs bulk-read by "status" (one SaveParameters call).</summary>
        private static readonly string[] _statusIdns =
        [
            "S-0-0030", // firmware version
            "S-0-0140", // controller type
            "S-0-0141", // motor type
            "S-0-0095", // diagnostic message text
            "S-0-0390", // diagnostic number
            "S-0-0135", // drive status word
            "P-0-0115", // axis state machine status word
            "S-0-0040", // velocity feedback
            "S-0-0051", // position feedback encoder 1
            "S-0-0084", // torque/force feedback
        ];

        /// <summary>
        /// SiP browse on each local IPv4 interface (or one given with --interface/--mask), pattern from
        /// EAL SDK doc/html/interface_e_a_l_1_1_interfaces_1_1_browse_1_1_i_browse_devices.html.
        /// Broadcast-based: drives behind a router won't show up.
        /// </summary>
        public static int Discover(CliArgs args)
        {
            List<(string Ip, string Mask)> t_interfaces = [];
            string? t_onlyIp = args.Get("interface");
            if (t_onlyIp != null)
            {
                t_interfaces.Add((t_onlyIp, args.Get("mask") ?? "255.255.255.0"));
            }
            else
            {
                foreach (NetworkInterface t_nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (t_nic.OperationalStatus != OperationalStatus.Up || t_nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    {
                        continue;
                    }
                    foreach (UnicastIPAddressInformation t_address in t_nic.GetIPProperties().UnicastAddresses)
                    {
                        if (t_address.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            t_interfaces.Add((t_address.Address.ToString(), t_address.IPv4Mask.ToString()));
                        }
                    }
                }
            }

            int t_timeoutMs = args.GetInt("timeout-ms", 2000);
            List<BrowseDevices> t_browsers = [];
            foreach ((string t_ip, string t_mask) in t_interfaces)
            {
                Output.Log($"Browsing from {t_ip}/{t_mask} ...");
                BrowseDevices t_browser = new(t_ip, t_mask, false, 0, 511, t_timeoutMs);
                t_browser.Begin();
                t_browsers.Add(t_browser);
            }

            List<object> t_results = [];
            foreach (BrowseDevices t_browser in t_browsers)
            {
                t_browser.End();
                foreach (var t_node in t_browser.FoundNodes ?? [])
                {
                    t_results.Add(new
                    {
                        ip_address = t_node.IPAddress?.ToString(),
                        subnet = t_node.Subnet?.ToString(),
                        gateway = t_node.Gateway?.ToString(),
                        mac_address = t_node.MACAddress?.ToString(),
                        display_name = t_node.DisplayName,
                        host_name = t_node.HostName,
                        found_from = t_browser.SenderIPAddress,
                    });
                }
                t_browser.Dispose();
            }
            Output.Json(new { interfaces = t_interfaces.Select(t_i => $"{t_i.Ip}/{t_i.Mask}"), devices = t_results });
            return Output.ExitOk;
        }

        /// <summary>EAL ping (EAL SDK doc/html/fundamentals05_01.html).</summary>
        public static int Ping(CliArgs args)
        {
            string t_ip = args.Ip;
            using PingEALConnection t_ping = new();
            EALPingReply t_reply = t_ping.Send(t_ip, args.GetInt("timeout-ms", 2000));
            bool t_ok = t_reply.EALStatus == EALIPStatus.Success;
            Output.Json(new
            {
                ip_address = t_ip,
                reachable = t_ok,
                eal_status = t_reply.EALStatus,
                common_error_code = t_reply.CommonErrorCode,
                supported_message_types = t_ok ? t_reply.SupportedMessageType : null,
            });
            return t_ok ? Output.ExitOk : Output.ExitError;
        }

        /// <summary>
        /// Identity, diagnosis and live values. ISystem getters that throw NotImplementedException on
        /// ctrlX DRIVE (GetTemperature, GetCpuLoad, GetHardwareDetails, GetDiagnosisLog, ...) are not used.
        /// </summary>
        public static int Status(CliArgs args)
        {
            using IEALConnection t_connection = Drive.Connect(args.Ip);
            List<string> t_errors = [];
            ParFile t_file = Drive.ReadBlocks(t_connection, _statusIdns, t_errors);
            AxisView t_axis = new(t_connection);
            Output.Json(new
            {
                ip_address = t_connection.System.GetIpAddress(),
                ip_suite = t_connection.System.GetIPSuite(),
                mac_address = t_connection.System.GetMacAddress(),
                name = t_connection.System.GetName(),
                serial_number = t_connection.System.GetSerialNumber(),
                operation_hours = t_connection.System.GetOperationHours(),
                type_plate = t_file.TypePlate,
                axis_condition = t_axis.Condition,
                operating_mode = t_axis.OperatingMode,
                is_error = t_axis.IsError,
                diagnosis_number = t_axis.DiagnosisNumber,
                diagnosis_text = t_axis.DiagnosisText?.TrimEnd(),
                parameters = t_file.Blocks.Select(Drive.Describe),
                errors = t_errors.Count > 0 ? t_errors : null,
            });
            t_connection.Disconnect();
            return Output.ExitOk;
        }

        /// <summary>Axis-level getters read once.</summary>
        private sealed class AxisView(IEALConnection connection)
        {
            public string Condition { get; } = connection.Motion.Axes[0].GetCondition().ToString();
            public string OperatingMode { get; } = connection.Motion.Axes[0].SelectedOperatingMode.ToString();
            public bool IsError { get; } = connection.Motion.Axes[0].IsError;
            public string DiagnosisNumber { get; } = connection.Motion.Axes[0].GetDiagnosisNumber().ToString("X8");
            public string? DiagnosisText { get; } = connection.Motion.Axes[0].GetDiagnosisText();
        }
    }
}
