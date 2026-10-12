---
name: ctrlx-drive-eng
description: Engineer Bosch Rexroth ctrlX DRIVE servo drives over Ethernet with the Easy Automation Library (EAL), using the DriveTool CLI that ships with this skill and extending it with new tools. Use it to check status and diagnostics, read or write drive parameters (SERCOS IDNs like S-0-0040), set up an axis (scaling and units, gear ratio, feed/pitch, travel and position limits, velocity/accel/jerk/torque limits), home or set absolute position, run drive commands, back up, diff or restore .par files, record and analyse scope traces, and update .ibf firmware. Can use the Rexroth parameter reference and function/hardware manuals as greppable Markdown. Use it whenever the user mentions a ctrlX DRIVE, servo drive, IDNs, scaling, homing, limits, drive errors (F-codes), scope traces, .par/.ibf files, PM/CM/OM modes, or wants a new drive tool on EAL, even if they don't name EAL.
---

# ctrlX DRIVE engineering with EAL

The tooling is **DriveTool**, a .NET 10 CLI on EAL in this skill's `drivetool/` folder. It talks to the drive
directly (SiP over Ethernet), not through a ctrlX CORE. For the CORE's Data Layer, use the `ctrlx-rest` skill.

```bash
T=~/.claude/skills/ctrlx-drive-eng/scripts/drivetool    # builds on first use / after source changes, then runs
export CTRLX_DRIVE_IP=<drive-ip>                       # or pass --ip; the test drive's address is in references/test-drive.md (local only)
```

### First use on a new machine
The repo ships the DriveTool source but no EAL binaries, manuals or drive details. Check these before the first
drive call:
- **Build:** the first `$T` call builds DriveTool. If it fails, tell the user what to install and let them run it
  (it needs sudo): `sudo apt install ./ealsdk_*.deb` (Bosch Rexroth EAL SDK; `EAL.dll not found` means it's
  missing) and the .NET 10 SDK (`dotnet --list-sdks`). Then `dotnet build ~/.claude/skills/ctrlx-drive-eng/drivetool`.
- **Drive address:** if `references/test-drive.md` doesn't exist, ask the user for the drive's IP, check it with
  `$T ping --ip <ip>`, then create the file (address, network route if any, type plate from `$T status`). It is
  gitignored, so it stays on this machine.
- **Manuals:** if `manuals/` holds only README.md, the Markdown books aren't converted yet. Say so before answering
  from memory, and offer to convert the user's Rexroth PDFs with `scripts/pdf_to_md.py` (commands in README.md).

Every command prints **JSON on stdout** and progress on **stderr**. Exit codes: `0` ok, `1` error, `2` usage,
`3` = *a write was planned but not executed* (see Safety).

## Commands

