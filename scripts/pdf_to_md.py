#!/usr/bin/env python3
"""Break a Bosch Rexroth manual PDF into greppable Markdown files, split along the PDF bookmarks.

  python3 -I pdf_to_md.py BOOK.pdf OUTDIR --kind manual [--max-pages 60] [--title "..."]
  python3 -I pdf_to_md.py BOOK.pdf OUTDIR --kind params [--chunk-pages 35] [--title "..."]

manual: one file per chapter (level-1 bookmark); chapters longer than --max-pages are split per level-2.
params: the "Objects/Parameters" reference book. One "## Object 0x...." heading per object
        (from "Brief description (0x....)") and one "### <IDN> · <name>" per parameter, chunked into files
        of about --chunk-pages pages. Also writes params.tsv (idn, coe, legacy_idn, name, file, pdf_page).

Text is `pdftotext -layout` output (tables keep their column alignment) with running headers/footers
removed, margins dedented and hyphenated line breaks joined. `<!-- p.N -->` marks each PDF page start, so
figures can be rendered later with: mutool draw -o fig.png -r 110 BOOK.pdf N
Needs poppler-utils (pdftotext) and mupdf-tools (mutool).
"""
import argparse
import os
import re
import subprocess
import sys

LRM = "‎"
SOFT_HYPHEN = "‐"  # the manuals break words with U+2010 "‐"

FOOTER_PATTERNS = [
    re.compile(r"^\s*\d+\s*/\s*\d+\s+ctrlX DRIVE.*$"),
    re.compile(r"^\s*ctrlX DRIVE.*\s\d+\s*/\s*\d+\s*$"),
    re.compile(r"^\s*Bosch Rexroth AG\b.*$"),
    re.compile(r"^\s*R\d{6,}\s*[–-]\s*Edition\s+\d+.*$"),
    re.compile(r"^\s*\.\s*$"),
]
DATE_LINE = re.compile(r"^\s*\d{2}\.\d{2}\.\d{4}\s*$")
PARAM_TITLE = re.compile(r"^(?P<name>.*?)\s*\((?P<coe>0x[0-9A-F]+(?::[0-9A-F]+)?)\s*/\s*(?P<idn>[^/]+?)\s*/\s*(?P<legacy>[^)]+?)\)\s*$")
BRIEF_TITLE = re.compile(r"^Brief description \((?P<obj>0x[0-9A-F]+)\)$")


def norm(s):
    return re.sub(r"\s+", " ", s.replace(LRM, "").replace(SOFT_HYPHEN, "")).strip().lower()


def slug(s, n=48):
    s = re.sub(r"^[\d.]+\s+", "", norm(s))  # drop the chapter number; the file prefix numbers already
    s = re.sub(r"[^a-z0-9]+", "-", s).strip("-")
    return s[:n].rstrip("-") or "section"


def read_outline(pdf):
    out = subprocess.run(["mutool", "show", pdf, "outline"], capture_output=True, text=True, check=True).stdout
    entries = []
    for line in out.splitlines():
        parts = line.split("\t")
        if len(parts) < 3:
            continue
        m = re.search(r"#page=(\d+)", parts[-1])
        if not m:
            continue
        title = parts[-2].strip()
        if title.startswith('"') and title.endswith('"'):
            title = title[1:-1]
        title = title.replace('\\"', '"').replace(LRM, "").strip()
        entries.append({"level": len(parts) - 2, "title": title, "page": int(m.group(1))})
    return entries


def read_pages(pdf):
    text = subprocess.run(["pdftotext", "-layout", pdf, "-"], capture_output=True, text=True, check=True).stdout
    pages = text.split("\f")
    if pages and not pages[-1].strip():
        pages.pop()
    return pages


