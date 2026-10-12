# ctrlx-drive-eng

A Claude Code skill for engineering Bosch Rexroth ctrlX DRIVE servo drives over Ethernet with the Easy
Automation Library (EAL). The skill instructions are in [SKILL.md](SKILL.md).

## Install

Clone into your Claude Code skills folder:

```bash
git clone https://github.com/rduffy190/ctrlx-drive-eng-skill.git ~/.claude/skills/ctrlx-drive-eng
```

## Requirements

- **DriveTool**, the .NET 10 CLI the skill drives. It is **not** in this repo. `scripts/drivetool` expects it
  at `~/Dev/EalDotNet/src/DriveTool`; set `DRIVETOOL_PROJECT` to use another path.
- .NET 10 SDK, Python 3, poppler-utils (`pdftotext`) and mupdf-tools (`mutool`).

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
| `scripts/drivetool` | Wrapper that builds and runs DriveTool |
| `scripts/scope_report.py` | Scope trace analysis (stats, CSV, PNG) |
| `scripts/pdf_to_md.py` | Rexroth PDF → Markdown converter |
| `references/` | Lessons learned, EAL behaviour on ctrlX DRIVE, .par format, IDN index, test-drive setup |
