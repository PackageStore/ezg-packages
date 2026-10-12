#!/usr/bin/env python3
"""Add one (or a batch of) localization key(s): the project half.

The sheet half runs on the EZG MCP server (tool `localize_add_keys`), which
writes with EZG's shared Google service account - no key file ever sits in a
project or on a laptop. This script does the two local steps around it:

  1. plan    validate the entries against this project (key format, the tab
             has a CSV table here) and print the exact arguments for
             `localize_add_keys`
  2. (MCP)   localize_add_keys stages the rows in `template` (English, =C<row>,
             GOOGLETRANSLATE for every language not authored by hand), waits
             until they resolve, writes the static text into the target tab and
             returns the 16 translations per key
  3. apply   `--apply <result.json>` merges only those keys into
             LocalizationData/<lang>/<tab>.csv and
             Resources/LocalizationData/<lang>/<tab>.asset (additive)

Scope: this adds keys. It does NOT scan prefabs for hardcoded strings and does
NOT attach the localize component - that is `fill-localize`.

Usage
-----
  python3 .claude/skills/add-localize/scripts/add_localize.py \
      --key ui_play --en "Play" --vi "Choi" [--tab common]          # 1. plan

  python3 .claude/skills/add-localize/scripts/add_localize.py --file keys.json

  keys.json: [{"key": "ui_play", "en": "Play", "vi": "Choi", "tab": "shop"}, ...]

  python3 .claude/skills/add-localize/scripts/add_localize.py \
      --apply /tmp/localize-result.json [--dry-run]                    # 3. apply

Any language column can be authored by hand instead of machine-translated:
  --pt "..." --th "..." --zhcn "..." --ko "..." --ja "..." --de "..." --it "..."
  --es "..." --id "..." --ru "..." --fr "..." --nl "..." --tr "..." --pl "..."
(or the same names as fields inside a --file entry).
"""
import argparse
import importlib.util
import json
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
KEY_RE = re.compile(r"^[a-z0-9_]+$")


# --------------------------------------------------------------------------
# Per-project values live in .claude/project-profile.json under `localize`, not
# in this file: the skill ships from the template to every project and has to
# stay byte-identical, so a project changes its profile, never the script.
# --------------------------------------------------------------------------
def repo_root():
    try:
        out = subprocess.run(["git", "rev-parse", "--show-toplevel"],
                             capture_output=True, text=True,
                             check=True).stdout.strip()
        if out:
            return out
    except (OSError, subprocess.CalledProcessError):
        pass
    return os.path.normpath(os.path.join(HERE, "..", "..", "..", ".."))


ROOT = repo_root()


def profile():
    path = os.path.join(ROOT, ".claude", "project-profile.json")
    try:
        data = json.load(open(path, encoding="utf-8"))
    except (OSError, ValueError):
        data = {}
    src = data.get("sourceRoot", "Assets/_Project")
    loc = data.get("localize") or {}
    # `or` rather than a get() default: the shipped profile carries these keys
    # with empty values so a new project can see the knobs, and an empty string
    # has to mean "not set", not "set to nothing".
    return {
        "sheet": loc.get("sheet") or "",
        "csvRoot": (loc.get("csvRoot")
                    or src + "/Features/_Shared/Localize/LocalizationData"),
        "assetsRoot": (loc.get("assetsRoot")
                       or src + "/Features/_Shared/Localize/Resources/"
                                "LocalizationData"),
        "stagingTab": loc.get("stagingTab") or "template",
        "defaultTab": loc.get("defaultTab") or "common",
    }


def under_root(path):
    """Profile paths are repo-relative, so the script runs from any cwd."""
    return path if os.path.isabs(path) else os.path.join(ROOT, path)


PROFILE = profile()
STAGING_TAB = PROFILE["stagingTab"]
DEFAULT_TAB = PROFILE["defaultTab"]

# project language folder -> GOOGLETRANSLATE code (the server holds the same table).
# `en` is the source and `vi` is authored by hand, so neither is translated.
GT_CODE = {
    "pt": "pt", "th": "th", "zhcn": "zh-cn", "ko": "ko", "ja": "ja",
    "de": "de", "it": "it", "es": "es", "id": "id", "ru": "ru",
    "fr": "fr", "nl": "nl", "tr": "tr", "pl": "pl",
}
AUTHORED = ("en", "vi")
ALL_LANGS = tuple(AUTHORED) + tuple(sorted(GT_CODE))
# `vi` left empty falls back to machine translation (with a warning).
GT_FALLBACK = dict(GT_CODE, vi="vi")

MCP_TOOL = "localize_add_keys"


