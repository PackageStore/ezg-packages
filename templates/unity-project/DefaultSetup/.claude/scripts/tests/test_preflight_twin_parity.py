"""Parity guard for the backlog-preflight twins.

CLAUDE.md states the invariant plainly — "Hai bản `.py` và `.ps1` là twin — cùng
rule, cùng confidence model, cùng JSON shape (giữ lockstep khi sửa rule)" — but
nothing enforced it, so nothing noticed when they drifted.

They drifted. Moving the sensitive-file globs out of backlog-preflight.py and
into project_profile.DEFAULTS dropped `*Credential*` on the way: 25 patterns
became 24. The .ps1 kept all 25 and security-auditor.md kept documenting all 25,
so two of the three agreed and the odd one out was the file that actually runs
on macOS and Linux. The visible effect was narrow and quiet — a new
`CredentialManager.cs` stopped being marked sensitive by FILENAME, so the
security-auditor was no longer auto-spawned for it. Content-level detection
still fired, which is exactly why nobody would notice from the output.

These tests read the three sources and compare them. Text-level, deliberately:
the point is to catch a hand-edit to one file that never reached the others, and
running PowerShell is not an option on the machines this suite runs on.
"""

import re
import sys
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
SCRIPTS = REPO / ".claude" / "scripts"
PS1 = SCRIPTS / "backlog-preflight.ps1"
AUDITOR = REPO / ".claude" / "agents" / "security-auditor.md"

sys.path.insert(0, str(SCRIPTS))
from project_profile import DEFAULTS  # noqa: E402


def ps1_fallback_globs():
    """The literal list the .ps1 falls back to when python3 is unavailable."""
    text = PS1.read_text(encoding="utf-8")
    m = re.search(r"\$sensitiveFilePatternsFallback = @\((.*?)\n\)", text, re.S)
    if not m:
        raise AssertionError(
            "could not find $sensitiveFilePatternsFallback in backlog-preflight.ps1 — "
            "if the variable was renamed, update this test too"
        )
    return re.findall(r'"([^"]+)"', m.group(1))


class SensitiveGlobParity(unittest.TestCase):
    def test_ps1_fallback_matches_python_defaults(self):
        """Both twins must fall back to the same set when there is no profile.

        Order is not part of the contract (fnmatch/-like are order-independent),
        so compare as sets and report the difference in both directions.
        """
        py = set(DEFAULTS["sensitiveGlobs"])
        ps1 = set(ps1_fallback_globs())
        self.assertEqual(
            py, ps1,
            "sensitive-glob fallbacks drifted.\n"
            "  only in project_profile.DEFAULTS: {}\n"
            "  only in backlog-preflight.ps1   : {}".format(
                sorted(py - ps1) or "-", sorted(ps1 - py) or "-"),
        )

    def test_security_auditor_documents_the_same_globs(self):
        """The auditor's brief lists the surfaces that trigger it.

        A glob that spawns the auditor but is absent from its own brief means it
        arrives without knowing why it was called. Only checks that documented
        globs exist in the default set — the doc groups them into prose lines and
        is not required to be exhaustive in the other direction.
        """
        documented = set(re.findall(r"`(\*[^`]+\*)`", AUDITOR.read_text(encoding="utf-8")))
        documented = {g for g in documented if not g.startswith("*.")}
        missing = documented - set(DEFAULTS["sensitiveGlobs"])
        self.assertFalse(
            missing,
            "security-auditor.md advertises globs that no longer trigger it: {}".format(
                sorted(missing)),
        )


class BackendRuleParity(unittest.TestCase):
    def test_both_twins_read_the_backend_rule_from_the_profile(self):
        """Neither twin may hardcode the datastore-write rule.

        The .py was converted to the profile first and the .ps1 was not, which
        left Windows applying this project's backend rule to every project that
        ships the toolchain — including ones with no backend at all.
        """
        ps1 = PS1.read_text(encoding="utf-8")
        self.assertIn('Get-ProfileJson "backend"', ps1,
                      "backlog-preflight.ps1 does not read `backend` from the profile")
        self.assertIn("$backendWriteBanned -and", ps1,
                      "backlog-preflight.ps1 applies the backend rule unconditionally")

        py = (SCRIPTS / "backlog-preflight.py").read_text(encoding="utf-8")
        self.assertIn("_BACKEND_WRITE_BANNED and", py,
                      "backlog-preflight.py applies the backend rule unconditionally")

    def test_ps1_fallback_pattern_matches_python_default(self):
        """The no-python3 fallback must still describe the same banned call."""
        ps1 = PS1.read_text(encoding="utf-8")
        # `[^']*` (not `+`): the base template ships no backend, so the default
        # pattern is the empty string — an empty fallback is the correct match,
        # not a missing one.
        m = re.search(r"\$backendWritePattern = .*?else \{ '([^']*)' \}", ps1, re.S)
        self.assertIsNotNone(m, "could not read the .ps1 backend-pattern fallback")
        # .NET and Python spell this regex identically; compare after collapsing
        # the escaping difference introduced by PowerShell single quotes.
        self.assertEqual(
            m.group(1).replace("\\\\", "\\"),
            DEFAULTS["backend"]["directWritePattern"],
        )


