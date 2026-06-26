#!/usr/bin/env python3
"""Deterministic preflight for /run-backlog staged diffs (macOS/Linux port).

Faithful port of backlog-preflight.ps1 — same rules, same confidence model, same
JSON shape. Catches hard project-rule violations before spending LLM reviewer
tokens. The orchestrator auto-fixes only `confidence=definite` findings and routes
`contextual` findings to the reviewers.

Matching parity: PowerShell `-match` is case-insensitive by default, so every
regex here uses re.IGNORECASE to produce the same verdicts as the .ps1 on the
same diff.

Usage:
    python3 .agents/scripts/backlog-preflight.py
    python3 .agents/scripts/backlog-preflight.py -Pretty
"""

import fnmatch
import json
import os
import re
import subprocess
import sys
from collections import deque
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


# Value-bearing / trust-boundary surfaces only. Plain progress-save files
# (*DataPlayer*, *SaveData*, *PlayerPrefs*, *Persistence*) were removed: tampering
# of non-value progress data is low-impact and is covered by the deterministic save
# rules below + qa-verifier, so it should NOT auto-spawn the security-auditor.
# Genuine secrets still flag via the credential content regexes during the diff scan.
SENSITIVE_FILE_PATTERNS = [
    "*Purchase*", "*IAP*", "*Receipt*", "*Payment*",
    "*Auth*", "*Token*", "*Session*",
    "*.env*", "*.config", "*Secrets*", "*Credential*",
]

NS_REQUIREMENTS = [
    (r"\.(Where|Select|ToList|FirstOrDefault|LastOrDefault|Any|All|OrderBy|OrderByDescending|ThenBy|ThenByDescending|GroupBy|Distinct|Skip|Take|Sum|Count|Max|Min|Average|SelectMany|Aggregate)\s*\(", "System.Linq"),
    (r"\b(UniTask|UniTaskVoid|UniTaskCompletionSource)\b", "Cysharp.Threading.Tasks"),
    (r"\.DO(Fade|Move|Scale|Color|Rotate|Jump|Punch|Shake|Value|Blendable|Path)\s*\(|\b(DOTween|DOVirtual|Tweener|TweenParams)\b", "DG.Tweening"),
    (r"\b(EasyEventManager)\b", "TigerForge"),
    (r"\b(TextMeshProUGUI|TMP_Text|TMP_InputField|TextMeshPro)\b", "TMPro"),
    (r"\bAction\s*[<(]|\bFunc\s*<|\[Serializable\]", "System"),
    (r"\bList\s*<|\bDictionary\s*<|\bHashSet\s*<|\bQueue\s*<|\bStack\s*<", "System.Collections.Generic"),
    (r"\b(Button|Slider|Toggle|Dropdown|ScrollRect|RawImage|Scrollbar)\b", "UnityEngine.UI"),
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
        low = f.lower()
        for pattern in SENSITIVE_FILE_PATTERNS:
            if fnmatch.fnmatch(low, pattern.lower()):
                sensitive_reasons.append({"type": "file-pattern", "file": f, "pattern": pattern})
                break

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

            if is_code:
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
                                "Use PlayerDataManager.[Module] instead of PlayerPrefs/direct local persistence.")

                if search(r"\bDataManager\s*\.\s*\w+\s*=", trimmed):
                    add_finding(findings, "data-persistence", "critical", "definite", current_file, line_number, trimmed,
                                "DataManager is read-only config. Do not assign values to DataManager properties at runtime.")

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

                if search(r"[A-Z0-9_]{3,}_(KEY|SECRET|TOKEN|PASSWORD)\b", trimmed):
                    add_finding(findings, "credential", "critical", "definite", current_file, line_number, trimmed,
                                "Do not add hardcoded credential-like identifiers or secrets to client code.")
                    sensitive_reasons.append({"type": "credential-pattern", "file": current_file, "line": line_number})

                if search(r"(sk_[A-Za-z0-9_]+|Bearer\s+[A-Za-z0-9._-]+|eyJ[A-Za-z0-9._-]+)", trimmed):
                    add_finding(findings, "credential", "critical", "definite", current_file, line_number, trimmed,
                                "Potential secret/JWT/Bearer token in staged diff. Remove from client/repo.")
                    sensitive_reasons.append({"type": "credential-pattern", "file": current_file, "line": line_number})

                # Value-bearing currency/resource mutation — deterministic backstop so a
                # currency write inside a save-named file (e.g. PlayerDungeonData.cs) still
                # routes to the security-auditor even though save-file globs were removed.
                # major/contextual: flags sensitivity, never blocks (has_blocking_definite stays false).
                if (search(r"\b(AddCurrency|SetCurrency|AddResource|GrantCurrency|DeductCurrency|(Spend|Grant|Earn|Deduct|Consume)(Gold|Gem|Currency|Resource|Money|Coin|Cash))\b", trimmed)
                        or search(r"\bCurrencyService\.(Spend|Grant|Earn|Add|Set|Deduct)", trimmed)):
                    add_finding(findings, "value-write", "major", "contextual", current_file, line_number, trimmed,
                                "Grants/spends a value-bearing currency or resource — verify amount/source is non-exploitable and (if applicable) server-validated.")
                    sensitive_reasons.append({"type": "value-write", "file": current_file, "line": line_number})

                mu = re.match(r"^using\s+([\w\.]+(?:\.[\w]+)*)\s*;", trimmed, IC)
                if mu:
                    file_added_usings.add(mu.group(1))

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
