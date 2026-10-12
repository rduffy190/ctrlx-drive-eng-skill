using System.Diagnostics;
using System.Text.Json;
using EAL.Interfaces;
using EAL.Interfaces.Oscilloscope;

namespace DriveTool.Commands
{
    /// <summary>
    /// Drive oscilloscope. Flow from
    /// EAL SDK doc/html/interface_e_a_l_1_1_interfaces_1_1_oscilloscope_1_1_i_oscilloscope.html (RaiseTrigger sample)
    /// and EAL SDK doc/html/fundamentals05_05_06.html (signal trigger).
    /// </summary>
    public static class ScopeCommands
    {
        private const int _statePollIntervalMs = 200;

        /// <summary>List the IDNs the scope can record (read-only).</summary>
        public static int Signals(CliArgs args)
        {
            using IEALConnection t_connection = Drive.Connect(args.Ip);
            string[] t_signals = t_connection.Oscilloscope.GetSignalSelectionList() ?? [];
            t_connection.Disconnect();
            string? t_filter = args.Positionals.FirstOrDefault();
            Output.Json(new
            {
                count = t_signals.Length,
                signals = t_filter == null ? t_signals : t_signals.Where(t_s => t_s.Contains(t_filter, StringComparison.OrdinalIgnoreCase)),
            });
            return Output.ExitOk;
        }

        /// <summary>Stop an armed scope (frees its configuration parameters for writing).</summary>
        public static int Stop(CliArgs args)
        {
            using IEALConnection t_connection = Drive.Connect(args.Ip);
            IOscilloscope t_scope = t_connection.Oscilloscope;
            bool t_wasStarted = t_scope.IsStarted;
            State t_before = t_scope.State;
            if (t_wasStarted && !args.Has("yes"))
            {
                t_connection.Disconnect();
                return Output.ConfirmationRequired(new { drive = args.Ip, scope_state = t_before, action = "stop the armed oscilloscope (discards a pending recording)" });
            }
            if (t_wasStarted)
            {
                t_scope.Stop();
            }
            Output.Json(new { was_started = t_wasStarted, state_before = t_before, state_after = t_scope.State, is_started = t_scope.IsStarted });
            t_connection.Disconnect();
            return Output.ExitOk;
        }

