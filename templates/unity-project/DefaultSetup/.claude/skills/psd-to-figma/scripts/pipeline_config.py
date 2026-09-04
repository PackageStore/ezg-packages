#!/usr/bin/env python3
"""Shared path/settings resolution for the psd2figma pipeline scripts.

Every stage imports this so it can relocate (into a Claude skill's scripts/
directory) without breaking, while its generated output and project data stay
in a data directory that this module locates at runtime.

    from pipeline_config import resolve
    cfg, argv = resolve()   # consumes --data-dir / --project-root, returns the rest

`argv` is the remaining arguments for the caller's own argparse, so each script
keeps its existing flags (--screen, --structure-only, ...) working unchanged.

The first run against a data dir writes `<data>/.gitignore` covering the
generated artifacts (PNG dirs, indexes, reports, runner state, lock files) so a
project's git tree never carries them. The file is created once and never
rewritten; edit it freely.

Hard rule: Path(__file__) is NEVER used here to locate data, the project root,
or the PSD directory. Data is anchored on the resolved data directory; scripts
may use their own Path(__file__) only to find script siblings (e.g. a .js).
"""

import argparse
import json
import os
import sys
from pathlib import Path

CONFIG_NAME = "psd2figma.json"

DATA_GITIGNORE = """# Written once by the psd-to-figma pipeline: generated artifacts only.
# Project tables (psd2figma.json, screens.json, node_names.json, text_styles.json,
# accepted_debt.json, component_ids.json, nine_slice.json, image_hashes.json,
# style_ids*.json) and the gate inputs (psd_manifest.json, figma_extract_*.json)
# stay tracked. Edit freely; the scripts never rewrite this file.
assets/
icons/
diff/
.pipeline/
.progress/
*.lock
assets_index.json
icons_index.json
verify_report.md
verify_report.json
diff_report.md
"""


def _fatal(message):
    sys.stderr.write("pipeline_config: " + message + "\n")
    sys.exit(2)


def _abs(path_str, base):
    p = Path(path_str).expanduser()
    if not p.is_absolute():
        p = base / p
    return p.resolve()


def _walk_up_for_config(start):
    cur = start.resolve()
    while True:
        if (cur / CONFIG_NAME).is_file():
            return cur
        if cur.parent == cur:
            return None
        cur = cur.parent


class Config:
    def __init__(self, data_dir, project_root, psd_dir, settings):
        self.data_dir = data_dir
        self.project_root = project_root
        self.psd_dir = psd_dir
        self.settings = settings

    def path(self, *parts):
        return self.data_dir.joinpath(*parts)

    def _filename(self, name):
        tables = self.settings.get("tables", {})
        return tables.get(name, name)

    def load(self, name):
        p = self.data_dir / self._filename(name)
        if not p.is_file():
            raise SystemExit(
                f"pipeline_config: required data file not found: {p} "
                f"(requested as {name!r})"
            )
        return json.loads(p.read_text(encoding="utf-8"))

    def load_optional(self, name, default=None):
        p = self.data_dir / self._filename(name)
        if not p.is_file():
            return default
        return json.loads(p.read_text(encoding="utf-8"))


def ensure_data_gitignore(data_dir):
    p = data_dir / ".gitignore"
    if not p.exists():
        p.write_text(DATA_GITIGNORE, encoding="utf-8")


def resolve(argv=None):
    if argv is None:
        argv = sys.argv[1:]

    parser = argparse.ArgumentParser(add_help=False)
    parser.add_argument("--data-dir")
    parser.add_argument("--project-root")
    ns, remaining = parser.parse_known_args(argv)

    cwd = Path.cwd()

    if ns.data_dir:
        data_dir = _abs(ns.data_dir, cwd)
        data_src = "--data-dir"
    elif os.environ.get("PSD2FIGMA_DATA_DIR"):
        data_dir = _abs(os.environ["PSD2FIGMA_DATA_DIR"], cwd)
        data_src = "PSD2FIGMA_DATA_DIR"
    else:
        found = _walk_up_for_config(cwd)
        if found is None:
            _fatal(
                f"cannot locate {CONFIG_NAME}. Pass --data-dir <dir>, set "
                "PSD2FIGMA_DATA_DIR, or run inside a tree that contains "
                f"{CONFIG_NAME}."
            )
        data_dir = found
        data_src = "walk-up"

    config_path = data_dir / CONFIG_NAME
    if not config_path.is_file():
        _fatal(
            f"no {CONFIG_NAME} in data dir {data_dir} (from {data_src}). "
            "Point --data-dir / PSD2FIGMA_DATA_DIR at the directory that holds "
            f"{CONFIG_NAME}."
        )
    settings = json.loads(config_path.read_text(encoding="utf-8"))
    ensure_data_gitignore(data_dir)

    if ns.project_root:
        project_root = _abs(ns.project_root, cwd)
    elif os.environ.get("PSD2FIGMA_PROJECT_ROOT"):
        project_root = _abs(os.environ["PSD2FIGMA_PROJECT_ROOT"], cwd)
    else:
        pr = settings.get("paths", {}).get("projectRoot")
        if not pr:
            _fatal(
                "cannot determine the project root. Pass --project-root <dir>, "
                "set PSD2FIGMA_PROJECT_ROOT, or add paths.projectRoot to "
                f"{config_path}."
            )
        project_root = _abs(pr, data_dir)

    psd = settings.get("paths", {}).get("psdDir")
    if not psd:
        _fatal(
            f"no paths.psdDir in {config_path}; add it (relative to the "
            "project root)."
        )
    psd_dir = _abs(psd, project_root)

    return Config(data_dir, project_root, psd_dir, settings), remaining


if __name__ == "__main__":
    cfg, remaining = resolve()
    print(json.dumps({
        "data_dir": str(cfg.data_dir),
        "project_root": str(cfg.project_root),
        "psd_dir": str(cfg.psd_dir),
        "exists": {
            "data_dir": cfg.data_dir.is_dir(),
            "project_root": cfg.project_root.is_dir(),
            "psd_dir": cfg.psd_dir.is_dir(),
        },
        "remaining_argv": remaining,
        "settings": cfg.settings,
    }, indent=2, ensure_ascii=False))
