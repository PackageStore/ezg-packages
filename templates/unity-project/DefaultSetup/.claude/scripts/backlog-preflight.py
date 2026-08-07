#!/usr/bin/env python3
"""Deterministic preflight for /run-backlog staged diffs (macOS/Linux port).

Faithful port of backlog-preflight.ps1 — same rules, same confidence model, same
JSON shape. Catches hard project-rule violations before spending LLM reviewer
tokens. The orchestrator auto-fixes only `confidence=definite` findings and routes
`contextual` findings to the reviewers.

Why a Python twin: the .ps1 needs PowerShell, which is not present on a stock
macOS dev box. This twin lets STEP 6b/7d run natively on macOS/Linux with
identical verdicts. Keep the two files in lockstep when editing rules.

Matching parity: PowerShell `-match` is case-insensitive by default, so every
regex here uses re.IGNORECASE to produce the same verdicts as the .ps1 on the
same diff. Exception: CREDENTIAL_ID_PATTERN scopes itself back to case-
sensitive with an inline (?-i:...) group — the same group syntax works in .NET
regex, so the two files stay verdict-identical.

Usage:
    python3 .claude/scripts/backlog-preflight.py
    python3 .claude/scripts/backlog-preflight.py -Pretty
"""

import fnmatch
import json
import os
import re
import subprocess
import sys
from collections import deque

# Project-specific review surfaces (which filenames are "sensitive", whether a
# direct client write to the datastore is banned) come from the profile so this
# file stays byte-identical across every project that ships the agent system.
# Running as a script puts this directory on sys.path, so the plain import works.
from project_profile import profile
from datetime import datetime

IC = re.IGNORECASE

# Repo root = parent of parent of this script (.claude/scripts/ -> repo root)
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.dirname(os.path.dirname(SCRIPT_DIR))


def invoke_git(args):
    proc = subprocess.run(
        ["git"] + args, cwd=REPO_ROOT,
        stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True,
    )
    if proc.returncode != 0:
        raise RuntimeError("git {} failed: {}".format(" ".join(args), proc.stdout))
    return proc.stdout.splitlines()


def invoke_git_soft(args):
    """git that tolerates failure (path absent, or no HEAD version yet) — returns None, never raises."""
    proc = subprocess.run(
        ["git"] + args, cwd=REPO_ROOT,
        stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True,
    )
    return proc.stdout.splitlines() if proc.returncode == 0 else None


def is_code_line(line):
    t = line.strip()
    if len(t) == 0:
        return False
    if t.startswith("//"):
        return False
    if t.startswith("*"):
        return False
    if t.startswith("/*"):
        return False
    return True


LOCALIZE_KEY_RE = re.compile(r"^(#[a-z0-9_]+),", IC)


def localize_keys(lines):
    keys = set()
    for line in lines:
        m = LOCALIZE_KEY_RE.match(line)
        if m:
            keys.add(m.group(1).lower())
    return keys


