#!/usr/bin/env python3
"""project_profile — the per-project knobs the agent system reads.

WHY THIS EXISTS
---------------
The backlog/planning/mockup toolchain is shared across projects: this repo is
the upstream where it is exercised daily, and `ezg-packages/templates/
unity-project` ships it to new projects. Sharing only works if the files are
byte-identical everywhere, because the template's sync copies them wholesale —
anything hand-edited on the template side is overwritten on the next sync.

So the project-specific values live in ONE data file, `.claude/project-profile.json`,
and every script reads them from here. A project changes the profile, never the
scripts.

CONTRACT
--------
Every key has a default that reproduces this repo's historical behaviour, so a
tree with no profile at all behaves exactly as it did before the profile
existed. That matters: it means adding this indirection cannot change what the
preflight flags or what the loop does here, and a project that only cares about
two keys writes only those two.

Markdown consumers (SKILL.md, agents/*.md) cannot import this module — a model
reads them. Those files name `.claude/project-profile.json` and the key they
need, and the model resolves it. Same single source, different reader.

USAGE
-----
    from project_profile import profile
    globs = profile().sensitive_globs
"""

# macOS still ships Python 3.9, where `Path | None` is a TypeError at class
# definition time rather than a 3.10 union. Deferring annotation evaluation
# keeps the modern spelling readable and the module importable on the stock
# interpreter every dev already has.
from __future__ import annotations

import json
import subprocess
from pathlib import Path


# Defaults == this repo's behaviour before the profile existed. Do not "clean
# these up" to generic values: a project with no profile.json must keep working
# exactly as it did, and these are what the tests pin.
DEFAULTS = {
    # --- identity -----------------------------------------------------------
    "projectName": "BlazeSurvivor",
    "solutionFile": "BlazeSurvivor.sln",
    # git config namespace for loop bookkeeping (<prefix>.agentBaseBranch)
    "gitConfigPrefix": "blazesurvivor",
    # last-resort base branch when HEAD is an agent branch and git config is empty
    "defaultBaseBranch": "Android/Release",

    # --- source layout ------------------------------------------------------
    "sourceRoot": "Assets/_Game",
    "featuresRoot": "Assets/_Game/2.BUS/Features",
    "gameplayRoot": "Assets/_Game/2.BUS/GamePlay",
    # where ui-kit-sync.py reads the screen template prefabs
    "uiTemplatesRoot": "Assets/Resources/Prefabs/Templates",

    # --- review surfaces ----------------------------------------------------
    # Filename globs that make a diff "sensitive" and auto-spawn the
    # security-auditor. Broad on purpose here: this project has a real backend,
    # a leaderboard and IAP. A project with none of that should trim the list
    # rather than inherit false positives.
    "sensitiveGlobs": [
        "*Backend*", "*Supabase*", "*Cloudflare*", "*Worker*",
        "*Purchase*", "*IAP*", "*Receipt*",
        "*DataPlayer*", "*SaveData*", "*PlayerPrefs*", "*Persistence*",
        "*Auth*", "*Login*", "*Token*", "*Session*",
        "*Leaderboard*", "*Ranking*", "*Social*",
        "*AntiCheat*", "*Validation*", "*Integrity*",
        "*.env*", "*.config", "*Secrets*",
    ],

    # --- backend shape ------------------------------------------------------
    # Drives the backend-write rules. "kind" is free-form; the rules key off
    # `directWriteBanned`, which says client code must not mutate the datastore
    # directly. Set kind to "none" in a project with no backend and the
    # matching preflight rule stops firing.
    "backend": {
        "kind": "supabase+cloudflare-worker",
        "directWriteBanned": True,
        # regex (python) matching a banned direct client write
        "directWritePattern":
            r"(?i)supabase\s*\.\s*from\s*\([^\)]*\)\s*\.\s*(insert|update|upsert|delete)\b",
        "directWriteAdvice":
            "Client writes must go through Cloudflare Worker, not direct Supabase mutation.",
    },
}

_PROFILE_FILENAME = "project-profile.json"


def _repo_root() -> Path:
    """Repo root, whether we are in a worktree or the main checkout."""
    try:
        out = subprocess.run(
            ["git", "rev-parse", "--show-toplevel"],
            capture_output=True, text=True, check=True,
        ).stdout.strip()
        if out:
            return Path(out)
    except (OSError, subprocess.CalledProcessError):
        pass
    # Fall back to the tree this file sits in: <root>/.claude/scripts/x.py
    return Path(__file__).resolve().parents[2]