def clean_page(text, running_titles):
    lines = [l.rstrip() for l in text.split("\n")]
    # running headers/footers
    keep = []
    nonempty_idx = [i for i, l in enumerate(lines) if l.strip()]
    top = set(nonempty_idx[:3])
    for i, l in enumerate(lines):
        if any(p.match(l) for p in FOOTER_PATTERNS):
            continue
        if i in top and (DATE_LINE.match(l) or norm(re.sub(r"^\s*[\d.]+\s+", "", l)) in running_titles):
            continue
        keep.append(l)
    # dedent
    ind = [len(l) - len(l.lstrip()) for l in keep if l.strip()]
    cut = min(ind) if ind else 0
    keep = [l[cut:] if l.strip() else "" for l in keep]
    # join words hyphenated across lines (same indentation, next starts lowercase)
    out = []
    i = 0
    while i < len(keep):
        l = keep[i]
        if l.endswith(SOFT_HYPHEN) and i + 1 < len(keep):
            nxt = keep[i + 1]
            same_indent = abs((len(l) - len(l.lstrip())) - (len(nxt) - len(nxt.lstrip()))) <= 2
            m = re.match(r"\s*([a-zäöüß][^\s]*)(.*)$", nxt)
            if same_indent and m:
                out.append(l[:-1] + m.group(1))
                rest = m.group(2).strip()
                keep[i + 1] = (" " * (len(nxt) - len(nxt.lstrip())) + rest) if rest else ""
                i += 1
                if not rest:
                    i += 1
                continue
        out.append(l)
        i += 1
    # collapse runs of blank lines
    res, blank = [], 0
    for l in out:
        blank = blank + 1 if not l.strip() else 0
        if blank <= 1:
            res.append(l)
    return res


def locate(entries, pages):
    """Find each bookmark's line in the cleaned pages; keep only entries that move forward."""
    cur_p, cur_l = 0, 0
    located = []
    for e in entries:
        p = e["page"] - 1
        if p < cur_p or p >= len(pages):
            continue
        key = norm(e["title"])[:40]
        key_short = norm(e["title"])[:24]
        # Parameter titles: match the "(0x607F:00 / S-0-0091" tuple, which only the real title line has
        # (the attribute table above it starts with the same name and must not be split).
        mp = PARAM_TITLE.match(e["title"]) if e["level"] == 1 else None
        tuple_key = norm(f"({mp.group('coe')} / {mp.group('idn')}") if mp else None
        name_key = norm(mp.group("name"))[:15] if mp else None
        found = None
        for pp in (p, p + 1):
            if pp >= len(pages):
                break
            start = cur_l if pp == cur_p else 0
            for li in range(start, len(pages[pp])):
                nl = norm(pages[pp][li])
                if not nl:
                    continue
                if tuple_key:
                    nxt = norm(pages[pp][li + 1]).replace(" ", "") if li + 1 < len(pages[pp]) else ""
                    joined = nl.replace(" ", "") + nxt
                    tk = tuple_key.replace(" ", "")
                    # the tuple may wrap onto the next line, even mid-IDN; quoted list entries ("Name" (0x...))
                    # aren't titles. Whitespace is ignored for the comparison.
                    if tk in joined and tk not in nxt and ('"' + tk) not in joined:
                        # title wrapped: the name starts on the previous line
                        if li > start and not nl.startswith(name_key) and norm(pages[pp][li - 1]).startswith(name_key):
                            li -= 1
                        found = (pp, li)
                        break
                    continue
                # short titles ("Function", "Use") must start the line; long ones may sit after margin text
                if nl.startswith(key) or (len(key) >= 20 and key in nl) or (len(key_short) >= 20 and nl.startswith(key_short)):
                    found = (pp, li)
                    break
            if found:
                break
        if not found:
            found = (p, cur_l if p == cur_p else 0)
        if found < (cur_p, cur_l):
            found = (cur_p, cur_l)
        e = dict(e, at=found)
        located.append(e)
        cur_p, cur_l = found
    return located


def section_text(pages, start, end, skip_heading_line):
    """Lines from start (page, line) up to end (exclusive), with page markers."""
    (sp, sl), (ep, el) = start, end
    out = []
    for p in range(sp, ep + 1):
        lo = sl if p == sp else 0
        hi = el if p == ep else len(pages[p])
        if p != sp or lo == 0:
            out.append(f"<!-- p.{p + 1} -->")
        chunk = pages[p][lo:hi]
        if p == sp and skip_heading_line and chunk:
            chunk = chunk[1:]
        out.extend(chunk)
    while out and not out[-1].strip():
        out.pop()
    while out and not out[0].strip():
        out.pop(0)
    return out


def write(path, lines):
    with open(path, "w", encoding="utf-8") as f:
        f.write("\n".join(lines).rstrip() + "\n")


def build_sections(located, pages):
    end_of_doc = (len(pages) - 1, len(pages[-1]))
    for i, e in enumerate(located):
        nxt = located[i + 1]["at"] if i + 1 < len(located) else end_of_doc
        e["body"] = section_text(pages, e["at"], nxt, skip_heading_line=True)
    return located


def heading(level, title):
    return "#" * min(level, 4) + " " + title