        /// <summary>Configure, arm, trigger, read channels, write a JSON result. Configuring writes the scope parameters listed in the plan.</summary>
        public static async Task<int> Run(CliArgs args)
        {
            string t_configPath = args.Get("config") ?? Path.Combine(AppContext.BaseDirectory, "scopeConfig.json");
            ScopeConfig t_config = ScopeConfig.Load(t_configPath);
            string t_ip = args.Ip;
            object t_plan = new
            {
                drive = t_ip,
                config = Path.GetFullPath(t_configPath),
                signals = t_config.Signals,
                time_resolution_us = t_config.TimeResolutionUs,
                memory_size = t_config.MemorySize,
                pre_trigger_percent = t_config.PreTriggerPercent,
                trigger = t_config.Trigger,
                recording_ms = (double)t_config.TimeResolutionUs * t_config.MemorySize / 1000.0,
                writes = "oscilloscope configuration only: channel signal selection (P-0-0023/0024/0147/0148/0270..0273), P-0-0031 time resolution, P-0-0032 memory size, P-0-0033 values after trigger, trigger P-0-0025 mask / P-0-0026 signal / P-0-0027 level / P-0-0030 edge",
            };
            if (!args.Has("yes"))
            {
                return Output.ConfirmationRequired(t_plan);
            }

            using IEALConnection t_connection = Drive.Connect(t_ip);
            IOscilloscope t_scope = t_connection.Oscilloscope;
            // An armed scope write-protects its configuration ("write protected, due to other settings").
            if (t_scope.IsStarted)
            {
                Output.Log("Scope is armed from an earlier run; stopping it before reconfiguring ...");
                t_scope.Stop();
            }

            t_scope.TimeResolution = t_config.TimeResolutionUs;
            t_scope.MemorySize = t_config.MemorySize;
            for (int t_i = 0; t_i < t_config.Signals.Count; t_i++)
            {
                t_scope.Channels[t_i].Signal = t_config.Signals[t_i];
            }
            ScopeTriggerConfig t_trigger = t_config.Trigger;
            t_scope.Trigger.Type = t_trigger.Type switch
            {
                "signal" => TriggerType.SignalTrigger,
                "auto" => TriggerType.AutomaticTrigger,
                _ => TriggerType.ManualTrigger,
            };
            if (t_trigger.Type == "signal")
            {
                t_scope.Trigger.Mask = t_trigger.Mask;
                t_scope.Trigger.Signal = t_trigger.Signal!;
                t_scope.Trigger.ValueAsString = t_trigger.Value!;
                t_scope.Trigger.EdgeType = t_trigger.Edge switch
                {
                    "falling" => TriggerEdgeType.FallingEdge,
                    "both" => TriggerEdgeType.BothEdges,
                    "equal" => TriggerEdgeType.EqualTo,
                    _ => TriggerEdgeType.RisingEdge,
                };
            }
            // Set after MemorySize: per the EAL docs PreTrigger writes P-0-0032 and P-0-0033.
            t_scope.Trigger.PreTrigger = t_config.PreTriggerPercent;

            t_scope.Start();
            Output.Log($"Scope armed ({t_trigger.Type} trigger).");
            DateTimeOffset t_armedAt = DateTimeOffset.Now;
            if (t_trigger.Type == "manual")
            {
                int t_delayMs = t_trigger.ManualDelayMs ?? t_config.PreTriggerFillMs;
                if (t_delayMs < 0)
                {
                    Output.Log("Press Enter to trigger ...");
                    Console.ReadLine();
                }
                else if (t_delayMs > 0)
                {
                    Output.Log($"Triggering in {t_delayMs} ms ...");
                    await Task.Delay(t_delayMs);
                }
                t_scope.RaiseTrigger();
                Output.Log("Triggered.");
            }

            await WaitForReadyAsync(t_scope, t_config.MeasurementTimeoutMs);
            DateTimeOffset t_readyAt = DateTimeOffset.Now;
            // P-0-0033 = number of values recorded after the trigger; everything before it is pre-trigger.
            int t_postTriggerSamples = Convert.ToInt32(t_connection.Parameter.ReadData("P-0-0033"));

            List<object> t_channels = [];
            List<double[]> t_data = [];
            for (int t_i = 0; t_i < t_config.Signals.Count; t_i++)
            {
                double[] t_values = t_scope.Channels[t_i].GetDataArrayDouble();
                t_data.Add(t_values);
                t_channels.Add(new { index = t_i + 1, signal = t_config.Signals[t_i], values = t_values });
            }
            t_connection.Disconnect();

            int t_sampleCount = t_data.Max(t_d => t_d.Length);
            int t_preTriggerSamples = Math.Max(0, t_sampleCount - t_postTriggerSamples);
            List<double> t_timeUs = Enumerable.Range(0, t_sampleCount)
                .Select(t_i => (double)(t_i - t_preTriggerSamples) * t_config.TimeResolutionUs)
                .ToList();
            object t_result = new
            {
                armed_at = t_armedAt,
                ready_at = t_readyAt,
                drive_ip_address = t_ip,
                time_resolution_us = t_config.TimeResolutionUs,
                memory_size = t_config.MemorySize,
                pre_trigger_percent = t_config.PreTriggerPercent,
                pre_trigger_samples_requested = t_config.PreTriggerSamples,
                pre_trigger_samples = t_preTriggerSamples,
                post_trigger_samples = t_postTriggerSamples,
                trigger = t_trigger,
                time_us = t_timeUs,
                channels = t_channels,
            };

            string t_directory = Path.GetFullPath(args.Get("out") ?? t_config.OutputDirectory);
            Directory.CreateDirectory(t_directory);
            string t_path = Path.Combine(t_directory, $"scope_{t_armedAt:yyyyMMdd_HHmmss}.json");
            await using (FileStream t_stream = File.Create(t_path))
            {
                await JsonSerializer.SerializeAsync(t_stream, t_result, Output.JsonOptions);
            }

            Output.Json(new
            {
                file = t_path,
                samples = t_sampleCount,
                pre_trigger_samples = t_preTriggerSamples,
                note = t_preTriggerSamples < t_config.PreTriggerSamples
                    ? $"Trigger came before the pre-trigger buffer was full: {t_preTriggerSamples} of {t_config.PreTriggerSamples} pre-trigger samples."
                    : null,
                channels = t_config.Signals.Select((t_signal, t_i) => new
                {
                    signal = t_signal,
                    min = t_data[t_i].Length > 0 ? t_data[t_i].Min() : (double?)null,
                    max = t_data[t_i].Length > 0 ? t_data[t_i].Max() : (double?)null,
                    mean = t_data[t_i].Length > 0 ? t_data[t_i].Average() : (double?)null,
                }),
            });
            return Output.ExitOk;
        }

        private static async Task WaitForReadyAsync(IOscilloscope scope, int timeoutMs)
        {
            Stopwatch t_timer = Stopwatch.StartNew();
            State t_last = State.None;
            while (true)
            {
                State t_state = scope.State;
                if (t_state != t_last)
                {
                    Output.Log($"  scope state: {t_state}");
                    t_last = t_state;
                }
                if (t_state == State.Ready)
                {
                    // The doc sample pauses briefly after Ready before reading data.
                    await Task.Delay(_statePollIntervalMs);
                    return;
                }
                if (t_state == State.Error)
                {
                    throw new InvalidOperationException("Oscilloscope reported State.Error.");
                }
                if (t_timer.ElapsedMilliseconds > timeoutMs)
                {
                    throw new TimeoutException($"Scope did not reach Ready within {timeoutMs} ms (last state: {t_state}).");
                }
                await Task.Delay(_statePollIntervalMs);
            }
        }
    }
}
