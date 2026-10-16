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
import re
import subprocess
from pathlib import Path


# Defaults == the base template's own shape, and they mirror the shipped
# `.claude/project-profile.json` key for key. Two reasons that matters: a
# checkout whose profile went missing keeps behaving like the template it came
# from instead of like some other game, and there is exactly one place to read
# to know what a value means. Do NOT put a specific project's layout, branch or
# threat surface here — that belongs in that project's profile.json.
DEFAULTS = {
    # --- identity -----------------------------------------------------------
    # Neutral on purpose: build_unity_template writes the real name into
    # project-profile.json at generation time. These only surface if that file
    # is missing, where a generic label beats another project's name.
    "projectName": "UnityProject",
    # Human-readable name for Discord notification titles ("My Game"). Empty =
    # projectName split at its CamelCase humps, so a generated project gets a
    # readable name without touching its profile.
    "displayName": "",
    "solutionFile": "UnityProject.sln",
    # git config namespace for loop bookkeeping (<prefix>.agentBaseBranch)
    "gitConfigPrefix": "agent",
    # last-resort base branch when HEAD is an agent branch and git config is empty
    "defaultBaseBranch": "main",
    # EZG Feature Hub project code (Features tab), e.g. "M001". Empty on purpose:
    # /publish-feature refuses to guess it and makes the dev pick instead.
    "featureHubProjectId": "",

    # --- source layout ------------------------------------------------------
    "sourceRoot": "Assets/_Project",
    "featuresRoot": "Assets/_Project/Features",
    "gameplayRoot": "Assets/_Project/Features",
    # where ui-kit-sync.py reads the screen template prefabs
    "uiTemplatesRoot": "Assets/_Project/Visual/ArtAsset/Shared/Resources/Prefabs/Templates",
    # where create-vfx writes generated VFX (<root>/<fx_name>/ + <root>/_Shared/)
    "vfxRoot": "Assets/_Project/Visual/ArtAsset/Shared/VFX/Generated",
    # where create-sfx writes generated sounds (<root>/<Name>/), the SoundConfig asset its --config wires, and the
    # List<SoundPlayCustomModel> field per --event on the game's own sound component (empty = SoundPlayController's)
    "sfxRoot": "Assets/_Project/Visual/ArtAsset/Shared/Sounds/Generated",
    "soundConfigAsset": "Assets/_Project/Features/_Shared/Resources/SoundConfig.asset",
    "sfxEventFields": {},

    # --- review surfaces ----------------------------------------------------
    # Filename globs that make a diff "sensitive" and auto-spawn the
    # security-auditor. Scoped to what the base template actually ships: IAP,
    # account sync and a two-tier save. Backend/leaderboard/anti-cheat globs are
    # deliberately absent — a project that grows one adds them to its own
    # profile.json rather than every diff dragging in a reviewer with nothing
    # to review.
    "sensitiveGlobs": [
        "*Purchase*", "*IAP*", "*Receipt*", "*Payment*",
        "*DataPlayer*", "*SaveData*", "*PlayerPrefs*", "*Persistence*",
        "*Auth*", "*Login*", "*Token*", "*Session*", "*Account*",
        "*.env*", "*.config", "*Secrets*", "*Credential*",
    ],
    # Repo-relative paths exempt from the preflight PlayerPrefs rule: stores that
    # are device-local BY DESIGN (never cloud-synced), so PlayerPrefs is the
    # spec, not a violation. Empty by default — every entry must be justified in
    # the project's design docs. Exact paths, no globs.
    "playerPrefsAllowedFiles": [],

    # --- backend shape ------------------------------------------------------
    # Drives the backend-write rules. "kind" is free-form; the rules key off
    # `directWriteBanned`, which says client code must not mutate the datastore
    # directly. The base template ships no backend, so the rule is off; a
    # project that lands one sets kind + directWriteBanned + a
    # directWritePattern matching the call its client must not make.
    "backend": {
        "kind": "none",
        "directWriteBanned": False,
        # regex (python) matching a banned direct client write
        "directWritePattern": "",
        "directWriteAdvice": "",
    },

    # --- BugHub -------------------------------------------------------------
    # Mã project trên server BugHub (/fix-bug, /bughub-setup). Rỗng có chủ đích:
    # /fix-bug thấy rỗng thì dừng và trỏ sang /bughub-setup thay vì đoán mã.
    # watch: cửa sổ /fix-bug --watch (scripts/bughub-watch.py).
    #   branch rỗng = chạy watch ở nhánh nào thì sửa + push lên nhánh đó; có giá trị =
    #   nhánh bot riêng, các key còn lại chỉ dùng khi đó. baseBranch rỗng =
    #   defaultBaseBranch; mergeToBase = đẩy fix lên nhánh chính sau khi push nhánh
    #   bot; worktree = chạy trong git worktree riêng (worktreePath rỗng = thư mục
    #   anh em `<repo>-<branch>`) thay vì chuyển nhánh của checkout này.
    "bugHub": {
        "projectCode": "",
        "watch": {
            "branch": "",
            "baseBranch": "",
            "mergeToBase": False,
            "worktree": False,
            "worktreePath": "",
        },
    },

    # --- QA (skill /auto-test-device, com.ezg.qa-probe, com.ezg.autotest) -----
    # Quy ước + tri thức QA riêng của project, để skill dùng chung giữa mọi repo mà
    # không ghi cứng giá trị. Tool `qadevice.py profile init` tự dò rồi ghi vào
    # profile (status template → draft); dev duyệt thì đổi thành reviewed.
    # Merge ĐỆ QUY: dict theo key, list THAY THẾ nguyên list, scalar ghi đè.
    #   conventions  — tên type/thành viên theo template EZG; probe + autotest đọc qua
    #                  `profile sync` (Assets/Resources/EzgQaProbeConfig.json,
    #                  ProjectSettings/EZGAutoTestSettings.json).
    #   liveness     — màn "chết" (asset/feature cũ chưa dọn) để autotest không báo
    #                  lỗi nặng oan; `observed` do tool ghi lúc `stop` (màn đã thấy mở).
    #   verified     — cheat / state path đã gọi thành công (tool ghi, kèm ngày).
    #   noise        — rule bỏ qua finding audit theo flag + regex path/screen.
    #   environments — ma trận môi trường `env matrix` chạy mặc định.
    #   paths        — file project sở hữu mà tool đọc/ghi.
    "qa": {
        "schemaVersion": 1,
        "status": "template",
        "conventions": {
            "screenBaseTypes": ["FeatureBaseController", "UIScreenBase", "ScreenBase", "PopupBase", "UIPanel"],
            "featuresEnum": "GameEnums+Features",
            "uiManagerType": "UIManager",
            "screenPrefabPrefix": "screen_",
            "cheatTypeName": "GameSystems",
            "cheatFlagMember": "isCheat",
            "cheatEnableMethod": "TryEnableCheat",
            "cheatDisableMethod": "DisableCheat",
            "cheatMethodPrefix": "Cheat_",
            "playerDataFacade": "PlayerDataManager",
            "timeProvider": "TimeManager",
            "trackingEventNameType": "TrackingEventName",
            "trackingLogPattern": "Tracking|Firebase|AppsFlyer",
            "csvConfigGlob": "Assets/_Project/Features/**/CsvConfig/*.csv",
        },
        "liveness": {
            "deadFeaturePatterns": ["_Retired$"],
            "forceLive": [],
            "forceDead": [],
            "observed": {},
        },
        "verified": {"cheats": {}, "statePaths": {}},
        "noise": [],
        "environments": {"fontScales": [1.3], "screenSizes": ["720x1280"], "darkMode": True, "animatorScale": 0},
        "paths": {
            "profileDoc": ".claude/docs/QaProfile.md",
            "chartersDir": ".claude/docs/qa/charters",
            "autotestSettings": "ProjectSettings/EZGAutoTestSettings.json",
            "autotestReports": "AutoTestReports",
            "probeOverride": "Assets/Resources/EzgQaProbeConfig.json",
        },
    },
}

