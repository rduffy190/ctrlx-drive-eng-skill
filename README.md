# ctrlx-drive-eng

A Claude Code skill for engineering Bosch Rexroth ctrlX DRIVE servo drives over Ethernet with the Easy
Automation Library (EAL). The skill instructions are in [SKILL.md](SKILL.md).

## Install

Clone into your Claude Code skills folder:

```bash
git clone https://github.com/rduffy190/ctrlx-drive-eng-skill.git ~/.claude/skills/ctrlx-drive-eng
```

## Requirements

- The **Easy Automation Library (EAL) SDK** from Bosch Rexroth, installed locally (the DLLs are not in this
  repo). DriveTool builds against its netstandard2.0 `EAL.dll` and `SiP.dll`. The default SDK root is
  `/usr/local/eal_sdk/02VRS`; elsewhere, set `export EAL_SDK=<folder holding eal4DotNetStandard20>`. The build
  stops with a clear message if the DLLs aren't found. The SDK's `doc/html` and `samples` are the API reference
  the skill uses when extending DriveTool.
- .NET 10 SDK (`dotnet --list-sdks` should show 10.x).
- Python 3 for the scripts. `scope_report.py --png` also needs matplotlib.
- poppler-utils (`pdftotext`) and mupdf-tools (`mutool`), only for converting the manuals.

## Build

The wrapper builds DriveTool automatically on first use and again whenever a source file changes, so you
normally don't need to build by hand. To build or check it yourself:

```bash
cd ~/.claude/skills/ctrlx-drive-eng/drivetool
export EAL_SDK=/usr/local/eal_sdk/02VRS     # only if the SDK isn't at this default location
dotnet build                                # output: drivetool/bin/Debug/net10.0/drivetool.dll
```

Instead of `EAL_SDK` you can pass `-p:EalSdkDir=<sdk root>`, or `-p:EalBinDir=<folder with EAL.dll>`.
If the build says `EAL.dll not found in …`, the SDK path is wrong.

## Run

```bash
T=~/.claude/skills/ctrlx-drive-eng/scripts/drivetool
$T                           # usage and the list of commands
$T ping --ip <drive-ip>      # check the drive answers
export CTRLX_DRIVE_IP=<drive-ip>   # or pass --ip on every call
$T status
```

Every command prints JSON on stdout. Commands that write to the drive only print a plan unless `--yes` is
given (see SKILL.md). Output files (backups, scope captures) go to `output/` under the current directory.

Set `DRIVETOOL_PROJECT=<folder>` to make the wrapper use a DriveTool checkout somewhere else.

## Manuals (not included)

`manuals/` holds the Rexroth manuals converted to greppable Markdown, but this repo doesn't include them
because they are Bosch Rexroth's copyrighted documentation. Download the PDFs from Bosch Rexroth and convert
them with `scripts/pdf_to_md.py`. [manuals/README.md](manuals/README.md) lists the books, editions and the
folder each one goes in. The commands are:

```bash
python3 -I scripts/pdf_to_md.py "<runtime functions>.pdf" manuals/runtime-axs-v-06-functions --kind manual
python3 -I scripts/pdf_to_md.py "<parameter reference>.pdf" manuals/parameters-axs-v-reference --kind params
python3 -I scripts/pdf_to_md.py "<XCS2 compact>.pdf"        manuals/xcs2-compact-hw --kind manual
python3 -I scripts/pdf_to_md.py "<modular drive systems>.pdf" manuals/modular-drive-systems-hw --kind manual
```

## Contents

| Path | What |
|---|---|
| `SKILL.md` | Skill instructions: commands, safety rules (plan → confirm → `--yes`), workflows |
| `drivetool/` | DriveTool source: the EAL-based CLI (commands in `Commands/`, .par handling in `Par/`) |
| `scripts/drivetool` | Wrapper that builds DriveTool on first use or after source changes, then runs it |
| `scripts/scope_report.py` | Scope trace analysis (stats, CSV, PNG) |
| `scripts/pdf_to_md.py` | Rexroth PDF → Markdown converter |
| `references/` | Lessons learned, EAL behaviour on ctrlX DRIVE, .par format, IDN index |

`references/test-drive.md` (your drive's address, network route and axis setup) is local only and not
published. SKILL.md refers to it, so create your own: address, hardware, firmware, backups and axis setup.
