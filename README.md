# ctrlx-drive-eng

An agent skill (`SKILL.md` format) for engineering Bosch Rexroth ctrlX DRIVE servo drives over Ethernet with the
Easy Automation Library (EAL). The skill instructions are in [SKILL.md](SKILL.md). DriveTool, the CLI it uses,
also works on its own.

## Install

Put this folder in the skills directory your agent loads skills from, under the folder name `ctrlx-drive-eng`.

## Requirements

- **EAL SDK** (the DLLs are not in this repo):
  - **Linux:** install Bosch Rexroth's `ealsdk` Debian package (tested with 2.6.0.0). It puts `EAL.dll` and
    `SiP.dll` in `/usr/local/eal_sdk/02VRS`, where DriveTool looks for them.
  - **Windows:** unzip `eal4DotNetStandard20_*.zip` (tested with 3.0.1.0) into `eal/` in this folder, so that
    `eal/eal4DotNetStandard20/bin/EAL.dll` exists. DriveTool finds it there and copies the support DLLs
    (libzmq, openssl, datalayer) next to the build. `eal/` and `*.zip` are gitignored.
  - Anywhere else: set `EAL_SDK` to the folder holding `eal4DotNetStandard20`, or pass `-p:EalBinDir=...`.
- **.NET 10 SDK** (`dotnet --list-sdks` should show 10.x).
- **Python 3** for the scripts (Windows: `winget install Python.Python.3.12`, then use `python` where the docs say
  `python3`). `scope_report.py --png` also needs matplotlib.
- **poppler-utils** (`pdftotext`) and **mupdf-tools** (`mutool`), only for converting the manuals. Without
  `mutool` (Windows), `pip install pymupdf` is used for the bookmarks; `pdftotext` is still needed.

## Build

Install the SDK, then build DriveTool from this folder.

Linux:
```bash
sudo apt install ./ealsdk_*.deb
dotnet build drivetool
```

Windows (PowerShell, .NET 10 SDK installed, e.g. `winget install Microsoft.DotNet.SDK.10`):
```powershell
Expand-Archive eal4DotNetStandard20_*.zip eal
dotnet build drivetool
```

If the build stops with `EAL.dll not found in …`, the SDK isn't where DriveTool looks (see above). After source
changes, `scripts/drivetool` rebuilds DriveTool by itself before running it.

## Run

```bash
T=<skill-dir>/scripts/drivetool
$T                           # usage and the list of commands
$T ping --ip <drive-ip>      # check the drive answers
export CTRLX_DRIVE_IP=<drive-ip>   # or pass --ip on every call
$T status
```

On Windows use `scripts\drivetool.ps1` in place of `scripts/drivetool` (same arguments, same auto-rebuild);
Git Bash can also run `scripts/drivetool`.

Every command prints JSON on stdout. Commands that write to the drive only print a plan unless `--yes` is
given (see SKILL.md). Output files (backups, scope captures) go to `output/` under the current directory.

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
| `scripts/drivetool`, `scripts/drivetool.ps1` | Wrappers (bash, PowerShell) that build DriveTool on first use or after source changes, then run it |
| `scripts/scope_report.py` | Scope trace analysis (stats, CSV, PNG) |
| `scripts/pdf_to_md.py` | Rexroth PDF → Markdown converter |
| `references/` | Lessons learned, EAL behaviour on ctrlX DRIVE, .par format, IDN index |

The values in `references/` were measured on a reference drive (ctrlX DRIVE compact XCS2-W0007F, firmware
FWA-XD1-AXS-V-0610N). `references/test-drive.md` holds your own drive's address, network route and axis setup.
It is local only and gitignored; SKILL.md tells the agent to create it on first use.