class Profile:
    """Read-only view over the merged profile.

    Merge is shallow per top-level key, except `backend`, which merges per
    sub-key. That way a project overriding only `backend.kind` keeps the
    default write-pattern instead of silently losing the rule.
    """

    def __init__(self, data: dict, source: Path | None = None):
        self._data = data
        self.source = source

    # -- raw access ---------------------------------------------------------
    def get(self, key, default=None):
        return self._data.get(key, DEFAULTS.get(key, default))

    # -- identity -----------------------------------------------------------
    @property
    def project_name(self) -> str:
        return self.get("projectName")

    @property
    def solution_file(self) -> str:
        return self.get("solutionFile")

    @property
    def git_config_prefix(self) -> str:
        return self.get("gitConfigPrefix")

    @property
    def default_base_branch(self) -> str:
        return self.get("defaultBaseBranch")

    # -- layout -------------------------------------------------------------
    @property
    def source_root(self) -> str:
        return self.get("sourceRoot")

    @property
    def features_root(self) -> str:
        return self.get("featuresRoot")

    @property
    def gameplay_root(self) -> str:
        return self.get("gameplayRoot")

    @property
    def ui_templates_root(self) -> str:
        return self.get("uiTemplatesRoot")

    # -- review surfaces ----------------------------------------------------
    @property
    def sensitive_globs(self) -> list:
        return list(self.get("sensitiveGlobs"))

    # -- backend ------------------------------------------------------------
    @property
    def backend(self) -> dict:
        merged = dict(DEFAULTS["backend"])
        merged.update(self._data.get("backend") or {})
        return merged

    @property
    def backend_direct_write_banned(self) -> bool:
        return bool(self.backend.get("directWriteBanned"))

    @property
    def backend_direct_write_pattern(self) -> str:
        return self.backend.get("directWritePattern") or ""

    @property
    def backend_direct_write_advice(self) -> str:
        return self.backend.get("directWriteAdvice") or ""


_cached: Profile | None = None


def profile(refresh: bool = False) -> Profile:
    """The active profile. Cached — scripts call this in hot loops."""
    global _cached
    if _cached is not None and not refresh:
        return _cached

    # Search order matters.
    #
    # First: beside this module. It lives in `<agent-dir>/scripts/`, so the
    # profile is its parent. This is exact — it does not care what the git root
    # is, which is the difference between working and not when the agent tree
    # sits in a subdirectory of a larger repo (the template ships from
    # `ezg-packages/templates/unity-project/DefaultSetup/`, where the git root
    # is four levels up and holds someone else's profile, or none).
    #
    # Then: the repo root, both spellings. `.claude/` leads because the template
    # inverts the canonical direction — a generated project keeps real files
    # there and `.claude/` is the link view. One module, both layouts.
    here = Path(__file__).resolve().parent.parent
    root = _repo_root()
    candidates = [here / _PROFILE_FILENAME]
    candidates += [root / folder / _PROFILE_FILENAME for folder in (".claude", ".claude")]

    for candidate in candidates:
        if candidate.is_file():
            try:
                data = json.loads(candidate.read_text(encoding="utf-8"))
            except (OSError, ValueError) as exc:
                # A malformed profile must be loud. Falling back to defaults
                # would silently apply the wrong project's review surfaces.
                raise SystemExit(f"{candidate}: cannot parse profile — {exc}")
            _cached = Profile(data, candidate)
            return _cached

    _cached = Profile({}, None)
    return _cached


if __name__ == "__main__":
    import sys

    p = profile()
    if len(sys.argv) > 1:
        # `project_profile.py sourceRoot` — for shell callers.
        value = p.get(sys.argv[1])
        if isinstance(value, (dict, list)):
            print(json.dumps(value))
        elif value is None:
            sys.exit(f"unknown profile key: {sys.argv[1]}")
        else:
            print(value)
    else:
        print(json.dumps({
            "source": str(p.source) if p.source else "(defaults — no profile file)",
            "projectName": p.project_name,
            "solutionFile": p.solution_file,
            "gitConfigPrefix": p.git_config_prefix,
            "defaultBaseBranch": p.default_base_branch,
            "sourceRoot": p.source_root,
            "featuresRoot": p.features_root,
            "gameplayRoot": p.gameplay_root,
            "uiTemplatesRoot": p.ui_templates_root,
            "sensitiveGlobs": p.sensitive_globs,
            "backend": p.backend,
        }, indent=2))