def run_manual(pdf, out, title, max_pages, located, pages):
    chapters = []
    for e in located:
        if e["level"] == 1:
            chapters.append([e])
        elif chapters:
            chapters[-1].append(e)
    files = []
    n = 0
    for ch in chapters:
        if norm(ch[0]["title"]) in ("table of contents", "inhaltsverzeichnis"):
            continue
        first, last_page = ch[0]["page"], (ch[-1]["at"][0] + 1)
        groups = [ch]
        if last_page - first + 1 > max_pages:
            groups = [[ch[0]]]
            for e in ch[1:]:
                if e["level"] == 2:
                    groups.append([e])
                else:
                    groups[-1].append(e)
            if len(groups[0]) == 1 and len(groups) > 1 and not ch[0]["body"]:
                groups[1].insert(0, groups.pop(0)[0])
        for g in groups:
            n += 1
            name = f"{n:02d}-{slug(g[0]['title'])}.md"
            lines = [f"<!-- {title} — source: {os.path.basename(pdf)} -->"]
            if g[0]["level"] > 1:
                lines += [heading(1, ch[0]["title"] + " (continued)"), ""]
            for e in g:
                lines += ["", heading(e["level"], e["title"]), f"*PDF p. {e['at'][0] + 1}*", ""] + e["body"]
            write(os.path.join(out, name), lines)
            files.append((name, g))
    idx = [f"# {title}", "", f"Source: `{pdf}` ({len(pages)} pages). Text via pdftotext -layout; figures are not",
           "included — render a page with `mutool draw -o fig.png -r 110 \"<pdf>\" <page>`. `<!-- p.N -->` marks PDF pages.", "",
           "| File | PDF pages | Contents (chapters / sections) |", "|---|---|---|"]
    for name, g in files:
        p0, p1 = g[0]["at"][0] + 1, max(e["at"][0] for e in g) + 1
        tops = [e["title"] for e in g if e["level"] <= 2][:14]
        idx.append(f"| [{name}]({name}) | {p0}–{p1} | {'; '.join(t.replace('|', '/') for t in tops)} |")
    write(os.path.join(out, "index.md"), idx)
    return len(files)