# Top-level keys merged per sub-key (recursively) instead of replaced wholesale, so a
# project overriding one leaf keeps every sibling default. Everything else is shallow.
DEEP_MERGE_KEYS = ("backend", "bugHub", "qa")

_PROFILE_FILENAME = "project-profile.json"


def deep_merge(base, override):
    """Recursive merge: dicts merge per key, lists REPLACE (never append), scalars override.

    Returns a new structure — neither argument is mutated, so DEFAULTS stays pristine
    across calls. Lists replace on purpose: a project that trims `screenBaseTypes` to
    one class must not get the template's five back.
    """
    if isinstance(base, dict) and isinstance(override, dict):
        merged = {k: deep_merge(v, None) for k, v in base.items()}
        for k, v in override.items():
            merged[k] = deep_merge(base.get(k), v) if k in base else deep_merge(v, None)
        return merged
    if override is None:
        if isinstance(base, dict):
            return {k: deep_merge(v, None) for k, v in base.items()}
        if isinstance(base, list):
            return list(base)
        return base
    if isinstance(override, dict):
        return {k: deep_merge(v, None) for k, v in override.items()}
    if isinstance(override, list):
        return list(override)
    return override


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

    Merge is shallow per top-level key, except the DEEP_MERGE_KEYS (`backend`,
    `bugHub`, `qa`), which merge recursively per sub-key. That way a project
    overriding only `backend.kind` keeps the default write-pattern instead of
    silently losing the rule.
    """

    def __init__(self, data: dict, source: Path | None = None):
        self._data = data
        self.source = source

    # -- raw access ---------------------------------------------------------
    def get(self, key, default=None):
        if key in DEEP_MERGE_KEYS:
            return deep_merge(DEFAULTS[key], self._data.get(key) or {})
        return self._data.get(key, DEFAULTS.get(key, default))

    # -- identity -----------------------------------------------------------
    @property
    def project_name(self) -> str:
        return self.get("projectName")

    @property
    def display_name(self) -> str:
        explicit = str(self.get("displayName") or "").strip()
        if explicit:
            return explicit
        # "MyGame" -> "My Game", "ABCTemplate" -> "ABC Template".
        # Twin of Get-ProjectDisplayName (run-backlog-loop-core.ps1, notify.ps1).
        name = re.sub(r"[_-]+", " ", str(self.project_name or ""))
        name = re.sub(r"(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ", name)
        return " ".join(name.split())

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

    @property
    def vfx_root(self) -> str:
        return self.get("vfxRoot")

    @property
    def sfx_root(self) -> str:
        return self.get("sfxRoot")

    @property
    def sound_config_asset(self) -> str:
        return self.get("soundConfigAsset")

    @property
    def sfx_event_fields(self) -> dict:
        return dict(self.get("sfxEventFields") or {})

    # -- review surfaces ----------------------------------------------------
    @property
    def sensitive_globs(self) -> list:
        return list(self.get("sensitiveGlobs"))

    @property
    def player_prefs_allowed_files(self) -> list:
        return list(self.get("playerPrefsAllowedFiles") or [])

    # -- backend ------------------------------------------------------------
    @property
    def backend(self) -> dict:
        return self.get("backend")

    @property
    def backend_direct_write_banned(self) -> bool:
        return bool(self.backend.get("directWriteBanned"))

    @property
    def backend_direct_write_pattern(self) -> str:
        return self.backend.get("directWritePattern") or ""

    @property
    def backend_direct_write_advice(self) -> str:
        return self.backend.get("directWriteAdvice") or ""

    # -- BugHub -------------------------------------------------------------
    @property
    def bug_hub(self) -> dict:
        return self.get("bugHub")

    @property
    def bug_hub_watch(self) -> dict:
        return self.bug_hub["watch"]

    @property
    def bug_hub_project_code(self) -> str:
        return self.bug_hub.get("projectCode") or ""

    # -- QA (auto-test-device) ----------------------------------------------
    @property
    def qa(self) -> dict:
        """Block `qa` đã merge đệ quy với DEFAULTS — skill /auto-test-device đọc."""
        return self.get("qa")

    @property
    def qa_conventions(self) -> dict:
        return self.qa["conventions"]

    @property
    def qa_paths(self) -> dict:
        return self.qa["paths"]

    @property
    def qa_status(self) -> str:
        return str(self.qa.get("status") or "template")


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
        # Key merge theo sub-key (backend/bugHub/qa) thì p.get() đã trả bản merge, để thiếu
        # sub-key vẫn ra default. `project_profile.py qa` in nguyên block QA đã merge.
        if sys.argv[1] == "displayName":
            value = p.display_name
        else:
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
            "displayName": p.display_name,
            "solutionFile": p.solution_file,
            "gitConfigPrefix": p.git_config_prefix,
            "defaultBaseBranch": p.default_base_branch,
            "sourceRoot": p.source_root,
            "featuresRoot": p.features_root,
            "gameplayRoot": p.gameplay_root,
            "uiTemplatesRoot": p.ui_templates_root,
            "vfxRoot": p.vfx_root,
            "sfxRoot": p.sfx_root,
            "soundConfigAsset": p.sound_config_asset,
            "sfxEventFields": p.sfx_event_fields,
            "sensitiveGlobs": p.sensitive_globs,
            "playerPrefsAllowedFiles": p.player_prefs_allowed_files,
            "backend": p.backend,
            "bugHub": p.bug_hub,
            "qa": p.qa,
        }, indent=2))