| Read-only (run freely) | |
|---|---|
| `$T ping` | EAL ping: reachable? supported services |
| `$T status` | identity, type plate, axis condition, diagnosis, live velocity/position/torque |
| `$T param read S-0-40 P-0-31 …` or `@ids.txt` | **bulk** read: value, unit, min/max, type, `writable_in` |
| `$T param diff file.par` | drive vs file; only changed, writable params (`--include read-only` for all) |
| `$T par show file.par [IDN…]` | decode a .par offline |
| `$T backup [--selection backup\|modified\|all] [--out f.par]` | save parameters (default `backup`, the restorable set) |
| `$T firmware info` | firmware version and .ibf file name |
| `$T scope signals [filter]` | IDNs the scope can record |
| `$T discover` | SiP broadcast on local subnets (won't see routed drives) |

| Writes (plan first; executed only with `--yes`) | |
|---|---|
| `$T param write S-0-91=1500 P-0-31=500 …` or `@values.txt` | **bulk** write in one call, then bulk read-back verify |
| `$T restore f.par [--set 0]` | load a parameter file (refuses non-Backup params without `--allow-non-backup`) |
| `$T scope run [--config cfg.json] [--out dir]` | configure scope, arm, trigger, record → `output/scope_*.json` |
| `$T scope stop` | stop an armed scope (it locks the scope config params) |
| `$T firmware get [--out dir]` | upload .ibf from the drive: PM, upload, **reboot** |
| `$T firmware set f.ibf [--wait-s 300]` | download firmware: PM, transfer, **reboot**, wait, verify version |
| `$T command S-0-447 [--timeout-ms 30000]` | run a procedure command (checks it is one); diagnosis before/after |

Values: decimals as the drive scales them (`1500` or `1500.0000`). Binary as `0b0000.0011`, `0x03` or `3`.
Text as-is. Lists comma-separated (`S-0-0016=S-0-0144,S-0-0386`). IDNs may be short (`S-0-40` → `S-0-0040.0.0`).

## Safety: plan → confirm → `--yes`

The drive can be wired to machinery. **Reads are safe. Every write needs the user's explicit OK.**

1. Run the write command **without** `--yes`. It reads the drive, validates (limits, decimals, read-only,
   list length, writability) and prints a plan with exit 3. Nothing is written.
2. Show the user the plan: each IDN with current → new value, and any mode switch or reboot.
3. Only after they confirm, re-run the same command with `--yes`. Report the read-back result
   (`written[].ok`), not just "done".

Never pass `--yes` on your own initiative, and never chain a write right after a plan in the same step.

### PM / CM / OM: which mode a write happens in

`writable_in` comes from the attribute bits: `always` · `pm_or_cm` (refused in operating mode) · `never`.
The attribute **can't tell PM from CM**. The rule:

- **Default is PM.** When a changed parameter is `pm_or_cm` and the axis is in OM, the plan includes
  switching to PM (`AXIS_CONDITION_ACTIVE_PARAMETERIZATION`) and back. The drive is **disabled** while in
  PM, so say so when presenting the plan.
- **CM only when the user explicitly asks** (`--mode cm`). If PM refuses a write, report it and ask. Don't
  retry in CM yourself.
- `--mode current` never switches.
- **The manual says which mode applies.** Look up each parameter's `Class:` in the object table
  (parameter reference; Class legend on PDF p. 1849). `A_CM` = editable **only in CM**. `A_PM` = PM or CM.
  `A`/`A_CIN`/`O…` = always editable. Scaling, gear, feed constant and max travel range (S-0-0076/0044/0160/
  0121/0122/0123/0278) are all `A_CM`, so PM refuses them. When a plan contains `A_CM` parameters, tell the user
  CM is needed and get their explicit OK for `--mode cm`.

### Write refused?

**First grep [references/lessons-learned.md](references/lessons-learned.md)**, the log of past failures and fixes
(mm/s scaling, A_CM parameters, armed scope, impossible speeds, …).

`load_errors` show `SERVICE_SPECIFIC` and the read-back shows the old value; nothing changed. Usual causes:
an armed scope (scope params locked → `scope stop`), the wrong mode (see above), or a value invalid for the
drive's configuration. A single-IDN `WriteData` returns the drive's own text.

## Workflows

**Health check:** `ping`, then `status`. Diagnosis texts are in the drive's language (e.g. German
*Referenzverlust Geber 1* = encoder 1 reference lost). `is_error`/`diagnosis_number` give the F-code.

**Change parameters:** find IDNs with `grep -i '<word>' references/idn-index.tsv` (1283 IDNs from the test
drive: idn, writable_in, type, unit, name; grep it, never read it whole). Look each one up in the manual
(meaning, default, **Class** for the mode). `param read` the candidates for current value, unit and limits.
Then `param write` (plan), get the OK, write with `--yes`. Take a `backup` first for anything beyond a
trivial tweak.

**Axis setup (scaling, gear, feed, travel, limits)**, done once on the test drive; details in
[references/lessons-learned.md](references/lessons-learned.md):
1. Feasibility first: load speed max = motor max (S-0-0113) ÷ gear × feed. Ask before writing an unreachable limit.
2. **Step 1, CM** (all `A_CM`): S-0-0076/0044/0160 scaling types, S-0-0121/0122 gear, S-0-0123 feed.
   Linear mm/min, mm and mm/s² are presets. **mm/s needs parameter scaling** (S-0-0044 `0000.0000.0110.1001`,
   S-0-0045 = 1, S-0-0046 = −6).
3. Re-read: the units and decimals of every limit have changed.
4. **Step 2, CM:** S-0-0278 max travel, S-0-0050/0049 position limits, S-0-0091 velocity limit, and S-0-0055
   bit 4 (limits on) **last**. Accel S-0-0138 and jerk S-0-0349: 0 = no limit.
5. Re-reference the position afterwards (see Home). Take `backup` before and after.

**Torque limits:** S-0-0092 (bipolar), S-0-0082 (+), S-0-0083 (−) in % of S-0-0533 nominal torque; all
writable in OM. P-0-0109 is the peak limit and the only one active during error reactions. Ask before
lowering it. Verify through P-0-0444 (effective).

**Backup / compare / restore:** `backup` (default Backup selection) → later `param diff f.par` shows drift →
`restore f.par` (plan lists every change and any mode switch) → `--yes` → `param diff` again to confirm.
Don't restore `--selection all` files. They carry live values (position command S-0-0047, system time), and
restore refuses them for that reason.

