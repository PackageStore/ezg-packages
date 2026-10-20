# -*- coding: utf-8 -*-
"""Tests for ezg_base.py (skill update-ai) on a fake project + a fake catalog mirror in temp folders —
no network, the real repo is never touched.

Run: python3 -m unittest discover -s .claude/skills/update-ai/scripts/tests
"""
import contextlib
import hashlib
import io
import json
import os
import shutil
import sys
import tempfile
import unittest
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
import ezg_base as eb  # noqa: E402


def zsha(data):
    return hashlib.sha256(data).hexdigest()


class Mirror:
    """A catalog mirror on disk (index.json, history.json, blobs/, files/) built from {id: {path: bytes}}."""

    def __init__(self, root):
        self.root = root
        self.history = {}

    def publish(self, items, source="defaultsetup", extra_meta=None):
        index = {"schemaVersion": 1, "categories": [], "items": []}
        for iid, files in sorted(items.items()):
            category, name = iid.split("/", 1)
            paths = sorted(files)
            is_dir = len(paths) > 1 or "/" in paths[0][len(eb.install_path_for(paths[0])):]
            install = eb.install_path_for(paths[0])
            buf = io.BytesIO()
            with zipfile.ZipFile(buf, "w") as archive:
                for path in paths:
                    archive.writestr(path, files[path])
            raw = buf.getvalue()
            file_name = name.lstrip(".") + ".zip"
            os.makedirs(os.path.join(self.root, "files", category), exist_ok=True)
            with open(os.path.join(self.root, "files", category, file_name), "wb") as handle:
                handle.write(raw)
            hashes = {p: eb.nsha(files[p]) for p in paths}
            for path, data in files.items():
                normalized = data if b"\0" in data[:8000] else data.replace(b"\r\n", b"\n")
                digest = eb.nsha(data)
                self.history.setdefault(path, [])
                if digest not in self.history[path]:
                    self.history[path].append(digest)
                os.makedirs(os.path.join(self.root, "blobs"), exist_ok=True)
                with open(os.path.join(self.root, "blobs", digest), "wb") as handle:
                    handle.write(normalized)
            item = {"id": iid, "name": name, "category": category, "installPath": install,
                    "isDirectory": is_dir, "files": paths, "fileCount": len(paths), "fileName": file_name,
                    "url": "unused", "sha256": zsha(raw), "size": len(raw), "source": source,
                    "ownership": "merge" if iid in ("Config/settings.json", "Config/.mcp.json") else "base",
                    "fileHashes": hashes}
            item.update((extra_meta or {}).get(iid, {}))
            index["items"].append(item)
        with open(os.path.join(self.root, "index.json"), "w") as handle:
            json.dump(index, handle)
        with open(os.path.join(self.root, "history.json"), "w") as handle:
            json.dump({"schemaVersion": 1, "files": self.history}, handle)


V1 = {
    "Skills/ui-kit": {".claude/skills/ui-kit/SKILL.md": b"# ui-kit v1\n",
                      ".claude/skills/ui-kit/scripts/run.py": b"print('v1')\n",
                      ".claude/skills/ui-kit/old.md": b"old file\n"},
    "Rules/code-style": {".claude/rules/code-style.md": b"rule v1\n"},
    "Agents/qa": {".claude/agents/qa.md": b"agent v1\n"},
    "Config/CLAUDE.md": {"CLAUDE.md": b"# shared v1\n"},
    "Config/settings.json": {".claude/settings.json": b'{"enabledMcpjsonServers": ["unity"], "hooks": {}}\n'},
    "Scripts/tool.py": {".claude/scripts/tool.py": b"print(1)\n"},
}


def v2():
    items = {k: dict(v) for k, v in V1.items()}
    items["Skills/ui-kit"] = {".claude/skills/ui-kit/SKILL.md": b"# ui-kit v2\n",
                              ".claude/skills/ui-kit/scripts/run.py": b"print('v2')\n"}  # old.md dropped
    items["Rules/code-style"] = {".claude/rules/code-style.md": b"rule v2\n"}
    items["Agents/qa"] = {".claude/agents/qa.md": b"agent v2\n"}
    items["Config/settings.json"] = {".claude/settings.json": b'{"enabledMcpjsonServers": ["unity", "codegraph"], "hooks": {"SessionStart": []}}\n'}
    items["Skills/new-skill"] = {".claude/skills/new-skill/SKILL.md": b"# new\n"}
    return items