# --------------------------------------------------------------------------
# The LanguageData .asset reader/writer (and the sheet URL parser) live next to
# this file in sheet_io.py, so the skill works in a project that has nothing
# else installed.
# --------------------------------------------------------------------------
def load_sheet_module():
    path = os.path.join(HERE, "sheet_io.py")
    if not os.path.exists(path):
        raise SystemExit("sheet_io.py is missing next to %s - the skill was "
                         "copied incompletely." % __file__)
    spec = importlib.util.spec_from_file_location("sheet_io", path)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


# --------------------------------------------------------------------------
# Entries
# --------------------------------------------------------------------------
def normalise_key(raw):
    key = raw.strip().lstrip("#").lower()
    if not KEY_RE.match(key):
        raise SystemExit(
            "bad key %r - this sheet uses lowercase snake_case with no '#' "
            "prefix (that is the BlazeSurvivor convention, not this one)" % raw)
    return key


def load_entries(args):
    out = []
    if args.file:
        data = json.load(open(args.file, encoding="utf-8"))
        if not isinstance(data, list):
            raise SystemExit("--file must contain a JSON list of objects")
        for i, d in enumerate(data):
            if not d.get("key") or not d.get("en"):
                raise SystemExit("entry %d needs at least 'key' and 'en'" % i)
            out.append({
                "key": normalise_key(d["key"]),
                "tab": d.get("tab") or args.tab,
                "values": {c: str(d.get(c, "")) for c in ALL_LANGS},
            })
    else:
        if not args.key or args.en is None:
            raise SystemExit("--key and --en are required (or use --file, "
                             "or --apply <result.json>)")
        out.append({
            "key": normalise_key(args.key),
            "tab": args.tab,
            "values": {c: str(getattr(args, c, "") or "") for c in ALL_LANGS},
        })
    for e in out:
        if not e["values"]["en"].strip():
            raise SystemExit("entry %r has an empty English value" % e["key"])
        if not e["values"]["vi"].strip():
            print("  ! %s: no Vietnamese given - it will be machine-translated "
                  "like the other 14 languages" % e["key"])
    seen = set()
    for e in out:
        sig = (e["tab"], e["key"])
        if sig in seen:
            raise SystemExit("duplicate entry %s in %s" % (e["key"], e["tab"]))
        seen.add(sig)
    return out


def project_tables(csv_root):
    en_dir = os.path.join(csv_root, "en")
    if not os.path.isdir(en_dir):
        return []
    return sorted(f[:-4] for f in os.listdir(en_dir) if f.endswith(".csv"))


def check_tabs(tabs, csv_root, allow_any_tab):
    for tab in tabs:
        if tab == STAGING_TAB:
            raise SystemExit("`%s` is the staging tab - pick a real table"
                             % STAGING_TAB)
        if not allow_any_tab and not os.path.exists(
                os.path.join(csv_root, "en", tab + ".csv")):
            raise SystemExit(
                "tab %r has no table in this project (%s/en/%s.csv). "
                "Tables here: %s. Use --allow-any-tab to override."
                % (tab, csv_root, tab,
                   ", ".join(project_tables(csv_root)) or "(none)"))


# --------------------------------------------------------------------------
# 1. plan - print the localize_add_keys arguments
# --------------------------------------------------------------------------
def plan(args, S):
    if not args.sheet:
        raise SystemExit(
            "no localization sheet configured - add a `localize` block to "
            "%s:\n"
            '  "localize": { "sheet": "https://docs.google.com/spreadsheets/d/..." }\n'
            "or pass --sheet / set LOCALIZE_SHEET."
            % os.path.join(ROOT, ".claude", "project-profile.json"))
    entries = load_entries(args)
    check_tabs(sorted({e["tab"] for e in entries}), args.csv_root,
               args.allow_any_tab)

    print("PLAN - nothing written yet")
    for e in entries:
        auto = [c for c in sorted(GT_FALLBACK) if not e["values"][c]]
        print("  %-14s %-28s en=%r vi=%r auto=%s"
              % (e["tab"], e["key"], e["values"]["en"], e["values"]["vi"],
                 ",".join(auto) or "-"))

    mcp_entries = []
    for e in entries:
        item = {"key": e["key"], "en": e["values"]["en"]}
        if e["values"]["vi"]:
            item["vi"] = e["values"]["vi"]
        if e["tab"] != DEFAULT_TAB:
            item["tab"] = e["tab"]
        for code in sorted(GT_CODE):
            if e["values"][code]:
                item[code] = e["values"][code]
        mcp_entries.append(item)
    call = {"sheet": S.sheet_id(args.sheet), "entries": mcp_entries,
            "default_tab": DEFAULT_TAB, "staging_tab": STAGING_TAB}
    if args.force:
        call["force"] = True
    if args.stage_only:
        call["stage_only"] = True
    if args.timeout:
        call["timeout_seconds"] = args.timeout
    print("\nNext: call the EZG MCP tool `%s` with these arguments "
          "(add \"dry_run\": true to only check the sheet first):" % MCP_TOOL)
    print(json.dumps(call, ensure_ascii=False, indent=2))
    print("\nThen save the tool's whole JSON result to a file and run:\n"
          "  python3 .claude/skills/add-localize/scripts/add_localize.py "
          "--apply <file>")


