"""Merge a user's exported glossary into the shipped one.

In the review window, "Copy my glossary (JSON)" puts the user's entries on the
clipboard. This script folds them into Data/ffxiv_jp_glossary.json:

  * An entry with the same Japanese as a built-in one is updated in place:
    the user's renderings win, any language they left blank keeps the
    built-in wording, and the entry keeps its position (and its `next` rule).
  * A new entry is appended, so for /jp an existing phrase with the same
    rendering, normally the standard polite form, stays first.
  * Any language still empty after that is filled with Google Translate and
    flagged for review. It translates from the entry's English, the reading
    a person already checked, not from the Japanese: Google reads FF14
    jargon literally (零式消化 -> "Pencernaan tipe nol", "type-zero
    digestion"), which is the whole reason the glossary exists. Only a
    missing English is translated from the Japanese.
  * `aliases` (other wordings /jp recognises, per language) are combined:
    the user's first, then the built-in ones, without repeats.
  * `src` becomes "curated" on entries that changed.

"Same Japanese" mirrors FfxivGlossary.KeyOf: NFKC, lower-case, trimmed, and
trailing segment breaks (! 。 ~ …) ignored, but not '?'.

Usage (from the repo root):
  python tools/merge_glossary.py                 # read the export from the clipboard
  python tools/merge_glossary.py export.json     # or from a file ('-' for stdin)
  python tools/merge_glossary.py --dry-run       # show what would change, write nothing

Review the printed changes (and `git diff Data/`) before committing.
"""

import argparse
import json
import re
import subprocess
import sys
import time
import unicodedata
import urllib.parse
import urllib.request
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
GLOSSARY = REPO / "Data" / "ffxiv_jp_glossary.json"

LANGS = ("en", "zh-TW", "id")
KEY_ORDER = ("ja", "en", "zh-TW", "id", "aliases", "mode", "next", "src")
# FfxivGlossary.SegmentBreaks, plus the whitespace KeyOf trims.
SEGMENT_BREAKS = "、,。.!~〜…♪☆★♡♥ \t\r\n"


def key_of(japanese: str) -> str:
    return unicodedata.normalize("NFKC", japanese).strip().lower().rstrip(SEGMENT_BREAKS)


def reverse_key(text: str) -> str:
    """FfxivGlossary.ReverseKey: also ignores spaces and apostrophes ("dont" == "don't")."""
    return key_of(re.sub(r"[\s'‘’ʼ`´]+", "", unicodedata.normalize("NFKC", text)))


def merge_aliases(mine, built_in, renderings: dict) -> dict:
    """Per language: the user's aliases, then the built-in ones; no repeats, no copy of the rendering."""
    merged = {}
    for source in (mine, built_in):
        if not isinstance(source, dict):
            continue
        for lang, aliases in source.items():
            if isinstance(aliases, str):
                aliases = [aliases]
            if not isinstance(aliases, list):
                continue
            target = merged.setdefault(lang, [])
            seen = {reverse_key(a) for a in target} | {reverse_key(clean(renderings.get(lang)))}
            for alias in map(clean, aliases):
                if alias and reverse_key(alias) and reverse_key(alias) not in seen:
                    target.append(alias)
                    seen.add(reverse_key(alias))
    return {lang: aliases for lang, aliases in merged.items() if aliases}


def clean(value) -> str:
    return value.strip() if isinstance(value, str) else ""


def read_export(source: str | None) -> list[dict]:
    if source is None:
        # PowerShell's clipboard reader, forced to UTF-8 so kana survive.
        raw = subprocess.run(
            ["powershell", "-NoProfile", "-Command",
             "[Console]::OutputEncoding = [Text.Encoding]::UTF8; Get-Clipboard -Raw"],
            capture_output=True, check=True,
        ).stdout.decode("utf-8")
    elif source == "-":
        raw = sys.stdin.buffer.read().decode("utf-8")
    else:
        raw = Path(source).read_text(encoding="utf-8")

    raw = raw.lstrip("﻿").strip()
    if not raw:
        sys.exit("Nothing to merge: the export is empty. Copy it with 'Copy my glossary (JSON)' first.")
    try:
        data = json.loads(raw)
    except json.JSONDecodeError as e:
        sys.exit(f"The export is not valid JSON ({e}). Copy it again with 'Copy my glossary (JSON)'.")

    entries = data.get("entries") if isinstance(data, dict) else data
    if not isinstance(entries, list):
        sys.exit("The export has no 'entries' list.")
    return entries


