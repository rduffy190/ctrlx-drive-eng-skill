# SERCOS-ASCII parameter files (.par)

Format written by EAL `IParameter.SaveParameters` and read by `LoadParameters`. It is **not in the EAL docs**.
Everything here comes from files a ctrlX DRIVE (FWA-XD1-AXS-V-0610N) produced on 2026-10-12. DriveTool's
`Par/` classes parse and re-emit these files byte-for-byte (`drivetool par show F` reports `round_trips`).

## Layout

UTF-8 without BOM, LF line endings. A `|` line follows the header and separates blocks. The drive writes
**no** `|` after the last block. The file ends with the last data line plus `\n`.

```
SERCOS-ASCII                       <- magic
10/12/2026 10:34:17 AM             <- timestamp
default                            <- axis name
FWA-... | XCS2-... | MS2S02-... | FUS2-...   <- type plate: firmware | drive | motor | option
1                                  <- parameter-set count (LoadParameters' paraSetIndex picks one)
|
S-0-0036.0.0                       <- IDN, always full form T-s-NNNN.si.se
Target velocity                    <- name (drive language)
00000100001000100000000000000001   <- attribute, 32 bits MSB first
Rev/min                            <- unit, "--" = none
-9100.0000                         <- minimum, "--" = none
9100.0000                          <- maximum, "--" = none
0.0000                             <- data line(s)
|
...
```

## Data lines by type

| Kind | Data lines | Example |
|---|---|---|
| scalar decimal | one, with exactly `decimal_places` decimals | `1000.0000`, `4000`, `3.0` |
| scalar binary | 4-bit groups joined by `.`, **no `0b`** (EAL `ReadDataAsString` adds `0b`) | `0110.0000.0000.0001` |
| scalar hex | `0x` + 2 hex digits per byte | `0xC000`, `0xC01F2174` |
| scalar IDN | full IDN | `S-0-0040.0.0` |
| float | .NET invariant `ToString()` | `-4.6`, `1E-06` |
| text | one line: `actual_bytes max_bytes text` (text may have trailing spaces) | `25 56 FWA-XD1-AXS-V-0610N-NN-00` |
| list | `actual_bytes`, `max_bytes`, then one element per line | `24` / `60` / `S-0-0144.0.0` / ... |
| empty list | `0` / `max_bytes`, no element lines | `0` / `256` |
| command | one status-like binary line | `0000.0000.0000.0000` |

List lengths are in **bytes**. Element count = actual_bytes / element size.

## Attribute bits (bit 0 = rightmost character)

| Bits | Meaning |
|---|---|
| 0–15 | conversion factor (unused here) |
| 16–18 | length: 1/2/3 = fixed 2/4/8 bytes; 4/5/6/7 = variable list of 1/2/4/8-byte elements (4 + text = string) |
| 19 | procedure command (e.g. S-0-0099 C0500 reset) |
| 20–22 | display: 0 binary, 1 unsigned dec, 2 signed dec, 3 hex, 4 text, 5 IDN, 6 float, 7 SERCOS time |
| 24–27 | decimal places |
| 28 | write-protected in P2 |
| 29 | write-protected in P3 |
| 30 | write-protected in operating mode (OM) |

Write classes seen on the drive: `0000…` writable always, `0110…` writable only outside OM, `0111…` read-only.
**PM vs CM is not encoded:** a `0110` parameter needs PM *or* CM, and only the drive knows which. That's why
DriveTool reports `pm_or_cm`, writes those in PM by default, and uses CM only with an explicit `--mode cm`.

## Unsupported IDNs in a bulk read

`SaveParameters(stream, idns)` doesn't raise an error for an IDN the drive lacks. It writes a **6-line stub**:
the IDN, an empty name, attribute `000…0`, an empty unit, `--`, `--`, and no data. DriveTool lists these in
`ParFile.Unsupported` and reports them as `missing`.

Parameter numbers above 4095 don't fit the 12-bit field. EAL silently wraps them: `S-0-9999` is sent as
`S-2-1807`. DriveTool rejects them before calling EAL.

## Building a write file

To bulk-write, DriveTool bulk-reads the target IDNs (which gives the drive's exact header, attribute and
limits), replaces only the data lines, and calls `LoadParameters(stream, 0)`. Don't hand-write attributes or
limits. Always start from the drive's own blocks.
