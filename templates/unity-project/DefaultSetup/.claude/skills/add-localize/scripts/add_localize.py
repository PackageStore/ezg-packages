#!/usr/bin/env python3
"""Add one (or a batch of) localization key(s) to the team sheet, then into the project.

Three steps, one command:

  1. stage    write the row into the `template` tab: key | FALSE | English |
              =C<row> | Vietnamese | =GOOGLETRANSLATE(C<row>, "en", <code>) x14
  2. harvest  poll the staging row until Sheets has evaluated every formula,
              then write the resulting STATIC text into the target tab
  3. pull     write only those keys into LocalizationData/<lang>/<tab>.csv and
              Resources/LocalizationData/<lang>/<tab>.asset (additive)

Why go through `template`: the real tabs of this sheet hold plain text only -
every language cell there is static. `template` is the staging area the team
already uses (`=C<row>` in English-en, GOOGLETRANSLATE in the rest), so the
machine translation happens where the convention says it happens and the real
tab still receives text.

Scope: this adds keys. It does NOT scan prefabs for hardcoded strings and does
NOT attach the localize component - that is `fill-localize`.

Usage
-----
  python3 .claude/skills/add-localize/scripts/add_localize.py \
      --key ui_play --en "Play" --vi "Choi" [--tab common]

  python3 .claude/skills/add-localize/scripts/add_localize.py --file keys.json

  keys.json: [{"key": "ui_play", "en": "Play", "vi": "Choi", "tab": "shop"}, ...]

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
import time
import urllib.parse

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
        "serviceAccount": (loc.get("serviceAccount")
                           or "Key/service-account-gsheets.json"),
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

# project language folder -> GOOGLETRANSLATE code.
# `en` is the source and `vi` is authored by hand, so neither is translated.
GT_CODE = {
    "pt": "pt", "th": "th", "zhcn": "zh-cn", "ko": "ko", "ja": "ja",
    "de": "de", "it": "it", "es": "es", "id": "id", "ru": "ru",
    "fr": "fr", "nl": "nl", "tr": "tr", "pl": "pl",
}
AUTHORED = ("en", "vi")
ALL_LANGS = tuple(AUTHORED) + tuple(sorted(GT_CODE))
# `vi` is meant to be authored, like `en`. Left empty it would stage a blank
# cell that never resolves, so it falls back to machine translation with a
# warning rather than hanging until the timeout.
GT_FALLBACK = dict(GT_CODE, vi="vi")

ERROR_CELLS = ("#ERROR!", "#N/A", "#VALUE!", "#REF!", "#NAME?", "Loading...")


# --------------------------------------------------------------------------
# Sheets auth + the LanguageData .asset reader/writer live next to this file in
# sheet_io.py, so the skill works in a project that has nothing else installed.
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


def col_letter(i):
    """0 -> A, 25 -> Z, 26 -> AA."""
    s = ""
    i += 1
    while i:
        i, r = divmod(i - 1, 26)
        s = chr(65 + r) + s
    return s


def q(tab):
    return urllib.parse.quote("'%s'" % tab)


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
            raise SystemExit("--key and --en are required (or use --file)")
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


# --------------------------------------------------------------------------
# Sheet side
# --------------------------------------------------------------------------
def tab_header(S, token, sid, tab):
    r = S.api(token, "GET", "%s/values/%s!A1:BZ1" % (sid, q(tab)))
    rows = r.get("values", [])
    return rows[0] if rows else []


def lang_columns(S, header):
    """{project lang code: column index} from a `Vietnamese-vi` style header."""
    out = {}
    for i, h in enumerate(header):
        m = S.LANG_COL_RE.match(h.strip())
        if m:
            out.setdefault(m.group(2), i)
    return out


def origin_column(header):
    for i, h in enumerate(header):
        if h.strip().lower().startswith("english(origin"):
            return i
    return 2


def first_free_row(S, token, sid, tab, row_count):
    r = S.api(token, "GET", "%s/values/%s!A1:A%d" % (sid, q(tab), row_count))
    return len(r.get("values", [])) + 1


def existing_keys(S, token, sid, tab, row_count):
    r = S.api(token, "GET", "%s/values/%s!A1:A%d" % (sid, q(tab), row_count))
    return {(row[0] or "").strip() for row in r.get("values", []) if row}


def ensure_rows(S, token, sid, props, tab, needed):
    have = props[tab]["gridProperties"]["rowCount"]
    if needed <= have:
        return
    S.api(token, "POST", "%s:batchUpdate" % sid, {"requests": [{
        "appendDimension": {"sheetId": props[tab]["sheetId"],
                            "dimension": "ROWS", "length": needed - have + 50},
    }]})
    props[tab]["gridProperties"]["rowCount"] = needed + 50


def write_staging(S, token, sid, header, entries, first_row):
    """Lay the entries into `template`; return the row number of each."""
    langs = lang_columns(S, header)
    origin = origin_column(header)
    width = max(len(header), max(langs.values()) + 1 if langs else 4)
    origin_letter = col_letter(origin)

    raw_rows, formula_rows = [], []
    for n, e in enumerate(entries):
        row_no = first_row + n
        raw = [""] * width
        formula = [""] * width
        raw[0] = e["key"]
        raw[origin] = e["values"]["en"]
        formula[1] = "FALSE"
        for code, col in langs.items():
            manual = e["values"].get(code, "")
            if code == "en":
                formula[col] = "=%s%d" % (origin_letter, row_no)
            elif manual:
                raw[col] = manual
            elif code in GT_FALLBACK:
                formula[col] = '=GOOGLETRANSLATE(%s%d, "en", "%s")' % (
                    origin_letter, row_no, GT_FALLBACK[code])
        raw_rows.append(raw)
        formula_rows.append(formula)
        e["staging_row"] = row_no

    last_row = first_row + len(entries) - 1
    rng = "%s!A%d:%s%d" % ("'%s'" % STAGING_TAB, first_row,
                           col_letter(width - 1), last_row)
    # RAW first: a value starting with =, + or - must not become a formula.
    S.api(token, "POST", "%s/values:batchUpdate" % sid, {
        "valueInputOption": "RAW",
        "data": [{"range": rng, "values": raw_rows}],
    })
    # Then the formula cells, which must be evaluated. Blank entries in this
    # payload would wipe the RAW text, so only non-empty cells are sent.
    data = []
    for n, formula in enumerate(formula_rows):
        for col, val in enumerate(formula):
            if val:
                data.append({
                    "range": "'%s'!%s%d" % (STAGING_TAB, col_letter(col),
                                            first_row + n),
                    "values": [[val]],
                })
    if data:
        S.api(token, "POST", "%s/values:batchUpdate" % sid,
              {"valueInputOption": "USER_ENTERED", "data": data})
    return langs


def harvest(S, token, sid, header, entries, timeout):
    """Read the staging rows back until every formula has resolved."""
    langs = lang_columns(S, header)
    rows = [e["staging_row"] for e in entries]
    lo, hi = min(rows), max(rows)
    width = col_letter(max(len(header), max(langs.values()) + 1) - 1)
    deadline = time.time() + timeout
    delay = 1.5
    while True:
        r = S.api(token, "GET",
                  "%s/values/%s!A%d:%s%d?valueRenderOption=FORMATTED_VALUE"
                  % (sid, q(STAGING_TAB), lo, width, hi))
        grid = r.get("values", [])
        pending = []
        for e in entries:
            idx = e["staging_row"] - lo
            row = grid[idx] if idx < len(grid) else []
            got = {}
            for code, col in langs.items():
                if code not in ALL_LANGS:
                    continue          # a language column this project does not ship
                cell = (row[col] if col < len(row) else "") or ""
                cell = cell.strip()
                if cell == "" or cell in ERROR_CELLS:
                    pending.append("%s/%s" % (e["key"], code))
                got[code] = cell
            e["resolved"] = got
        if not pending:
            return
        if time.time() >= deadline:
            raise SystemExit(
                "GOOGLETRANSLATE did not resolve within %ds: %s\n"
                "The staging rows are written in `%s` - open the sheet, wait for "
                "them, then rerun with --from-staging to continue."
                % (timeout, ", ".join(pending[:12]), STAGING_TAB))
        time.sleep(delay)
        delay = min(delay * 1.6, 10)


def write_target(S, token, sid, props, tab, entries, force):
    header = tab_header(S, token, sid, tab)
    if not header:
        raise SystemExit("tab %r has no header row" % tab)
    langs = lang_columns(S, header)
    missing = [c for c in ALL_LANGS if c not in langs]
    if missing:
        raise SystemExit("tab %r is missing language columns: %s"
                         % (tab, ", ".join(missing)))
    origin = origin_column(header)
    width = max(len(header), max(langs.values()) + 1)

    have = existing_keys(S, token, sid, tab,
                         props[tab]["gridProperties"]["rowCount"])
    todo, skipped = [], []
    for e in entries:
        if e["key"] in have and not force:
            skipped.append(e["key"])
            continue
        todo.append(e)
    if not todo:
        return [], skipped

    values = []
    for e in todo:
        row = [""] * width
        row[0] = e["key"]
        if width > 1:
            row[1] = "FALSE"
        row[origin] = e["resolved"].get("en") or e["values"]["en"]
        for code, col in langs.items():
            row[col] = e["resolved"].get(code, "")
        values.append(row)

    first = first_free_row(S, token, sid, tab,
                           props[tab]["gridProperties"]["rowCount"])
    ensure_rows(S, token, sid, props, tab, first + len(values) - 1)
    S.api(token, "PUT",
          "%s/values/%s!A%d?valueInputOption=RAW" % (sid, q(tab), first),
          {"values": values})
    for n, e in enumerate(todo):
        e["target_row"] = first + n
    return todo, skipped


# --------------------------------------------------------------------------
# Project side - only the keys this run added, nothing else
# --------------------------------------------------------------------------
def csv_newline(path):
    """Keep the line ending the file already uses.

    `fill-localize pull` hardcodes CRLF. That is invisible on a checkout whose
    git normalises line endings and very visible on one that does not: rewriting
    a 458-line table in the other ending turns "added one key" into a diff
    touching every row. Adding one key should show up as one line.
    """
    if os.path.exists(path):
        with open(path, "rb") as f:
            head = f.read(65536)
        if b"\r\n" in head:
            return "\r\n"
        if b"\n" in head:
            return "\n"
    return "\n"


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
            merged = S.read_project_csv(csv_path)
            before = dict(merged)
            for k, v in incoming.items():
                if k not in merged or overwrite:
                    merged[k] = v
                else:
                    kept += 1
            if merged != before and not dry_run:
                os.makedirs(os.path.dirname(csv_path), exist_ok=True)
                body = "key~value\n" + "".join(
                    "%s~%s\n" % (k, v) for k, v in merged.items())
                open(csv_path, "w", encoding="utf-8",
                     newline=csv_newline(csv_path)).write(body)
                touched.append(csv_path)
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
        if out != current and not dry_run:
            os.makedirs(os.path.dirname(asset_path), exist_ok=True)
            S.write_asset(asset_path, raw, out)
            touched.append(asset_path)
    return added, kept, touched


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
    ap.add_argument("--service-account",
                    default=under_root(PROFILE["serviceAccount"]))
    ap.add_argument("--csv-root", default=under_root(PROFILE["csvRoot"]))
    ap.add_argument("--assets", default=under_root(PROFILE["assetsRoot"]))
    ap.add_argument("--timeout", type=int, default=90,
                    help="seconds to wait for GOOGLETRANSLATE (default 90)")
    ap.add_argument("--allow-any-tab", action="store_true",
                    help="permit tabs that have no CSV table in this project")
    ap.add_argument("--force", action="store_true",
                    help="write the sheet row even if the key is already there")
    ap.add_argument("--overwrite", action="store_true",
                    help="replace the project's existing copy for these keys")
    ap.add_argument("--create-missing", action="store_true")
    ap.add_argument("--no-pull", action="store_true",
                    help="stop after the sheet; do not touch CSV/.asset")
    ap.add_argument("--stage-only", action="store_true",
                    help="stage in `%s` and print the translations, then stop - "
                         "nothing is written to a real tab or to the project"
                         % STAGING_TAB)
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    if not args.sheet:
        raise SystemExit(
            "no localization sheet configured - add a `localize` block to "
            "%s:\n"
            '  "localize": { "sheet": "https://docs.google.com/spreadsheets/d/..." }\n'
            "or pass --sheet / set LOCALIZE_SHEET."
            % os.path.join(ROOT, ".claude", "project-profile.json"))

    S = load_sheet_module()
    entries = load_entries(args)

    tabs = sorted({e["tab"] for e in entries})
    for tab in tabs:
        if tab == STAGING_TAB:
            raise SystemExit("`%s` is the staging tab - pick a real table"
                             % STAGING_TAB)
        if not args.allow_any_tab and not os.path.exists(
                os.path.join(args.csv_root, "en", tab + ".csv")):
            raise SystemExit(
                "tab %r has no table in this project (%s/en/%s.csv). "
                "Tables here: %s. Use --allow-any-tab to override."
                % (tab, args.csv_root, tab,
                   ", ".join(sorted(f[:-4] for f in os.listdir(
                       os.path.join(args.csv_root, "en"))
                       if f.endswith(".csv")))))

    if args.dry_run:
        print("DRY RUN - nothing is written")
        for e in entries:
            auto = [c for c in sorted(GT_FALLBACK) if not e["values"][c]]
            print("  %-14s %-28s en=%r vi=%r auto=%s"
                  % (e["tab"], e["key"], e["values"]["en"], e["values"]["vi"],
                     ",".join(auto) or "-"))
        return

    sid = S.sheet_id(args.sheet)
    token = S.service_account_token(args.service_account)
    meta = S.api(token, "GET", "%s?fields=sheets.properties" % sid)
    props = {s["properties"]["title"]: s["properties"] for s in meta["sheets"]}
    for tab in tabs + [STAGING_TAB]:
        if tab not in props:
            raise SystemExit("sheet has no tab %r (have: %s)"
                             % (tab, ", ".join(sorted(props))))

    staging_header = tab_header(S, token, sid, STAGING_TAB)
    if not staging_header:
        raise SystemExit("staging tab %r has no header row" % STAGING_TAB)

    first = first_free_row(S, token, sid, STAGING_TAB,
                           props[STAGING_TAB]["gridProperties"]["rowCount"])
    ensure_rows(S, token, sid, props, STAGING_TAB, first + len(entries) - 1)
    write_staging(S, token, sid, staging_header, entries, first)
    print("staged   %d row(s) in `%s` at row %d" % (len(entries), STAGING_TAB,
                                                    first))

    harvest(S, token, sid, staging_header, entries, args.timeout)
    print("resolved %d row(s) - every language column evaluated" % len(entries))

    if args.stage_only:
        for e in entries:
            print("\n%s  (staged at %s!A%d)"
                  % (e["key"], STAGING_TAB, e["staging_row"]))
            for code in ALL_LANGS:
                print("    %-5s %s" % (code, e["resolved"].get(code, "")))
        print("\n--stage-only: nothing written to a real tab or to the project.")
        return

    for tab in tabs:
        group = [e for e in entries if e["tab"] == tab]
        written, skipped = write_target(S, token, sid, props, tab, group,
                                        args.force)
        if written:
            print("sheet    %-12s +%d row(s) at row %d"
                  % (tab, len(written), written[0]["target_row"]))
        if skipped:
            print("sheet    %-12s skipped (already present): %s"
                  % (tab, ", ".join(skipped)))

        if args.no_pull:
            continue
        pull_set = group if args.force else [e for e in group
                                             if e["key"] not in skipped]
        if not pull_set:
            continue
        added, kept, touched = apply_to_project(
            S, tab, pull_set, args.csv_root, args.assets, args.overwrite,
            args.create_missing, False)
        print("project  %-12s +%d asset entr(ies), %d existing value(s) kept, "
              "%d file(s) written" % (tab, added, kept, len(touched)))

    print("\nThe staging rows stay in `%s` on purpose - that tab mirrors every "
          "key by convention." % STAGING_TAB)
    if not args.no_pull:
        print("Open the Editor once so it reimports the touched CSV/.asset files.")


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    main()