def test_localize_integrity(changed_files, findings):
    """Two deterministic guards on the localize table — both are regressions we already shipped.

    1. localize-key-loss. `CsvImportManager.BuildLocalizationCsvContent` rewrites
       Localization.csv wholesale from the Google Sheet, and the Sheet does not contain keys
       that were only ever added to the local file. Re-importing therefore deletes them with
       no error: it cost 144 already-shipped keys in backlog/done/116, and the same trap is
       recorded in backlog/done/104. Any staged removal of a key present in HEAD is a
       regression, so this is `definite` — it blocks before the LLM reviewers ever run.
    2. localize-escape-collision. The importer escapes ',' as '%%' and LocalizationCollection
       unescapes with a left-to-right Replace("%%", ","), so a literal '%' immediately before
       a comma becomes '%%%' and decodes as ',%' — "40,%" instead of "40%,". Only ADDED lines
       are inspected, so the pre-existing rows carrying this artifact do not spam every diff
       that happens to touch the file.
    """
    for f in changed_files:
        if not f.lower().endswith("localization.csv"):
            continue

        staged = invoke_git_soft(["show", ":{}".format(f)])
        if staged is None:
            continue

        head = invoke_git_soft(["show", "HEAD:{}".format(f)])
        if head is not None:
            lost = sorted(localize_keys(head) - localize_keys(staged))
            if lost:
                shown = ", ".join(lost[:8]) + (", ..." if len(lost) > 8 else "")
                add_finding(
                    findings, "localize-key-loss", "critical", "definite", f, None,
                    "{} key(s) in HEAD are missing from the staged file: {}".format(len(lost), shown),
                    "Do NOT overwrite Localization.csv wholesale — re-importing it from the Google "
                    "Sheet drops keys that exist only locally. Merge instead: keep the freshly "
                    "downloaded rows and re-append the local-only ones from "
                    "`git show HEAD:<file>`. Add every NEW key through /add-localize so it lives "
                    "in the Sheet and survives the next import.")

        added = invoke_git_soft(["diff", "--staged", "-U0", "--", f]) or []
        for line in added:
            if not line.startswith("+") or line.startswith("+++"):
                continue
            content = line[1:]
            if LOCALIZE_KEY_RE.match(content) and "%%%" in content:
                add_finding(
                    findings, "localize-escape-collision", "critical", "definite", f, None,
                    content[:160],
                    "'%%%' decodes as ',%' not '%,'. The importer escapes ',' as '%%' and the "
                    "reader replaces left-to-right, so a literal '%' directly before a comma "
                    "collides. Reword so no '%' is immediately followed by a comma "
                    "(e.g. 'shrinks 40% in area,' instead of 'shrinks 40%,').")


UI_KIT_JSON = ".claude/ui-kit/ui-kit.json"


def test_ui_kit_staleness(changed_files, findings):
    """A staged screen-template edit must carry the regenerated UI kit with it.

    The kit (`ui-kit.json` + `.css` + gallery) is extracted from the template
    prefabs and read by mockup-drafter, the ui-spec validator and /new-ui. It has
    no watcher, and drifting costs nothing at commit time: the kit simply keeps
    describing the old prefabs while the whole UI test suite stands down with
    "kit not generated yet". That is exactly how this repo shipped a kit
    describing 46 templates against a folder of 48 for weeks. So the diff itself
    is the checkpoint.

    Only fires when the kit is TRACKED: a project may legitimately gitignore the
    generated half and rebuild it at bootstrap, and nagging there would be noise
    on every prefab commit forever.
    """
    templates_root = profile().ui_templates_root.replace("\\", "/").rstrip("/").lower()
    if not templates_root:
        return
    touched = [f for f in changed_files
               if f.replace("\\", "/").lower().startswith(templates_root + "/")
               and f.lower().endswith((".prefab", ".prefab.meta"))]
    if not touched:
        return
    if any(f.replace("\\", "/").lower() == UI_KIT_JSON.lower() for f in changed_files):
        return
    if invoke_git_soft(["ls-files", "--error-unmatch", UI_KIT_JSON]) is None:
        return

    shown = ", ".join(os.path.basename(f) for f in touched[:6])
    add_finding(
        findings, "ui-kit-stale", "major", "definite", touched[0], None,
        "{} screen template file(s) staged without the regenerated kit: {}{}".format(
            len(touched), shown, ", ..." if len(touched) > 6 else ""),
        "Run `python3 .claude/scripts/ui-kit-sync.py` and stage .claude/ui-kit/ in the "
        "same commit. The kit is generated FROM these prefabs, so leaving it behind "
        "makes every later mockup describe UI that no longer exists — silently, "
        "because the UI tests skip instead of failing when the kit is out of date. "
        "See .claude/skills/ui-kit/SKILL.md.")


def add_finding(findings, rule, severity, confidence, file, line, evidence, suggestion):
    location = "{}:{}".format(file, line) if line is not None else file
    findings.append({
        "rule": rule,
        "severity": severity,
        "confidence": confidence,
        "file": file,
        "line": line,
        "location": location,
        "evidence": evidence,
        "suggestion": suggestion,
    })


