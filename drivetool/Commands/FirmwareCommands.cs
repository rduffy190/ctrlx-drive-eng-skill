using System.Diagnostics;
using EAL.EALConnection;
using EAL.Enums;
using EAL.Exceptions;
using EAL.Interfaces;
using EAL.Interfaces.System;

namespace DriveTool.Commands
{
    /// <summary>
    /// firmware info (read-only), get and set. get/set follow EAL SDK samples/Sample - GetFirmwareFileSample
    /// and Sample - SetFirmwareSample: switch to parameterization mode, transfer, reboot.
    /// </summary>
    public static class FirmwareCommands
    {
        public static int Info(CliArgs args)
        {
            using IEALConnection t_connection = Drive.Connect(args.Ip);
            Output.Json(new
            {
                firmware = t_connection.System.FirmwareManagement.GetFirmware(),
                firmware_filename = t_connection.System.FirmwareManagement.GetFirmwareFilename(),
                axis_condition = Drive.GetCondition(t_connection),
            });
            t_connection.Disconnect();
            return Output.ExitOk;
        }

        /// <summary>Upload the drive's firmware to a local .ibf file. The sample reboots the drive afterwards.</summary>
        public static int Get(CliArgs args)
        {
            string t_ip = args.Ip;
            string t_directory = Path.GetFullPath(args.Get("out") ?? "output");
            using IEALConnection t_connection = Drive.Connect(t_ip);
            IFirmwareManagement t_firmware = t_connection.System.FirmwareManagement;
            string t_fileName = t_firmware.GetFirmwareFilename();
            string t_path = Path.Combine(t_directory, t_fileName);
            object t_plan = new
            {
                drive = t_ip,
                firmware = t_firmware.GetFirmware(),
                destination = t_path,
                steps = new[] { "switch axis to parameterization mode (drive disabled)", "upload firmware file", "reboot drive" },
            };
            if (File.Exists(t_path))
            {
                throw new UsageException($"'{t_path}' already exists; pick another --out directory.");
            }
            if (!args.Has("yes"))
            {
                t_connection.Disconnect();
                return Output.ConfirmationRequired(t_plan);
            }

            Directory.CreateDirectory(t_directory);
            List<string> t_errors = Transfer(t_connection, () => t_firmware.GetFirmwareFile(t_path));
            Output.Log("Rebooting drive ...");
            t_connection.System.Reboot();
            bool t_ok = t_errors.Count == 0 && File.Exists(t_path);
            Output.Json(new
            {
                ok = t_ok,
                file = t_path,
                size_bytes = File.Exists(t_path) ? new FileInfo(t_path).Length : 0,
                transfer_errors = t_errors.Count > 0 ? t_errors : null,
                rebooted = true,
            });
            return t_ok ? Output.ExitOk : Output.ExitError;
        }

        /// <summary>Download an .ibf to the drive, reboot, wait for it to come back and read the new version.</summary>
        public static int Set(CliArgs args)
        {
            string t_ip = args.Ip;
            string t_source = Path.GetFullPath(args.Positional(0, ".ibf firmware file"));
            if (!File.Exists(t_source))
            {
                throw new UsageException($"Firmware file '{t_source}' not found.");
            }
            if (!t_source.EndsWith(".ibf", StringComparison.OrdinalIgnoreCase))
            {
                throw new UsageException($"'{t_source}' is not an .ibf firmware file.");
            }
            int t_waitSeconds = args.GetInt("wait-s", 300);

            string t_before;
            using (IEALConnection t_connection = Drive.Connect(t_ip))
            {
                t_before = t_connection.System.FirmwareManagement.GetFirmware();
                object t_plan = new
                {
                    drive = t_ip,
                    current_firmware = t_before,
                    new_firmware_file = t_source,
                    size_bytes = new FileInfo(t_source).Length,
                    steps = new[] { "switch axis to parameterization mode (drive disabled)", "download firmware", "reboot drive", $"wait up to {t_waitSeconds} s and read the new version" },
                };
                if (!args.Has("yes"))
                {
                    t_connection.Disconnect();
                    return Output.ConfirmationRequired(t_plan);
                }

                List<string> t_errors = Transfer(t_connection, () => t_connection.System.FirmwareManagement.SetFirmware(t_source));
                if (t_errors.Count > 0)
                {
                    Output.Json(new { ok = false, transfer_errors = t_errors, rebooted = false });
                    return Output.ExitError;
                }
                Output.Log("Rebooting drive ...");
                t_connection.System.Reboot();
            }

            string? t_after = WaitAndReadFirmware(t_ip, TimeSpan.FromSeconds(t_waitSeconds));
            Output.Json(new
            {
                ok = t_after != null,
                firmware_before = t_before,
                firmware_after = t_after,
                note = t_after == null ? $"Drive did not answer within {t_waitSeconds} s after reboot; check it with 'status'." : null,
            });
            return t_after != null ? Output.ExitOk : Output.ExitError;
        }

        private static List<string> Transfer(IEALConnection connection, Action transfer)
        {
            IFirmwareManagement t_firmware = connection.System.FirmwareManagement;
            List<string> t_errors = [];
            int t_lastPercent = -1;
            OnTransferEventHandler t_onTransfer = (sender, e) =>
            {
                int t_percent = e.BytesTotal > 0 ? (int)(e.BytesTransferred * 100 / e.BytesTotal) : 0;
                if (t_percent / 5 != t_lastPercent / 5)
                {
                    t_lastPercent = t_percent;
                    Output.Log($"  {t_percent}% ({e.BytesTransferred}/{e.BytesTotal} bytes)");
                }
            };
            OnErrorEventHandler t_onError = (sender, e) => t_errors.Add($"{e.ErrorCode}: {e.ErrorMessage}");
            OnFinishEventHandler t_onFinish = (sender, e) => Output.Log($"  finished, {e.BytesTransferred} bytes");
            t_firmware.OnTransfer += t_onTransfer;
            t_firmware.OnError += t_onError;
            t_firmware.OnFinish += t_onFinish;
            try
            {
                Drive.EnterParameterization(connection);
                transfer();
            }
            catch (FwMngmtEALException ex)
            {
                t_errors.Add(ex.Message);
            }
            finally
            {
                t_firmware.OnTransfer -= t_onTransfer;
                t_firmware.OnError -= t_onError;
                t_firmware.OnFinish -= t_onFinish;
            }
            return t_errors;
        }

        private static string? WaitAndReadFirmware(string ip, TimeSpan timeout)
        {
            Stopwatch t_timer = Stopwatch.StartNew();
            // Give the drive time to actually go down before polling.
            Thread.Sleep(TimeSpan.FromSeconds(10));
            using PingEALConnection t_ping = new();
            while (t_timer.Elapsed < timeout)
            {
                if (t_ping.Send(ip, 2000).EALStatus == EALIPStatus.Success)
                {
                    try
                    {
                        using IEALConnection t_connection = Drive.Connect(ip);
                        string t_version = t_connection.System.FirmwareManagement.GetFirmware();
                        t_connection.Disconnect();
                        return t_version;
                    }
                    catch (Exception ex)
                    {
                        Output.Log($"  drive answers ping but not ready yet: {ex.Message}");
                    }
                }
                Thread.Sleep(TimeSpan.FromSeconds(5));
            }
            return null;
        }
    }
}
