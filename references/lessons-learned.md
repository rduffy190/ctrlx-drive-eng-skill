# Lessons learned: failures and their causes

Each entry gives the symptom, the cause, the fix and the source. Grep here first when a drive write or a tool
step fails. All entries are from the XCS2 test drive (FWA-XD1-AXS-V-0610N) on 2026-10-12, unless noted.

## Writing parameters

### Velocity in mm/s: "min" had to become "s", and the preset couldn't do it
- **Goal:** linear velocity in mm/s, i.e. the time unit changed from minutes to seconds (S-0-0044 bit 5 = 1).
- **Symptom:** S-0-0044 = `0000.0000.0110.0001` (linear, *preferred*, mm, **s**, load) was refused in CM.
  `SERVICE_SPECIFIC` in bulk; single `WriteData` said **"Invalid operation data"**. Writing it alone didn't help.
- **Cause:** preferred (preset) linear velocity scaling exists **only as mm/min** (runtime manual Table 94, PDF
  p. 530). Rotary scaling offers rps, linear doesn't. Linear + preferred + seconds is an illegal combination.
- **Fix:** switch velocity to **parameter scaling** (bit 3 = 1) and set the factor/exponent yourself:
  `S-0-0044=0b0000.0000.0110.1001  S-0-0045=1  S-0-0046=-6` (S-0-0044 first in the file), giving
  LSB = 1 m/s × 1 × 10⁻⁶ = **0.001 mm/s** (formula: runtime manual Fig. 329, PDF p. 532). After the write,
  S-0-0040/S-0-0091 showed unit `mm/s` with 3 decimals.
- Acceleration needed no such change: preferred linear acceleration is already mm/s² (S-0-0160 = `…0100.0001`).

### Scaling, gear and feed refused in PM
- **Symptom:** S-0-0076/0044/0160/0121/0123 all refused (`SERVICE_SPECIFIC`) after switching to PM.
- **Cause:** these are Class **`A_CM`** = editable only in CM. The attribute bits only say "not in OM" and can't
  tell PM from CM; the manual's Class field can (Class legend: parameter reference PDF p. 1849).
- **Fix:** look up `Class:` under the parameter's `## Object` table in `manuals/parameters-axs-v-reference`
  **before planning**. For `A_CM`, tell the user CM is needed and get an explicit OK for `--mode cm`.
  The default stays PM; never fall back to CM silently.
- `A_CM` here: S-0-0076, S-0-0044, S-0-0160, S-0-0121, S-0-0122, S-0-0123, S-0-0278. S-0-0049/0050 are
  `O_CINOUT` and S-0-0091 is `A_CIN` (always writable).

### Requested speed physically impossible
- **Symptom (caught in planning):** 2 m/s requested with a 10:1 gear and 25 mm pitch.
- **Cause:** motor max 9000 rpm (S-0-0113) ÷ 10 × 25 mm = 375 mm/s. S-0-0091's maximum is derived from
  S-0-0113 (drive reported max 379.166 mm/s after rescaling).
- **Fix:** always compute load speed = motor max ÷ gear × feed before writing a velocity limit, and ask the user.

### Values written in the wrong units
- **Risk:** S-0-0049/0050/0278/0091 are interpreted in the scaling active **at write time**.
- **Fix:** change scaling, gear and feed first, re-read (units and decimals change), then write limits as a
  second step. Enable the position limits (S-0-0055 bit 4) **last**, after the limit values are written and
  the actual position is confirmed to be inside them. Otherwise the drive trips a travel-range error at once.

### Position meaning changes after rescaling
- Changing scaling, gear or feed re-interprets the actual position (12853° became 1089.2574 mm). The axis needs
  homing / set absolute position afterwards. The test drive also had F2174 (encoder 1 reference lost),
  documented in runtime manual chapter "Establishing the position data reference".

### Scope parameters refused
- **Symptom:** P-0-0031/P-0-0032 refused, `SERVICE_SPECIFIC`; single `WriteData` said "Operation data is
  write protected, due to other settings".
- **Cause:** the oscilloscope was left armed (`IsStarted`) by an earlier capture; an armed scope locks its
  configuration.
- **Fix:** `drivetool scope stop` first (`scope run` now does this itself).

### `SERVICE_SPECIFIC` tells you nothing
- Bulk `LoadParameters` errors only carry `SERVICE_SPECIFIC`. To get the drive's reason, repeat **one** approved
  write with `IParameter.WriteData` (single IDN, in the same mode). Its `NotSupportedEALException` message
  holds the drive's text. The scratch-probe pattern: switch condition, `WriteData`, print exception, restore.

## Reading / tooling

### Unsupported IDNs crashed the bulk-read parser
- `SaveParameters(stream, idns)` doesn't raise an error for a missing IDN. It writes a 6-line stub (empty
  name, attribute 0, no data). The parser now lists these as `missing`.

### IDN numbers above 4095 silently become another parameter
- EAL wraps the 12-bit parameter number: `S-0-9999` was sent as `S-2-1807`. DriveTool rejects numbers above 4095.

### Rebuilt .par files didn't match the drive's
- The drive writes `|` **between** blocks with none after the last, and ends the file with `\n`. Re-emitting a
  trailing `|`, or treating the final newline as an empty data line, broke the byte-for-byte round trip.
  `drivetool par show F` reports `round_trips`.

### "All" backups are not safe to restore
- `--selection all` contains live values (S-0-0047 position command, S-0-1305 system time). Restore refuses
  them unless `--allow-non-backup` is given. Use the default Backup selection.

### Scope capture short / time axis shifted
- A manual trigger right after arming recorded 3320 instead of 4096 samples: the pre-trigger buffer wasn't
  full. Fixes: wait about the pre-trigger time before `RaiseTrigger` (the default now), and compute pre-trigger
  samples as `total - P-0-0033`.

### EAL calls that just throw on ctrlX DRIVE
- `GetCurrentMode`, `GetTemperature`, `GetCpuLoad`, `GetHardwareDetails`, the diagnosis index/log calls,
  `GetVersionInfo` and `GetServerCoreVersion` throw. See [eal-on-ctrlx-drive.md](eal-on-ctrlx-drive.md).

### Drive unreachable
- The test drive needs a host route:
  `sudo ip route add <drive-ip>/32 via <gateway> dev <iface>` (actual values in references/test-drive.md). It's lost on reboot; the user must run it,
  since sudo is denied in this project. `discover` never finds it (routed, not broadcast-reachable).