def test_file_usings(file, added_usings, needed_usings, findings):
    # The missing-using rule only applies to C# source. Asset YAML (.prefab/.asset/.unity/.meta)
    # serialize type names like "UnityEngine.UI.Button" that wrongly trip the namespace regex.
    # (The diff scan below also only records needed usings for *.cs files, so this is belt-and-braces.)
    if not file.lower().endswith(".cs"):
        return
    for ns, occ in needed_usings.items():
        if ns in added_usings:
            continue
        file_path = os.path.join(REPO_ROOT, file)
        if os.path.exists(file_path):
            try:
                with open(file_path, "r", encoding="utf-8", errors="ignore") as fh:
                    content = fh.read()
            except OSError:
                content = ""
            if content and re.search(r"using\s+{}\s*;".format(re.escape(ns)), content, IC):
                continue
        add_finding(findings, "missing-using", "critical", "definite", file,
                    occ["line"], occ["evidence"],
                    "Add 'using {};' at the top of the file.".format(ns))


# Which staged files the LANGUAGE rules apply to.
#
# Nearly every rule below is a C# rule — DateTime.Now, StartCoroutine, PlayerPrefs,
# Debug.Log, UIManager.SetActive, LINQ in a hot loop. Run them over the whole diff
# and they match any prose that merely QUOTES the pattern, which is exactly what
# the agent-system docs do for a living: a fresh project's first commit stages
# ~200 markdown/py/ps1 files and drew 798 findings, 719 of them "unitask" against
# text explaining the UniTask rule. Zero were about C#.
#
# Secret detection is deliberately NOT gated: a leaked key in a .json, .env or
# .md is a leak all the same.
SOURCE_FILE_EXTENSIONS = (".cs",)


def is_source_file(path):
    return bool(path) and path.lower().endswith(SOURCE_FILE_EXTENSIONS)


# Sensitive surfaces that auto-spawn the security-auditor, from
# `.claude/project-profile.json` (`sensitiveGlobs`). This project's defaults are
# broad on purpose — a real backend (Supabase read + Cloudflare Worker write), a
# leaderboard and IAP — and they live in project_profile.DEFAULTS so a tree with
# no profile behaves exactly as before. A project with no backend trims the list
# there rather than inheriting false positives.
# Keep backlog-preflight.ps1 reading the same profile key.
SENSITIVE_FILE_PATTERNS = profile().sensitive_globs

# Resolved once: the scan loop runs these per changed line.
_BACKEND_WRITE_BANNED = profile().backend_direct_write_banned
_BACKEND_WRITE_PATTERN = profile().backend_direct_write_pattern
_BACKEND_WRITE_ADVICE = profile().backend_direct_write_advice

# Credential-LIKE identifier (e.g. SUPABASE_SERVICE_KEY). The scoped (?-i:...)
# keeps it UPPER_SNAKE-only even though search() compiles everything with
# IGNORECASE — without it, ordinary lowercase identifiers/JSON keys
# (player_token, session_key, "access_token") false-positive as critical
# findings. Confidence is `contextual` (not `definite`): legit SCREAMING_CONST
# names like LEADERBOARD_CACHE_KEY match the same shape, so this routes to the
# security-auditor for judgment instead of auto-fix/block. The actual-secret
# rule (sk_/Bearer/eyJ values, below in main) stays definite.
CREDENTIAL_ID_PATTERN = r"(?-i:[A-Z0-9_]{3,}_(KEY|SECRET|TOKEN|PASSWORD)\b)"