class Base(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.mkdtemp(prefix="ezg-base-test-")
        self.proj = os.path.join(self.tmp, "proj")
        self.mirror_dir = os.path.join(self.tmp, "mirror")
        os.makedirs(os.path.join(self.proj, ".claude"))
        self.mirror = Mirror(self.mirror_dir)

    def tearDown(self):
        shutil.rmtree(self.tmp, ignore_errors=True)

    # helpers
    def write(self, rel, data):
        path = os.path.join(self.proj, *rel.split("/"))
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "wb") as handle:
            handle.write(data)

    def read(self, rel):
        path = os.path.join(self.proj, *rel.split("/"))
        if not os.path.exists(path):
            return None
        with open(path, "rb") as handle:
            return handle.read()

    def install(self, items):
        for files in items.values():
            for rel, data in files.items():
                self.write(rel, data)

    def run_cli(self, *args, stdin=None):
        out, err = io.StringIO(), io.StringIO()
        old_stdin = sys.stdin
        if stdin is not None:
            sys.stdin = io.StringIO(stdin)
        try:
            with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
                code = eb.main(["--root", self.proj, "--source", self.mirror_dir] + list(args))
        finally:
            sys.stdin = old_stdin
        return code, out.getvalue(), err.getvalue()

    def plan(self):
        return eb.build_plan(eb.Project(self.proj), eb.Remote(self.mirror_dir))

    def states(self, plan=None):
        plan = plan or self.plan()
        return {f["path"]: f["state"] for e in plan["items"] for f in e["files"]}

    def item_state(self, iid, plan=None):
        plan = plan or self.plan()
        return next(e for e in plan["items"] if e["id"] == iid)["state"]


class HashTests(unittest.TestCase):
    def test_crlf_folds_for_text_only(self):
        self.assertEqual(eb.nsha(b"a\r\nb\r\n"), eb.nsha(b"a\nb\n"))
        self.assertNotEqual(eb.nsha(b"\0a\r\n"), eb.nsha(b"\0a\n"))

    def test_classify_matrix(self):
        c = eb.classify_file
        self.assertEqual(c("u", "u", None, set()), "ok")
        self.assertEqual(c("u", None, None, set()), "add")
        self.assertEqual(c("u", "b", "b", set()), "update")
        self.assertEqual(c("u", "old", None, {"old"}), "update")
        self.assertEqual(c("u", "mine", "u", set()), "local-only")
        self.assertEqual(c("u", "mine", "b", set()), "conflict")
        self.assertEqual(c("u", "mine", None, set()), "conflict")
        self.assertEqual(c(None, "b", "b", set()), "remove")
        self.assertEqual(c(None, "mine", "b", set()), "removed-modified")


class MergeTests(unittest.TestCase):
    def merge(self, base, local, up):
        conflicts = []
        out = eb.merge3(base, local, up, "", conflicts)
        return out, conflicts

    def test_both_sides_add(self):
        base = {"a": 1, "list": ["x"]}
        local = {"a": 1, "list": ["x", "mine"], "own": True}
        up = {"a": 1, "list": ["x", "new"], "fresh": 2}
        out, conflicts = self.merge(base, local, up)
        self.assertEqual(out, {"a": 1, "list": ["x", "mine", "new"], "own": True, "fresh": 2})
        self.assertEqual(conflicts, [])

    def test_conflict_keeps_project_value(self):
        out, conflicts = self.merge({"id": "YOUR"}, {"id": "game/x"}, {"id": "YOUR2"})
        self.assertEqual(out, {"id": "game/x"})
        self.assertEqual(conflicts, ["id"])

    def test_upstream_removal_and_unknown_base(self):
        out, _ = self.merge({"gone": 1, "k": 1}, {"gone": 1, "k": 1}, {"k": 1})
        self.assertEqual(out, {"k": 1})
        out, conflicts = self.merge(eb._MISSING, {"k": 1, "own": 1}, {"k": 2, "new": 1})
        self.assertEqual(out, {"k": 1, "own": 1, "new": 1})
        self.assertEqual(conflicts, ["k"])

    def test_hook_lists_merge(self):
        guard = {"matcher": "Edit", "hooks": [{"command": "guard"}]}
        mine = {"matcher": "Bash", "hooks": [{"command": "mine"}]}
        out, _ = self.merge({"PreToolUse": []}, {"PreToolUse": [mine]}, {"PreToolUse": [guard]})
        self.assertEqual(out, {"PreToolUse": [mine, guard]})