# --------------------------------------------------------------------------
# 3. apply - only the keys the sheet step returned, nothing else
# --------------------------------------------------------------------------
def csv_eol(lines):
    """The line ending a new row gets: whatever most rows already use.

    Tables come as LF, as CRLF, or - often - mostly LF with a few CRLF rows
    left by a hand edit. Picking CRLF as soon as one row has it rewrote every
    row of such a mixed table.
    """
    crlf = sum(1 for line in lines if line.endswith(b"\r\n"))
    lf = sum(1 for line in lines if line.endswith(b"\n")) - crlf
    return b"\r\n" if crlf > lf else b"\n"


def patch_csv(path, incoming, overwrite):
    """-> (new file bytes, or None when nothing changes; existing keys kept).

    Patches the table at byte level instead of re-serialising it: rows that are
    not touched keep their exact bytes (line ending, BOM, order), a new key is
    appended as one line, and --overwrite replaces only that key's line and
    keeps its line ending. `fill-localize pull` rewrites whole tables as CRLF;
    rewriting here too turns "added one key" into a diff over every row on a
    checkout that does not normalise line endings.
    """
    raw = b""
    if os.path.exists(path):
        with open(path, "rb") as f:
            raw = f.read()
    bom = b"\xef\xbb\xbf" if raw.startswith(b"\xef\xbb\xbf") else b""
    # split on \n only: a stray \r inside a value is data, not a row break
    parts = raw[len(bom):].split(b"\n")
    lines = [p + b"\n" for p in parts[:-1]]
    if parts[-1]:
        lines.append(parts[-1])
    if not lines:
        lines = [b"key~value\n"]
    eol = csv_eol(lines)

    rows = {}  # key -> line indexes; header skipped like S.read_project_csv
    for i, line in enumerate(lines):
        text = line.rstrip(b"\r\n")
        if not text or (i == 0 and text.startswith(b"key~")):
            continue
        k = text.partition(b"~")[0].decode("utf-8", "replace")
        if k:
            rows.setdefault(k, []).append(i)

    kept = 0
    changed = False
    for k, v in incoming.items():
        new = ("%s~%s" % (k, v)).encode("utf-8")
        if k in rows:
            if not overwrite:
                kept += 1
                continue
            for i in rows[k]:
                text = lines[i].rstrip(b"\r\n")
                if text != new:
                    lines[i] = new + lines[i][len(text):]
                    changed = True
            continue
        if not lines[-1].endswith(b"\n"):
            lines[-1] += eol
        lines.append(new + eol)
        rows[k] = [len(lines) - 1]
        changed = True
    return (bom + b"".join(lines) if changed else None), kept


def apply_to_project(S, tab, entries, csv_root, assets_root, overwrite,
                     create_missing, dry_run):
    added = kept = 0
    touched = []
    for code in ALL_LANGS:
        incoming = {}
        for e in entries:
            val = e["resolved"].get(code, "").strip()
            if val:
                incoming[e["key"]] = S.pipeline_text(val)
        if not incoming:
            continue

        csv_path = os.path.join(csv_root, code, tab + ".csv")
        if os.path.exists(csv_path) or create_missing:
            body, csv_kept = patch_csv(csv_path, incoming, overwrite)
            kept += csv_kept
            if body is not None:
                touched.append(csv_path)
                if not dry_run:
                    os.makedirs(os.path.dirname(csv_path), exist_ok=True)
                    with open(csv_path, "wb") as f:
                        f.write(body)
        else:
            print("  ! no CSV at %s (use --create-missing)" % csv_path)

        asset_path = os.path.join(assets_root, code, tab + ".asset")
        raw, current = S.read_asset(asset_path)
        if raw is None:
            if not create_missing:
                print("  ! no asset at %s (use --create-missing)" % asset_path)
                continue
            raw, current = S.asset_template(tab), {}
        out = dict(current)
        for k, v in incoming.items():
            if k not in out or overwrite:
                if k not in out:
                    added += 1
                out[k] = v
        if out != current:
            touched.append(asset_path)
            if not dry_run:
                os.makedirs(os.path.dirname(asset_path), exist_ok=True)
                S.write_asset(asset_path, raw, out)
    return added, kept, touched