NS_REQUIREMENTS = [
    (r"\.(Where|Select|ToList|FirstOrDefault|LastOrDefault|Any|All|OrderBy|OrderByDescending|ThenBy|ThenByDescending|GroupBy|Distinct|Skip|Take|Sum|Count|Max|Min|Average|SelectMany|Aggregate)\s*\(", "System.Linq"),
    (r"\b(UniTask|UniTaskVoid|UniTaskCompletionSource)\b", "Cysharp.Threading.Tasks"),
    (r"\.DO(Fade|Move|Scale|Color|Rotate|Jump|Punch|Shake|Value|Blendable|Path)\s*\(|\b(DOTween|DOVirtual|Tweener|TweenParams)\b", "DG.Tweening"),
    (r"\b(EasyEventManager)\b", "TigerForge"),
    (r"\b(TextMeshProUGUI|TMP_Text|TMP_InputField|TextMeshPro)\b", "TMPro"),
    (r"\bAction\s*[<(]|\bFunc\s*<|\[Serializable\]", "System"),
    (r"\bList\s*<|\bDictionary\s*<|\bHashSet\s*<|\bQueue\s*<|\bStack\s*<", "System.Collections.Generic"),
    # Negative lookbehind on "[": Odin's attribute `[Button("...")]` is
    # Sirenix.OdinInspector.Button, NOT UnityEngine.UI.Button. Without it every cheat file the
    # [CHEAT] guardrail asks for (Odin [Button] on the controller) is blocked by a false critical.
    # Lookbehind on "." for the same reason: `EditorGUILayout.Toggle(...)` / `GUILayout.Button(...)`
    # are IMGUI calls, not UI components, so every EditorWindow drawing one was blocked. A real
    # component reference is never preceded by a dot (`Button b`, `GetComponent<Button>()`).
    (r"(?<![\[.])\b(Button|Slider|Toggle|Dropdown|ScrollRect|RawImage|Scrollbar)\b", "UnityEngine.UI"),
]


def search(pattern, text):
    return re.search(pattern, text, IC) is not None