**Scope capture:** copy `drivetool/scopeConfig.json`, then set `signals` (≤8, check with
`scope signals`), `time_resolution_us`, `memory_size` and `pre_trigger_percent`. Trigger `manual` (default:
fires once the pre-trigger buffer is full), `signal` (signal/value/edge; not yet hardware-verified) or `auto`.
`scope run` plans, then `--yes` runs it. Analyse with:
```bash
python3 -I ~/.claude/skills/ctrlx-drive-eng/scripts/scope_report.py output/scope_X.json [--csv f.csv] [--png f.png]
```
It gives per-channel min/max (with time), peak-to-peak, mean, std, rms and value at trigger. Look at the PNG
before describing a trace.

**Home / set position (absolute encoder):** the axis is at the reference spot → `param read S-0-448 S-0-52
S-0-403` (encoder selected, home offset = the value wanted there) → `command S-0-447` (C0300 Set absolute
position) → verify S-0-0051 ≈ offset and S-0-0403 bits set. With the drive *not* enabled it applies at once;
in AF it needs S-0-0448 bit 2 and halt/disable (runtime manual 16.2.4, PDF pp. 545–548). Errors: `command
S-0-99` (C0500 reset).

**Firmware:** `firmware info` → `firmware get` (backup of the current .ibf) → `firmware set new.ibf`. Both
transfers reboot the drive, so take a parameter `backup` before `set`.

## Building new tools

Add a command to DriveTool rather than writing one-off programs:

1. Look up the EAL API in the EAL SDK: `$EAL_SDK/eal4DotNetStandard20/doc/html` (API reference) and
   `$EAL_SDK/eal4DotNetStandard20/samples` (default `EAL_SDK=/usr/local/eal_sdk/02VRS`). **Never invent EAL names.** Some documented calls throw `NotImplementedException` on ctrlX DRIVE;
   check [references/eal-on-ctrlx-drive.md](references/eal-on-ctrlx-drive.md) first, and probe new calls
   read-only in a try/catch.
2. More than one parameter → bulk only: `Drive.ReadBlocks` (SaveParameters to a stream) and `Drive.LoadBlocks`
   (LoadParameters from a stream built from the drive's own blocks via `ParValueFormatter`). Format in
   [references/par-format.md](references/par-format.md).
3. Writes follow the plan/`--yes` pattern (`Output.ConfirmationRequired`), mode via `Drive.ResolveMode`,
   read-back verify. Results go through `Output.Json` (snake_case).
4. Add the file under `Commands/`, add a case to `Program.cs` and a line to its usage text. Match the repo
   style (`_` fields, `t_` locals, block namespaces), then `dotnet build` and `dotnet format`.
5. Test read-only paths live; get the user's OK before the first live write. Then add the command to the
   tables above.

## Manuals: look here before guessing drive behaviour

`manuals/` holds the four Rexroth books converted to Markdown, about 10 MB. **Grep them; never read whole
files.** Start from [manuals/README.md](manuals/README.md).
- What a parameter means: `grep -P '^S-0-0091\t' manuals/parameters-axs-v-reference/params.tsv` gives the
  file and PDF page. Then `grep -n '^### S-0-0091 '` in that file and read the section with its object table.
- How a function works (homing, scaling, limits, modes, scope, error reactions):
  `manuals/runtime-axs-v-06-functions/index.md`.
- Diagnosis codes: `grep -rn 'F2174' manuals/`.
- Test drive hardware (XCS2-W0007F): `manuals/xcs2-compact-hw/`. Other device families:
  `manuals/modular-drive-systems-hw/`.
- Figures aren't in the text. Render the PDF page (`mutool draw`, see the README) and look at it.

Cite the manual and PDF page when an answer depends on it. Write plans should mention the parameter's
documented meaning and default, not just its name.

## Reference

- [references/lessons-learned.md](references/lessons-learned.md): failures hit so far, their causes and fixes.
- [references/eal-on-ctrlx-drive.md](references/eal-on-ctrlx-drive.md): what works or throws, timings, write
  refusals, backup selections, scope quirks.
- [references/par-format.md](references/par-format.md): SERCOS-ASCII .par layout and attribute bits.
- `references/idn-index.tsv`: IDN → name/unit/type/writability for the test drive (FWA-XD1-AXS-V-0610N),
  regenerated after the linear rescale.
- [references/test-drive.md](references/test-drive.md): the test drive's address, hardware and current axis setup.
- `manuals/`: Rexroth parameter reference, runtime functions manual and two hardware manuals, as Markdown.
- `scripts/pdf_to_md.py`: converts further Rexroth PDFs the same way.

**Verified on hardware (2026-10-12, XCS2, FWA-XD1-AXS-V-0610N):** ping, status, bulk read, bulk write with
read-back, PM and CM switching for writes, diff, backup (all three selections), restore *plan*, firmware
info, scope signals/stop/run (manual trigger), `command` (C0300 set absolute position), and the full axis
setup and torque limits above.
**Not yet run on hardware:** the `restore --yes` load, the scope signal trigger, `firmware get`/`set`, C0500
via `command`, and discover finding a device (the test drive is routed).
