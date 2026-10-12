# EAL on ctrlX DRIVE: verified behaviour

Observed with EAL 02VRS (netstandard2.0 build, `$EAL_SDK/eal4DotNetStandard20/bin`) against a
ctrlX DRIVE XCS2 running FWA-XD1-AXS-V-0610N-NN-00, 2026-10-12. Library API docs: `$EAL_SDK/eal4DotNetStandard20/doc/html/`
(use the repo's `library-reference` skill to look things up).

## Calls that throw `NotImplementedException` on ctrlX DRIVE

`ISystem.GetCurrentMode`, `GetHardwareDetails`, `GetTemperature`, `GetCpuLoad`, `GetNewestDiagnosisIndex`,
`GetOldestDiagnosisIndex`, `GetVersionInfo`, and `IEALConnection.GetServerCoreVersion`.
`GetDiagnosisLog` throws `NotSupportedEALException` (S-0-0375 not supported).

## Calls that work

| Area | Calls |
|---|---|
| System | `GetName`, `GetSerialNumber`, `GetIpAddress`, `GetIPSuite`, `GetMacAddress`, `GetOperationHours`, `GetDisplayedDiagnosis`, `Reboot` |
| Firmware | `FirmwareManagement.GetFirmware`, `GetFirmwareFilename` (others not exercised yet) |
| Axis 0 | `GetCondition`, `GetName`, `GetDiagnosisNumber`, `GetDiagnosisText`, `IsError`, `Firmware`, `ApplicationType`, `GetActualPosition`, `GetState`, `SelectedOperatingMode` |
| Scope | `State`, `Mode`, `IsStarted`, `GetSignalSelectionList` (322 signals), `Start`, `Stop`, `RaiseTrigger`, channel `GetDataArrayDouble` |
| Ping | `PingEALConnection.Send` → `Success`, message types incl. `ReadEverything`, `WriteData` |
| Connection | `GetConnectedPort` → `MasterIP` |

## Bulk parameter access (use it whenever more than one IDN is involved)

`IParameter` has no multi-IDN `ReadData`/`WriteData`. The bulk path is:
- **read:** `SaveParameters(Stream, string[] idns)` into a `MemoryStream`, then parse the SERCOS-ASCII result
  ([par-format.md](par-format.md)). 13 IDNs took ~0.6 s, versus ~1 s for 26 single calls.
- **write:** `LoadParameters(Stream, 0)` with a file built from the drive's own blocks. Errors come through
  `ErrorOnUpdate` as `{Parameter}: {ErrorCode}`, and the data is unchanged. Always read back to verify.

`SaveParameters(path, selection)` timings: Modified (95 params) ~2.5 s, Backup (528) ~9 s, All (1283) ~30 s.

## Write refusals and what they mean

- `ErrorOnUpdate` gives only `SERVICE_SPECIFIC`. A single `WriteData` on the same IDN throws
  `NotSupportedEALException` carrying the drive's text, for example
  *"Operation data is write protected, due to other settings"*.
- **An armed scope locks its configuration.** While `Oscilloscope.IsStarted`, P-0-0031/P-0-0032/... refuse
  writes. `drivetool scope stop` releases it, and `scope run` stops an armed scope itself.
- **Operating mode vs PM/CM:** `pm_or_cm` parameters (attribute `0110…`) refuse writes in OM. Whether one
  needs PM (`AXIS_CONDITION_ACTIVE_PARAMETERIZATION`) or CM (`AXIS_CONDITION_ACTIVE_CONFIGURATION`) is
  **not** in the attribute. **House rule: PM is the default.** DriveTool switches to PM automatically when a
  changed parameter is blocked in OM, then restores the previous condition. **CM only when the user
  explicitly asks** (`--mode cm`). If PM refuses, report it and ask; don't retry in CM on your own.
  The manual's `Class` field does tell: `A_CM` = CM only, `A_PM` = PM or CM (PDF p. 1849). Both switches
  via `IAxis.SetCondition` were verified on hardware on 2026-10-12.

## Scaling changes (done on the test drive, 2026-10-12)

- Rotary to linear load-side mm: S-0-0076 = `0000.0000.0100.0001` (linear, preferred, metric, load, absolute) and
  S-0-0160 = `0000.0000.0100.0001` (mm/s²). Gear S-0-0121/0122 = motor revs : load revs. Feed S-0-0123 mm/rev.
  Max travel S-0-0278 (absolute format) bounds the position-limit ranges (they became ±2000 mm).
- **Preferred linear velocity scaling only exists as mm/min.** Linear + preferred + seconds is rejected with
  "Invalid operation data". For **mm/s** use parameter scaling: S-0-0044 = `0000.0000.0110.1001` (write it
  first), S-0-0045 = 1, S-0-0046 = −6, giving LSB 0.001 mm/s (runtime manual, scaling chapter, Table 94 and
  Fig. 329, PDF pp. 530–532).
- Position limits: S-0-0049/S-0-0050, active only with S-0-0055 bit 4. Write the limits before setting the
  bit, and check that the actual position lies inside them. S-0-0138 = 0 means no accel limit; S-0-0349 = 0
  means no jerk limit.
- Changing scaling, gear or feed re-interprets the actual position. The axis must be re-referenced afterwards.
- Change scaling first and limits second, as separate writes. Limit values are interpreted in the units
  active at write time.

## Torque limits (set on the test drive, 2026-10-12)

- Percentage scaling (S-0-0086 = 0): 100 % = S-0-0533, the nominal motor torque (MS2: from encoder memory;
  test motor 0.230 Nm). Reference: parameter reference p. 1076.
- Process limits S-0-0092 (bipolar), S-0-0082 (+) and S-0-0083 (−) are writable in OM (no mode switch).
  "Unlimited" reads 3276.7 / −3276.8 %, and the drive caps them at 343.5 % (≈ S-0-0109 peak current ÷
  S-0-0111 rated current). Verify through P-0-0444 (effective) and P-0-0442/0443.
- P-0-0109 (peak) is the absolute maximum and the **only** torque limit active during error reactions. Lowering
  it also weakens emergency braking, so ask before changing it.

## Procedure commands

`IParameter.Command(idn, timeout)` runs a command synchronously and deletes it afterwards (the command
parameter reads back 0). C0300 (S-0-0447) on 2026-10-12: S-0-0051 went 1089.2574 → −0.0001 mm, S-0-0403
became `0011` (homed), S-0-0175 recorded the shift −1089.2575 mm. F2174 had already cleared after the CM
round-trips; A0011 "STO aktiv" (Safe Torque Off) doesn't block C0300.

## Backup selections

- `All` includes live values: S-0-0047 position command, S-0-1305 system time, S-0-1099 comm test, and others.
  Restoring an All file would write them back, so DriveTool refuses that unless `--allow-non-backup` is given.
- `Backup` is the restorable set. It diffs to zero against the drive it came from, and it's DriveTool's default.
  It includes S-0-0192 (516 IDNs) plus ~11 more.
- `Modified` is the parameters changed from defaults (95 on the test drive).

## Scope

- `Trigger.PreTrigger` takes a **percentage** (0–100). A sample count was rejected. Set it after `MemorySize`
  (it writes P-0-0032/P-0-0033).
- If the trigger fires before the pre-trigger buffer has filled, fewer pre-trigger samples are recorded: an
  immediate manual trigger gave 3320 samples instead of 4096. Compute pre-trigger samples as
  `total - P-0-0033` (values after the trigger) rather than from the percentage.
- Channels: P-0-0023/0024/0147/0148/0270..0273 select the signals, and P-0-0021/0022/0145/0146/0274..0277 hold
  the values. Trigger: P-0-0025 mask, P-0-0026 signal, P-0-0027 level, P-0-0030 edge.
- A signal trigger (`ValueAsString` level) is implemented but **not yet verified on hardware**.

## Discovery

`BrowseDevices` is SiP broadcast on a local subnet. A drive behind a router (such as the test drive, reached through
a host route; see references/test-drive.md) **isn't found** by `discover`; use `ping`/`status`
with its IP instead.