def main():
    argv = [a.lower() for a in sys.argv[1:]]
    pretty = ("-pretty" in argv) or ("--pretty" in argv)
    include_diff_stat = ("-nodiffstat" not in argv) and ("--no-diff-stat" not in argv)

    changed_files = [f for f in invoke_git(["diff", "--staged", "--name-only"]) if f and f.strip()]

    diff = ""
    if changed_files:
        diff = "\n".join(invoke_git(["diff", "--staged", "--unified=20"]))

    diff_stat = ""
    if include_diff_stat and changed_files:
        diff_stat = "\n".join(invoke_git(["diff", "--staged", "--stat"]))

    findings = []
    sensitive_reasons = []

    for f in changed_files:
        for pattern in SENSITIVE_FILE_PATTERNS:
            # PowerShell -like is case-insensitive; fnmatch is case-sensitive, so lower both.
            if fnmatch.fnmatch(f.lower(), pattern.lower()):
                sensitive_reasons.append({"type": "file-pattern", "file": f, "pattern": pattern})
                break

    test_localize_integrity(changed_files, findings)
    test_ui_kit_staleness(changed_files, findings)

    current_file = None
    new_line = None
    hunk_buffer = deque()
    file_added_usings = set()
    file_needed_usings = {}

    def trim_buffer():
        while len(hunk_buffer) > 40:
            hunk_buffer.popleft()

    for raw in diff.split("\n"):
        line = raw.rstrip("\r")

        m = re.match(r"^diff --git a/(.+?) b/(.+)$", line)
        if m:
            if current_file is not None:
                test_file_usings(current_file, file_added_usings, file_needed_usings, findings)
            current_file = m.group(2)
            new_line = None
            hunk_buffer.clear()
            file_added_usings = set()
            file_needed_usings = {}
            continue

        m = re.match(r"^\+\+\+ b/(.+)$", line)
        if m:
            current_file = m.group(1)
            continue

        m = re.match(r"^@@ -\d+(?:,\d+)? \+(\d+)(?:,\d+)? @@", line)
        if m:
            new_line = int(m.group(1))
            hunk_buffer.clear()
            continue

        if current_file is None or new_line is None:
            continue

        if line.startswith("+") and not line.startswith("+++"):
            code = line[1:]
            line_number = new_line
            is_code = is_code_line(code)
            trimmed = code.strip()
            context = "\n".join(hunk_buffer)

            # Language rules only for real C#; secret rules run on everything
            # (see SOURCE_FILE_EXTENSIONS).
            if is_code and is_source_file(current_file):
                if search(r"\bDateTime\.(Now|UtcNow)\b", trimmed):
                    add_finding(findings, "time-manager", "critical", "definite", current_file, line_number, trimmed,
                                "Use TimeManager instead of DateTime.Now/DateTime.UtcNow.")

                if search(r"\bTime\.realtimeSinceStartup\b", trimmed):
                    add_finding(findings, "time-manager", "major", "contextual", current_file, line_number, trimmed,
                                "Verify this is not game-time logic. Use TimeManager for game cooldown/save/time rules.")

                if search(r"\bStartCoroutine\s*\(", trimmed) or search(r"\bStopCoroutine\s*\(", trimmed):
                    add_finding(findings, "unitask", "critical", "definite", current_file, line_number, trimmed,
                                "Use UniTask with cancellation instead of new coroutine calls.")

                if search(r"\bIEnumerator\b", trimmed):
                    add_finding(findings, "unitask", "critical", "contextual", current_file, line_number, trimmed,
                                "New async/game flows should use UniTask. Verify this is not an allowed Unity/third-party signature.")

                if search(r"\basync\s+void\b", trimmed):
                    add_finding(findings, "unitask", "critical", "contextual", current_file, line_number, trimmed,
                                "Avoid async void except narrow Unity event-handler cases; prefer UniTask.")

                if search(r"\bTask\s*(<|\b)", trimmed):
                    add_finding(findings, "unitask", "critical", "contextual", current_file, line_number, trimmed,
                                "Use UniTask instead of Task for game code.")

                if search(r"\.SetActive\s*\(", trimmed) or search(r"\bgameObject\.SetActive\s*\(", trimmed):
                    add_finding(findings, "ui-manager", "critical", "contextual", current_file, line_number, trimmed,
                                "Use UIManager for top-level UI feature show/hide. Child component toggles may be acceptable with task-specific justification.")

                if search(r"\bPlayerPrefs\b", trimmed):
                    add_finding(findings, "data-persistence", "critical", "definite", current_file, line_number, trimmed,
                                "Use DataPlayer through PlayerDataManager instead of PlayerPrefs/direct local persistence.")

                if search(r"\bConsole\.WriteLine\s*\(", trimmed):
                    add_finding(findings, "logging", "critical", "definite", current_file, line_number, trimmed,
                                "Use Unity Debug.Log/LogWarning/LogError instead of Console.WriteLine.")

                if search(r"\bDebug\.Log(Exception|Error)\s*\(", trimmed):
                    add_finding(findings, "console-noise", "major", "contextual", current_file, line_number, trimmed,
                                "Verify this is restricted to exceptional/catch paths and does not create new normal-flow console errors.")

                if search(r"\b(GameObject\.Find|FindObjectOfType|FindObjectsOfType)\s*\(", trimmed):
                    confidence = "contextual" if search(r"\bAwake\s*\(", context) else "definite"
                    add_finding(findings, "mobile-performance", "critical", confidence, current_file, line_number, trimmed,
                                "Cache Find/GetComponent lookups in Awake; do not use Find APIs in hot paths.")

                if search(r"\.(Where|Select|ToList)\s*\(", trimmed):
                    severity = "major" if search(r"\b(Update|FixedUpdate|LateUpdate)\s*\(", context) else "minor"
                    add_finding(findings, "mobile-performance", severity, "contextual", current_file, line_number, trimmed,
                                "Verify LINQ is not in a gameplay hot path.")

                if search(r"\bnew\s+(List|Dictionary|HashSet|Queue|Stack|StringBuilder)\b", trimmed) and search(r"\b(Update|FixedUpdate|LateUpdate)\s*\(", context):
                    add_finding(findings, "mobile-performance", "major", "contextual", current_file, line_number, trimmed,
                                "Avoid allocations in gameplay/update loops.")

                if search(r"\.Save\s*\(", trimmed) and search(r"\b(Update|FixedUpdate|LateUpdate)\s*\(", context):
                    add_finding(findings, "data-persistence", "critical", "contextual", current_file, line_number, trimmed,
                                "Never call Save() from Update/FixedUpdate/LateUpdate or per-frame loops.")

                # Direct client write to the datastore. Both the pattern and
                # whether it is banned at all come from the profile: a project
                # with no backend sets backend.kind to "none" and this rule
                # stops firing instead of flagging code that cannot exist.
                if _BACKEND_WRITE_BANNED and search(_BACKEND_WRITE_PATTERN, trimmed):
                    add_finding(findings, "backend-security", "critical", "definite", current_file, line_number, trimmed,
                                _BACKEND_WRITE_ADVICE)
                    sensitive_reasons.append({"type": "direct-backend-write", "file": current_file, "line": line_number})

            if is_code:
                if search(CREDENTIAL_ID_PATTERN, trimmed):
                    add_finding(findings, "credential", "critical", "contextual", current_file, line_number, trimmed,
                                "Credential-like UPPER_SNAKE identifier. If it carries a real key/secret value, remove it from client code; if it is only a constant name, justify it to the security reviewer.")
                    sensitive_reasons.append({"type": "credential-pattern", "file": current_file, "line": line_number})

                # sk_/eyJ are word-bounded + case-scoped: unanchored `sk_` matched
                # INSIDE ordinary identifiers (task_still, TASK_BASE under
                # IGNORECASE) and flagged them critical-definite. Real Stripe keys
                # are lowercase `sk_...`; a JWT header is literally `eyJ` (base64
                # of '{"'), so exact case loses nothing.
                if search(r"((?-i:\bsk_[A-Za-z0-9_]+)|Bearer\s+[A-Za-z0-9._-]+|(?-i:\beyJ[A-Za-z0-9._-]+))", trimmed):
                    add_finding(findings, "credential", "critical", "definite", current_file, line_number, trimmed,
                                "Potential secret/JWT/Bearer token in staged diff. Remove from client/repo.")
                    sensitive_reasons.append({"type": "credential-pattern", "file": current_file, "line": line_number})

                # Using directive tracking (for missing-using check at file boundary)
                mu = re.match(r"^using\s+([\w\.]+(?:\.[\w]+)*)\s*;", trimmed, IC)
                if mu:
                    file_added_usings.add(mu.group(1))

                # Namespace requirement detection (C# source files only — skip prefabs, assets, etc.)
                if current_file is not None and current_file.lower().endswith(".cs"):
                    for pattern, namespace in NS_REQUIREMENTS:
                        if search(pattern, trimmed):
                            if namespace not in file_needed_usings:
                                file_needed_usings[namespace] = {"line": line_number, "evidence": trimmed}

            hunk_buffer.append(code)
            trim_buffer()
            new_line += 1
            continue

        if line.startswith(" ") or len(line) == 0:
            context_line = line[1:] if len(line) > 0 else ""
            hunk_buffer.append(context_line)
            trim_buffer()
            new_line += 1
            continue

        if line.startswith("-") and not line.startswith("---"):
            continue

    # Final file check for the last file in the diff
    if current_file is not None:
        test_file_usings(current_file, file_added_usings, file_needed_usings, findings)

    critical_count = sum(1 for f in findings if f["severity"] == "critical")
    definite_critical_count = sum(1 for f in findings if f["severity"] == "critical" and f["confidence"] == "definite")
    contextual_count = sum(1 for f in findings if f["confidence"] == "contextual")

    result = {
        "schema_version": 1,
        "generated_at": datetime.now().astimezone().isoformat(),
        "repo": REPO_ROOT,
        "diff": {
            "staged": True,
            "files_changed_count": len(changed_files),
            "changed_files": changed_files,
            "stat": diff_stat,
        },
        "sensitive": {
            "value": len(sensitive_reasons) > 0,
            "reasons": sensitive_reasons,
        },
        "summary": {
            "findings_count": len(findings),
            "critical_count": critical_count,
            "definite_critical_count": definite_critical_count,
            "contextual_count": contextual_count,
            "has_blocking_definite": definite_critical_count > 0,
        },
        "findings": findings,
    }

    if pretty:
        print(json.dumps(result, indent=2, ensure_ascii=False))
    else:
        print(json.dumps(result, separators=(",", ":"), ensure_ascii=False))


if __name__ == "__main__":
    main()