class UpdateFlowTests(Base):
    def test_fresh_project_without_lock_updates_everything_clean(self):
        self.mirror.publish(V1)
        self.install(V1)
        self.mirror.publish(v2())
        states = self.states()
        self.assertEqual(states[".claude/rules/code-style.md"], "update")
        self.assertEqual(states[".claude/skills/ui-kit/old.md"], "remove")
        self.assertEqual(states[".claude/settings.json"], "update")
        self.assertEqual(self.item_state("Skills/new-skill"), "new")

        code, _, err = self.run_cli("apply")
        self.assertEqual(code, 0, err)
        self.assertEqual(self.read(".claude/rules/code-style.md"), b"rule v2\n")
        self.assertIsNone(self.read(".claude/skills/ui-kit/old.md"))
        self.assertEqual(self.read(".claude/skills/new-skill/SKILL.md"), b"# new\n")
        self.assertTrue(os.access(os.path.join(self.proj, ".claude/skills/ui-kit/scripts/run.py"), os.X_OK))
        lock = json.loads(self.read(eb.LOCK_REL))
        self.assertEqual(lock["files"][".claude/rules/code-style.md"]["sha256"], eb.nsha(b"rule v2\n"))
        self.assertEqual(lock["items"]["Config/settings.json"]["ownership"], "merge")
        record = json.loads(self.read(eb.RECORD_REL))
        self.assertIn("Skills/new-skill", {i["id"] for i in record["aiItems"]})
        self.assertTrue(all(f["state"] == "ok" for e in self.plan()["items"] for f in e["files"]))

    def test_noop_apply_does_not_touch_lock_or_record(self):
        self.mirror.publish(V1)
        self.install(V1)
        self.run_cli("apply")
        lock_before, record_before = self.read(eb.LOCK_REL), self.read(eb.RECORD_REL)
        code, out, _ = self.run_cli("apply", "--json")
        self.assertEqual(code, 0)
        result = json.loads(out)
        self.assertEqual(result["written"], [])
        self.assertEqual(self.read(eb.LOCK_REL), lock_before)
        self.assertEqual(self.read(eb.RECORD_REL), record_before)

    def test_local_edit_is_kept_and_reported_with_override_path(self):
        self.mirror.publish(V1)
        self.install(V1)
        self.run_cli("apply")
        self.write(".claude/rules/code-style.md", b"rule v1\nproject line\n")
        self.write(".claude/agents/qa.md", b"agent mine\n")
        self.mirror.publish(v2())
        plan = self.plan()
        files = {f["path"]: f for e in plan["items"] for f in e["files"]}
        self.assertEqual(files[".claude/rules/code-style.md"]["state"], "conflict")
        self.assertEqual(files[".claude/rules/code-style.md"]["override"], ".claude/project/overrides/rules/code-style.md")

        self.run_cli("apply")
        self.assertEqual(self.read(".claude/rules/code-style.md"), b"rule v1\nproject line\n")
        self.assertEqual(self.read(".claude/agents/qa.md"), b"agent mine\n")
        self.assertEqual(self.read(".claude/skills/ui-kit/SKILL.md"), b"# ui-kit v2\n")
        # acknowledged: next run sees "project edited, base unchanged", not a fresh conflict
        self.assertEqual(self.states()[".claude/rules/code-style.md"], "local-only")

        code, out, _ = self.run_cli("diff", ".claude/rules/code-style.md")
        self.assertEqual(code, 0)
        self.assertIn("+project line", out)

        code, _, err = self.run_cli("apply", "--take-upstream", ".claude/rules/code-style.md")
        self.assertEqual(code, 0, err)
        self.assertEqual(self.read(".claude/rules/code-style.md"), b"rule v2\n")
        self.assertEqual(self.states()[".claude/rules/code-style.md"], "ok")

    def test_conflict_diff_uses_exact_baseline(self):
        self.mirror.publish(V1)
        self.install(V1)
        self.run_cli("apply")
        self.write(".claude/rules/code-style.md", b"rule v1\nproject line\n")
        self.mirror.publish(v2())
        code, out, _ = self.run_cli("diff", ".claude/rules/code-style.md")
        self.assertEqual(code, 0)
        self.assertIn("Project đã đổi gì", out)
        self.assertIn("+project line", out)
        self.assertIn("+rule v2", out)

    def test_settings_merge_keeps_project_hooks(self):
        self.mirror.publish(V1)
        self.install(V1)
        self.run_cli("apply")
        self.write(".claude/settings.json", b'{"enabledMcpjsonServers": ["unity", "bughub"], "hooks": {"Stop": [1]}}\n')
        self.mirror.publish(v2())
        self.assertEqual(self.states()[".claude/settings.json"], "merge")
        self.run_cli("apply")
        merged = json.loads(self.read(".claude/settings.json"))
        self.assertEqual(merged["enabledMcpjsonServers"], ["unity", "bughub", "codegraph"])
        self.assertEqual(merged["hooks"], {"Stop": [1], "SessionStart": []})
        self.assertEqual(self.states()[".claude/settings.json"], "ok")

    def test_settings_without_lock_merges_two_way(self):
        self.mirror.publish(V1)
        self.install(V1)
        # reformatted by a tool + a project hook, and no lock: must still receive upstream additions
        self.write(".claude/settings.json", b'{\n  "enabledMcpjsonServers": [\n    "unity",\n    "bughub"\n  ],\n  "hooks": {\n    "Stop": [1]\n  }\n}\n')
        self.mirror.publish(v2())
        self.assertEqual(self.states()[".claude/settings.json"], "merge")
        self.run_cli("apply")
        merged = json.loads(self.read(".claude/settings.json"))
        self.assertEqual(merged["enabledMcpjsonServers"], ["unity", "bughub", "codegraph"])
        self.assertEqual(merged["hooks"], {"Stop": [1], "SessionStart": []})

    def test_removed_upstream_but_modified_is_kept(self):
        self.mirror.publish(V1)
        self.install(V1)
        self.run_cli("apply")
        self.write(".claude/skills/ui-kit/old.md", b"old file + project notes\n")
        self.mirror.publish(v2())
        self.assertEqual(self.states()[".claude/skills/ui-kit/old.md"], "removed-modified")
        self.run_cli("apply")
        self.assertIsNotNone(self.read(".claude/skills/ui-kit/old.md"))

    def test_extra_file_in_base_dir_is_reported_not_deleted(self):
        self.mirror.publish(V1)
        self.install(V1)
        self.write(".claude/skills/ui-kit/notes-of-project.md", b"mine\n")
        self.assertEqual(self.states()[".claude/skills/ui-kit/notes-of-project.md"], "extra")
        self.run_cli("apply")
        self.assertEqual(self.read(".claude/skills/ui-kit/notes-of-project.md"), b"mine\n")

    def test_pin_and_optional_items(self):
        self.mirror.publish(V1)
        self.install(V1)
        self.run_cli("apply")
        items = v2()
        items["Skills/maintainer-only"] = {".claude/skills/maintainer-only/SKILL.md": b"x\n"}
        self.mirror.publish(items, extra_meta={"Skills/maintainer-only": {"source": "extra"}})
        code, _, _ = self.run_cli("pin", "Rules/code-style", "--reason", "giữ bản vá")
        self.assertEqual(code, 0)
        self.run_cli("apply")
        self.assertEqual(self.read(".claude/rules/code-style.md"), b"rule v1\n")
        self.assertIsNone(self.read(".claude/skills/maintainer-only/SKILL.md"))
        self.assertEqual(self.item_state("Rules/code-style"), "pinned")
        self.run_cli("apply", "--install", "Skills/maintainer-only")
        self.assertEqual(self.read(".claude/skills/maintainer-only/SKILL.md"), b"x\n")

    def test_item_removed_by_project_is_not_reinstalled(self):
        self.mirror.publish(V1)
        self.install(V1)
        self.run_cli("apply")
        os.remove(os.path.join(self.proj, ".claude/agents/qa.md"))
        self.assertEqual(self.item_state("Agents/qa"), "removed-by-project")
        self.run_cli("apply")
        self.assertIsNone(self.read(".claude/agents/qa.md"))

    def test_no_lock_infers_baseline_from_closest_version(self):
        self.mirror.publish(V1)
        items = v2()
        items["Agents/qa"] = {".claude/agents/qa.md": b"agent v1\n"}  # base did not touch this one
        self.install(V1)
        self.write(".claude/agents/qa.md", b"agent v1\nproject line\n")
        self.write(".claude/rules/code-style.md", b"rule v1\nproject line\n")
        self.mirror.publish(items)
        plan = self.plan()
        files = {f["path"]: f for e in plan["items"] for f in e["files"]}
        self.assertEqual(files[".claude/agents/qa.md"]["state"], "local-only")
        self.assertTrue(files[".claude/agents/qa.md"]["baselineInferred"])
        self.assertEqual(files[".claude/rules/code-style.md"]["state"], "conflict")
        self.assertEqual(files[".claude/rules/code-style.md"]["baseline"], eb.nsha(b"rule v1\n"))
        code, out, _ = self.run_cli("diff", ".claude/rules/code-style.md")
        self.assertIn("+project line", out)
        self.assertIn("+rule v2", out)

    def test_crlf_checkout_is_not_an_edit(self):
        self.mirror.publish(V1)
        self.install(V1)
        self.write(".claude/rules/code-style.md", b"rule v1\r\n")
        self.assertEqual(self.states()[".claude/rules/code-style.md"], "ok")

    def test_lock_from_dir(self):
        template = os.path.join(self.tmp, "DefaultSetup")
        for files in V1.values():
            for rel, data in files.items():
                path = os.path.join(template, *rel.split("/"))
                os.makedirs(os.path.dirname(path), exist_ok=True)
                with open(path, "wb") as handle:
                    handle.write(data)
        self.install(V1)
        self.write(".claude/agents/qa.md", b"mine\n")
        code, _, _ = self.run_cli("lock-init", "--from-dir", template)
        self.assertEqual(code, 0)
        lock = json.loads(self.read(eb.LOCK_REL))
        self.assertIn(".claude/rules/code-style.md", lock["files"])
        self.assertNotIn(".claude/agents/qa.md", lock["files"])
        self.assertEqual(lock["files"][".claude/skills/ui-kit/SKILL.md"]["item"], "Skills/ui-kit")
        self.assertTrue(lock["items"]["Skills/ui-kit"]["isDirectory"])