def run_params(pdf, out, title, chunk_pages, located, pages, all_entries):
    first_obj = next(i for i, e in enumerate(located) if BRIEF_TITLE.match(e["title"]) or (e["level"] == 1 and PARAM_TITLE.match(e["title"])))
    last_param = max(i for i, e in enumerate(located) if e["level"] == 1 and (PARAM_TITLE.match(e["title"]) or BRIEF_TITLE.match(e["title"])))
    # end of the parameter part = next level-1 entry after the last parameter
    tail_start = next((i for i in range(last_param + 1, len(located)) if located[i]["level"] == 1), len(located))
    front, body, tail = located[:first_obj], located[first_obj:tail_start], located[tail_start:]

    files, rows = [], []

    def emit_simple(name, entries, heading_title=None):
        lines = [f"<!-- {title} — source: {os.path.basename(pdf)} -->"]
        if heading_title:
            lines += [f"# {heading_title}", ""]
        for e in entries:
            lines += ["", heading(e["level"] + (1 if heading_title else 0), e["title"]), f"*PDF p. {e['at'][0] + 1}*", ""] + e["body"]
        write(os.path.join(out, name), lines)
        files.append((name, entries, None))

    emit_simple("00-general.md", front, "General information")

    # chunk the parameter part at object boundaries
    chunks, cur, cur_start = [], [], None
    for e in body:
        is_obj = bool(BRIEF_TITLE.match(e["title"]))
        if cur and is_obj and e["at"][0] - cur_start >= chunk_pages:
            chunks.append(cur)
            cur = []
        if not cur:
            cur_start = e["at"][0]
        cur.append(e)
    if cur:
        chunks.append(cur)

    for n, ch in enumerate(chunks, start=1):
        p0, p1 = ch[0]["at"][0] + 1, max(e["at"][0] for e in ch) + 1
        name = f"{n:02d}-p{p0:04d}-{p1:04d}.md"
        lines = [f"<!-- {title} — source: {os.path.basename(pdf)} -->", f"# Parameters, PDF pages {p0}–{p1}", ""]
        topics = []
        for e in ch:
            mb, mp = BRIEF_TITLE.match(e["title"]), PARAM_TITLE.match(e["title"]) if e["level"] == 1 else None
            if mb:
                h = f"## Object {mb.group('obj')}"
            elif mp:
                idn, legacy = mp.group("idn").strip(), mp.group("legacy").strip()
                h = f"### {idn} · {mp.group('name')} ({mp.group('coe')}" + (f", legacy {legacy}" if legacy != "--" else "") + ")"
                rows.append((idn, mp.group("coe"), legacy, mp.group("name"), name, e["at"][0] + 1))
                topic = mp.group("name").split(":")[0].strip()
                if topic not in topics:
                    topics.append(topic)
            else:
                h = heading(e["level"] + 3, e["title"])
            lines += ["", h, f"*PDF p. {e['at'][0] + 1}*", ""] + e["body"]
        write(os.path.join(out, name), lines)
        files.append((name, ch, topics))

    tail_groups = []
    for e in tail:
        if e["level"] == 1:
            tail_groups.append([e])
        elif tail_groups:
            tail_groups[-1].append(e)
    for k, g in enumerate(tail_groups, start=90):
        emit_simple(f"{k:02d}-{slug(g[0]['title'])}.md", g)

    # Bookmarks that couldn't be placed still get an index row: the file holding their PDF page.
    have = {r[0] + r[1] for r in rows}
    for e in all_entries:
        mp = PARAM_TITLE.match(e["title"]) if e["level"] == 1 else None
        if mp and mp.group("idn").strip() + mp.group("coe") not in have:
            for name, entries, topics in files:
                if topics is not None and entries[0]["at"][0] + 1 <= e["page"] <= max(x["at"][0] for x in entries) + 1:
                    rows.append((mp.group("idn").strip(), mp.group("coe"), mp.group("legacy").strip(),
                                 mp.group("name") + " [no heading; search the PDF page]", name, e["page"]))
                    break

    with open(os.path.join(out, "params.tsv"), "w", encoding="utf-8") as f:
        f.write("# idn\tcoe\tlegacy_idn\tname\tfile\tpdf_page\n")
        for r in rows:
            f.write("\t".join(str(x) for x in r) + "\n")

    idx = [f"# {title}", "", f"Source: `{pdf}` ({len(pages)} pages). Text via pdftotext -layout; `<!-- p.N -->` marks PDF pages.", "",
           f"**Find a parameter:** `grep -i 'S-0-0091' params.tsv` ({len(rows)} parameters: idn, CoE index, legacy IDN, name, file, PDF page),",
           "then `grep -n '^### S-0-0091' <file>` and read from that line. Each parameter section has the object's",
           "attribute table (type, unit, default, hardware, parameter-set switching) under its `## Object` heading above it.", "",
           "| File | PDF pages | Contents |", "|---|---|---|"]
    for name, entries, topics in files:
        p0, p1 = entries[0]["at"][0] + 1, max(e["at"][0] for e in entries) + 1
        what = "; ".join(topics[:12]) + (" …" if topics and len(topics) > 12 else "") if topics is not None else "; ".join(e["title"] for e in entries if e["level"] == 1)
        idx.append(f"| [{name}]({name}) | {p0}–{p1} | {what.replace('|', '/')} |")
    write(os.path.join(out, "index.md"), idx)
    return len(files), len(rows)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("pdf")
    ap.add_argument("outdir")
    ap.add_argument("--kind", choices=["manual", "params"], required=True)
    ap.add_argument("--title")
    ap.add_argument("--max-pages", type=int, default=60)
    ap.add_argument("--chunk-pages", type=int, default=35)
    a = ap.parse_args()

    os.makedirs(a.outdir, exist_ok=True)
    entries = read_outline(a.pdf)
    raw_pages = read_pages(a.pdf)
    # Manuals repeat the chapter title at the top of each page; the params book doesn't (its short
    # level-2 titles like "Function" are real headings there).
    running = set()
    if a.kind == "manual":
        running = {norm(re.sub(r"^\s*[\d.]+\s+", "", e["title"])) for e in entries if e["level"] <= 2}
    pages = [clean_page(p, running) for p in raw_pages]
    located = build_sections(locate(entries, pages), pages)
    lost = [e["title"] for e in entries if e["title"] not in {x["title"] for x in located}]
    if lost:
        print(f"not placed ({len(lost)}): " + "; ".join(t[:60] for t in lost[:8]), file=sys.stderr)
    title = a.title or os.path.splitext(os.path.basename(a.pdf))[0]
    if a.kind == "manual":
        n = run_manual(a.pdf, a.outdir, title, a.max_pages, located, pages)
        print(f"{a.outdir}: {n} files from {len(pages)} pages, {len(located)}/{len(entries)} bookmarks placed")
    else:
        n, p = run_params(a.pdf, a.outdir, title, a.chunk_pages, located, pages, entries)
        print(f"{a.outdir}: {n} files, {p} parameters from {len(pages)} pages, {len(located)}/{len(entries)} bookmarks placed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