def google(text: str, source: str, target: str) -> str:
    """source -> target through the same gtx endpoint the plugin uses."""
    query = urllib.parse.urlencode({"client": "gtx", "sl": source, "tl": target, "dt": "t", "q": text})
    request = urllib.request.Request(
        f"https://translate.googleapis.com/translate_a/single?{query}",
        headers={"User-Agent": "Mozilla/5.0"},
    )
    for attempt in range(3):
        try:
            with urllib.request.urlopen(request, timeout=15) as response:
                body = json.loads(response.read().decode("utf-8"))
            return "".join(seg[0] for seg in body[0] if seg and isinstance(seg[0], str)).strip()
        except Exception as e:  # noqa: BLE001 - any failure just means "try again / leave it"
            if attempt == 2:
                print(f"    ! Google failed for {text!r} ({source} -> {target}): {e}")
            time.sleep(1 + attempt)
    return ""


def ordered(entry: dict) -> dict:
    out = {k: entry[k] for k in KEY_ORDER if entry.get(k)}
    out.update({k: v for k, v in entry.items() if k not in out and v})
    return out


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("export", nargs="?", help="exported JSON file, '-' for stdin; default: the clipboard")
    parser.add_argument("--glossary", type=Path, default=GLOSSARY, help="glossary to merge into")
    parser.add_argument("--dry-run", action="store_true", help="print the changes without writing")
    args = parser.parse_args()
    sys.stdout.reconfigure(encoding="utf-8")

    raw_bytes = args.glossary.read_bytes()
    newline = "\r\n" if b"\r\n" in raw_bytes else "\n"
    glossary = json.loads(raw_bytes.decode("utf-8-sig"))
    entries: list[dict] = glossary["entries"]
    index = {}
    for i, entry in enumerate(entries):
        index.setdefault(key_of(entry["ja"]), i)

    added = updated = unchanged = skipped = 0
    to_review: list[str] = []

    for item in read_export(args.export):
        if not isinstance(item, dict) or not clean(item.get("ja")):
            skipped += 1
            continue

        japanese = clean(item["ja"])
        key = key_of(japanese)
        existing = entries[index[key]] if key in index else None
        merged = dict(existing) if existing else {"ja": japanese}

        for lang in LANGS:
            if clean(item.get(lang)):
                merged[lang] = clean(item[lang])
        aliases = merge_aliases(item.get("aliases"), merged.get("aliases"), merged)
        if aliases:
            merged["aliases"] = aliases
        else:
            merged.pop("aliases", None)
        # The export always says what kind of entry it is: "phrase" or absent (term).
        if clean(item.get("mode")) == "phrase":
            merged["mode"] = "phrase"
        else:
            merged.pop("mode", None)

        filled = []
        for lang in LANGS:  # "en" first, so the others can translate from it
            if not clean(merged.get(lang)):
                english = clean(merged.get("en"))
                translated = google(english, "en", lang) if english else google(japanese, "ja", lang)
                if translated:
                    merged[lang] = translated
                    filled.append(lang)
                else:
                    merged.pop(lang, None)

        if not clean(merged.get("en")):
            print(f"  ✗ {japanese}: no English, even from Google; skipped")
            skipped += 1
            continue

        if existing is None or ordered(merged) != ordered(existing):
            merged["src"] = "curated"
        merged = ordered(merged)
        kind = "phrase" if merged.get("mode") == "phrase" else "term"
        if existing is None:
            entries.append(merged)
            index[key] = len(entries) - 1
            added += 1
            print(f"  + {japanese} ({kind})")
            for lang in LANGS:
                print(f"      {lang:5} {merged.get(lang, '')}{'   ← Google' if lang in filled else ''}")
            for lang, aliases in merged.get("aliases", {}).items():
                print(f"      also {lang:5} {' | '.join(aliases)}")
        elif merged == ordered(existing):
            unchanged += 1
            continue
        else:
            entries[index[key]] = merged
            updated += 1
            print(f"  ~ {existing['ja']} ({kind})")
            for field in ("mode",) + LANGS:
                before, after = existing.get(field, ""), merged.get(field, "")
                if before != after:
                    note = "   ← Google" if field in filled else ""
                    print(f"      {field:5} {before or '∅'}  →  {after or '∅'}{note}")
            if existing.get("aliases") != merged.get("aliases"):
                print(f"      also  {existing.get('aliases') or '∅'}  →  {merged.get('aliases') or '∅'}")
        if filled:
            to_review.append(f"{japanese}: {', '.join(filled)}")

    print(f"\n{added} added, {updated} updated, {unchanged} already identical, {skipped} skipped.")
    if to_review:
        print("Filled by Google; check these before committing:")
        for line in to_review:
            print(f"  - {line}")

    if args.dry_run or not (added or updated):
        if args.dry_run:
            print("Dry run: nothing written.")
        return

    text = json.dumps(glossary, ensure_ascii=False, indent=1) + "\n"
    args.glossary.write_bytes(text.replace("\n", newline).encode("utf-8"))
    print(f"Wrote {args.glossary.relative_to(REPO) if args.glossary.is_relative_to(REPO) else args.glossary}. "
          "Review with `git diff Data/`, then commit and release.")


if __name__ == "__main__":
    main()