class GuardTests(Base):
    def setUp(self):
        super().setUp()
        self.mirror.publish(V1)
        self.install(V1)
        self.run_cli("apply")

    def decide(self, rel):
        return eb.guard_decision(self.proj, os.path.join(self.proj, rel))

    def test_denies_base_files_and_new_files_in_base_dirs(self):
        reason = self.decide(".claude/rules/code-style.md")
        self.assertIn(".claude/project/overrides/rules/code-style.md", reason)
        self.assertIn("CLAUDE_PROJECT.md", self.decide("CLAUDE.md"))
        self.assertIsNotNone(self.decide(".claude/skills/ui-kit/new-notes.md"))
        self.assertIn("không override bằng markdown", self.decide(".claude/scripts/tool.py"))
        self.assertIsNotNone(self.decide(eb.LOCK_REL))

    def test_allows_project_owned_and_merge_files(self):
        for rel in (".claude/project/overrides/rules/code-style.md", "CLAUDE_PROJECT.md",
                    ".claude/skills/local-mine/SKILL.md", ".claude/rules/local-x.md",
                    ".claude/settings.json", ".claude/docs/ArtStyle.md", "Assets/_Project/Foo.cs",
                    ".claude/skills/brand-new/SKILL.md"):
            self.assertIsNone(self.decide(rel), rel)

    def test_link_view_path_is_guarded(self):
        os.symlink(os.path.join(self.proj, ".claude", "rules"), os.path.join(self.proj, ".agents-rules"))
        os.makedirs(os.path.join(self.proj, ".agents"))
        os.symlink(os.path.join(self.proj, ".claude", "rules"), os.path.join(self.proj, ".agents", "rules"))
        self.assertIsNotNone(self.decide(".agents/rules/code-style.md"))

    def test_hook_output_and_escape_hatch(self):
        event = json.dumps({"tool_name": "Edit", "cwd": self.proj,
                            "tool_input": {"file_path": os.path.join(self.proj, ".claude/rules/code-style.md")}})
        code, out, _ = self.run_cli("guard", stdin=event)
        self.assertEqual(code, 0)
        self.assertEqual(json.loads(out)["hookSpecificOutput"]["permissionDecision"], "deny")
        os.environ["EZG_BASE_EDIT"] = "1"
        try:
            code, out, _ = self.run_cli("guard", stdin=event)
        finally:
            del os.environ["EZG_BASE_EDIT"]
        self.assertEqual((code, out), (0, ""))

    def test_fail_open(self):
        os.remove(os.path.join(self.proj, eb.LOCK_REL))
        self.assertIsNone(self.decide(".claude/rules/code-style.md"))
        code, out, _ = self.run_cli("guard", stdin="not json")
        self.assertEqual((code, out), (0, ""))

    def test_session_context(self):
        self.assertEqual(eb.session_context_text(self.proj), "")
        self.write(".claude/project/overrides/skills/ui-kit.md", b"# Override\n")
        self.write(".claude/rules/code-style.md", b"edited\n")
        text = eb.session_context_text(self.proj)
        self.assertIn("skills/ui-kit → .claude/project/overrides/skills/ui-kit.md", text)
        self.assertIn("1 file base đang bị sửa tại chỗ", text)


if __name__ == "__main__":
    unittest.main()
