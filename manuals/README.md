# ctrlX DRIVE manuals (Markdown)

Generated from the Bosch Rexroth PDFs (download them from Bosch Rexroth) by `../scripts/pdf_to_md.py` (2026-10-12).
The text comes from `pdftotext -layout`, so tables keep their column alignment. Figures are **not** included:
`<!-- p.N -->` marks each PDF page, and `*PDF p. N*` follows each heading. To see a figure, render the page and
read the PNG: `mutool draw -o /tmp/p.png -r 110 "<pdf>" N`.

| Folder | Book | Use it for |
|---|---|---|
| [parameters-axs-v-reference/](parameters-axs-v-reference/index.md) | Objects/Parameters of Runtime AXS-V-xxRS, Reference Book (R911425449 Ed. 02, 1864 p.) | What one parameter means: function, type, unit, default, hardware, CoE index, parameter-set copies |
| [runtime-axs-v-06-functions/](runtime-axs-v-06-functions/index.md) | Runtime AXS-V-06RS Functions, Application Manual (R911425358 Ed. 02, 890 p.) | How a function works and which parameters belong to it: modes, control loops, encoders, homing, scaling, limits, error reactions, scope, I/O |
| [xcs2-compact-hw/](xcs2-compact-hw/index.md) | ctrlX DRIVE compact XCS2-F/H, Project Planning Manual (R912009455 Ed. 07, 172 p.) | Hardware of the **test drive** (XCS2-W0007F…): connectors, wiring, technical data, LED/diagnostic display |
| [modular-drive-systems-hw/](modular-drive-systems-hw/index.md) | ctrlX DRIVE modular Drive Systems, Project Planning Manual (R911386579 Ed. 17, 582 p.) | XCS/XCD/XMS/XMD/XVR/XVE/XMV hardware: one file per device family's technical data |

## How to look things up

- **A parameter:** `grep -P '^S-0-0091\t' parameters-axs-v-reference/params.tsv` gives the CoE index, legacy
  IDN, name, file and PDF page. Then `grep -n '^### S-0-0091 ' <file>` and read about 60 lines from there. The
  attribute table (type, unit, default, prerequisites) is under the `## Object 0x....` heading just above.
  `params.tsv` uses the short IDN form (`S-0-0091`, `P-0-4089.0.1`): leave a trailing `.0.0` off when
  grepping. Parameter-set copies (S-1-/S-2-/S-3-) are listed inside the S-0- entry.
- **By name or topic:** `grep -il '<words>' */*.md`, then each folder's `index.md` to pick the chapter.
- **A diagnosis code (F2174, E8260, …):** `grep -rn 'F2174' .`. Codes appear in the function chapters; the
  error reactions chapter is `runtime-axs-v-06-functions/52-error-reactions.md`.
- **Hardware data for the test drive:** `xcs2-compact-hw/10-technical-data-of-components.md`;
  connectors and wiring are in `09-mounting-and-installation.md`.

Coverage: 1258 of the test drive's 1283 IDNs (`../references/idn-index.tsv`) are documented in the reference
book. The other 25 are mostly Sercos/MCI and internal parameters the book doesn't list.
To convert another manual: `python3 -I ../scripts/pdf_to_md.py BOOK.pdf <folder> --kind manual|params --title "..."`.