class ProfileDefaultsAreComplete(unittest.TestCase):
    def test_defaults_cover_every_key_the_scripts_ask_for(self):
        """A key read without a DEFAULTS entry returns None and fails silently.

        Scans the shell/PowerShell callers for `project_profile.py <key>` and the
        Python callers for `profile().<attr>`, then checks each resolves.
        """
        from project_profile import profile

        keys = set()
        for f in SCRIPTS.glob("*"):
            if f.suffix not in {".sh", ".ps1", ".py", ".command", ".bat"}:
                continue
            text = f.read_text(encoding="utf-8", errors="ignore")
            # Require `python3` on the same line: without it this also matches
            # prose that merely names the module ("the defaults in
            # project_profile.py apply."), which reads as a request for a key
            # called "apply". A `$Key`/`%1` style dynamic argument does not match
            # the [a-zA-Z]+ group and is skipped on purpose — it cannot be
            # resolved statically.
            keys |= set(re.findall(
                r'python3[^\n]*?project_profile\.py"?\)?\s+"?([a-zA-Z]+)"?', text))

        keys.discard("")
        unknown = sorted(k for k in keys if profile().get(k) is None)
        self.assertFalse(
            unknown,
            "scripts request profile keys with no default: {}".format(unknown),
        )


class SourceFileScoping(unittest.TestCase):
    """Language rules must be gated to source files in BOTH twins.

    Without the gate the C# rules match any text that quotes the pattern, and the
    agent system's own docs quote them constantly. A generated project's first
    commit stages the whole toolchain and drew 798 findings — 719 `unitask`
    against markdown that explains the UniTask rule, none about C#.
    """

    def test_python_gates_language_rules(self):
        py = (SCRIPTS / "backlog-preflight.py").read_text(encoding="utf-8")
        self.assertIn("if is_code and is_source_file(current_file):", py,
                      "backlog-preflight.py runs language rules on every file type")
        # the secret rules must NOT be behind that gate
        gate = py.index("if is_code and is_source_file(current_file):")
        cred = py.index("search(CREDENTIAL_ID_PATTERN, trimmed)")
        reopen = py.index("            if is_code:", gate)
        self.assertLess(reopen, cred,
                        "credential rule fell inside the source-file gate — a secret "
                        "in .env/.json/.md would stop being reported")

    def test_powershell_gates_language_rules(self):
        ps1 = PS1.read_text(encoding="utf-8")
        self.assertIn("if ($isCode -and (Test-SourceFile $currentFile)) {", ps1,
                      "backlog-preflight.ps1 runs language rules on every file type")
        gate = ps1.index("if ($isCode -and (Test-SourceFile $currentFile)) {")
        cred = ps1.index("Credential-LIKE identifier")
        reopen = ps1.index("        if ($isCode) {", gate)
        self.assertLess(reopen, cred,
                        "credential rule fell inside the source-file gate in the .ps1")

    def test_both_twins_agree_on_the_extension_set(self):
        py = (SCRIPTS / "backlog-preflight.py").read_text(encoding="utf-8")
        py_exts = set(re.findall(r'SOURCE_FILE_EXTENSIONS = \(([^)]*)\)', py)[0].replace('"', '').split(","))
        ps1_exts = set(re.findall(r'\$SourceFileExtensions = @\(([^)]*)\)', PS1.read_text(encoding="utf-8"))[0].replace('"', '').split(","))
        clean = lambda xs: {x.strip() for x in xs if x.strip()}
        self.assertEqual(clean(py_exts), clean(ps1_exts))


if __name__ == "__main__":
    unittest.main()