def load_result(path):
    """The localize_add_keys result: the whole object, its `apply` block, or the list."""
    try:
        data = json.load(open(path, encoding="utf-8"))
    except (OSError, ValueError) as e:
        raise SystemExit("cannot read %s as JSON: %s" % (path, e))
    if isinstance(data, dict) and data.get("stageOnly"):
        raise SystemExit("that result is from stage_only - nothing was written "
                         "to the sheet, so there is nothing to apply yet")
    if isinstance(data, dict) and "apply" in data:
        data = data["apply"]
    if isinstance(data, dict):
        data = data.get("entries")
    if not isinstance(data, list):
        raise SystemExit("%s is not a localize_add_keys result (no apply.entries)"
                         % path)
    out = []
    for i, d in enumerate(data):
        values = d.get("values") if isinstance(d, dict) else None
        if not d.get("key") or not d.get("tab") or not isinstance(values, dict):
            raise SystemExit("apply entry %d needs key, tab and values" % i)
        missing = [c for c in ALL_LANGS if not str(values.get(c, "")).strip()]
        if missing:
            print("  ! %s: no text for %s - those languages are left out"
                  % (d["key"], ",".join(missing)))
        out.append({"key": normalise_key(d["key"]), "tab": d["tab"],
                    "resolved": {c: str(values.get(c, "")) for c in ALL_LANGS}})
    return out


def apply(args, S):
    entries = load_result(args.apply)
    if not entries:
        print("Nothing to apply - the sheet step wrote no new key "
              "(already present in its tab?).")
        return
    tabs = sorted({e["tab"] for e in entries})
    check_tabs(tabs, args.csv_root, args.allow_any_tab or args.create_missing)
    if args.dry_run:
        print("DRY RUN - nothing is written")
    for tab in tabs:
        group = [e for e in entries if e["tab"] == tab]
        added, kept, touched = apply_to_project(
            S, tab, group, args.csv_root, args.assets, args.overwrite,
            args.create_missing, args.dry_run)
        print("project  %-12s %s: +%d asset entr(ies), %d existing value(s) "
              "kept, %d file(s) %s"
              % (tab, ", ".join(e["key"] for e in group), added, kept,
                 len(touched), "would change" if args.dry_run else "written"))
    if not args.dry_run:
        print("Open the Editor once so it reimports the touched CSV/.asset files.")


# --------------------------------------------------------------------------
def main():
    ap = argparse.ArgumentParser(
        description=__doc__,
        formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--key")
    ap.add_argument("--en")
    ap.add_argument("--vi", default="")
    for code in sorted(GT_CODE):
        ap.add_argument("--" + code, default="",
                        help="hand-authored %s text (skips GOOGLETRANSLATE)" % code)
    ap.add_argument("--file", help="JSON list of entries for a batch")
    ap.add_argument("--tab", default=DEFAULT_TAB,
                    help="target tab / runtime table (default: %s)" % DEFAULT_TAB)
    ap.add_argument("--sheet", default=os.environ.get("LOCALIZE_SHEET",
                                                      PROFILE["sheet"]))
    ap.add_argument("--csv-root", default=under_root(PROFILE["csvRoot"]))
    ap.add_argument("--assets", default=under_root(PROFILE["assetsRoot"]))
    ap.add_argument("--timeout", type=int, default=0,
                    help="seconds the server waits for GOOGLETRANSLATE "
                         "(default 60, max 120)")
    ap.add_argument("--allow-any-tab", action="store_true",
                    help="permit tabs that have no CSV table in this project")
    ap.add_argument("--force", action="store_true",
                    help="write the sheet row even if the key is already there")
    ap.add_argument("--stage-only", action="store_true",
                    help="only stage in `%s` and return the translations - "
                         "nothing is written to a real tab or to the project"
                         % STAGING_TAB)
    ap.add_argument("--apply", metavar="RESULT_JSON",
                    help="merge a localize_add_keys result into the project")
    ap.add_argument("--overwrite", action="store_true",
                    help="(--apply) replace the project's existing copy for these keys")
    ap.add_argument("--create-missing", action="store_true",
                    help="(--apply) create the CSV/.asset of a table this project lacks")
    ap.add_argument("--dry-run", action="store_true",
                    help="(--apply) report what would change, write nothing")
    args = ap.parse_args()

    S = load_sheet_module()
    if args.apply:
        apply(args, S)
    else:
        plan(args, S)


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    main()
