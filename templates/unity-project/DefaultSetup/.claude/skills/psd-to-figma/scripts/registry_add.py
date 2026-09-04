"""Atomic writer for the component / 9-slice / style registries.

Parallel builders call this instead of dropping a sidecar file for a later
merge agent to fold. Each call locks the target, deep-merges one entry,
validates, and replaces the file atomically, so concurrent adds never clobber
each other.

    registry_add.py --data-dir <data> component --entry entry.json [--replace]
    registry_add.py --data-dir <data> nine-slice-applied --stem <stem> --entry applied.json [--replace]
    registry_add.py --data-dir <data> style --entry styles.json --sidecar <name> [--replace]

Exit codes: 0 ok (including a no-op), 2 usage/config, 3 validation, 4 conflict.
"""
import argparse
import contextlib
import fcntl
import json
import os
import re
import sys
import tempfile

from pipeline_config import resolve

ID_RE = re.compile(r"^\d+:\d+$")
ID_KEYS = {"id", "setId", "componentId", "nodeId", "instanceOf"}
CONTAINER_KEYS = {"variants", "children", "instances"}
NOTE_KEYS = {"notes", "note"}
NOTE_SEP = "\n---\n"
NOTE_CAP = 2000

EXIT_VALIDATION = 3
EXIT_CONFLICT = 4

_MISSING = object()


def _fail(code, message):
    sys.stderr.write("registry_add: " + message + "\n")
    sys.exit(code)


def _scalar(value):
    return json.dumps(value, ensure_ascii=False, sort_keys=True)


class Conflict(Exception):
    def __init__(self, path, existing, incoming):
        self.path = path
        self.existing = existing
        self.incoming = incoming


def id_leaves(node, key=None, container=False):
    if isinstance(node, str):
        if container or key in ID_KEYS:
            yield node
        return
    if isinstance(node, list):
        for item in node:
            yield from id_leaves(item, key, False)
        return
    if isinstance(node, dict):
        if container:
            for ck, cv in node.items():
                if isinstance(cv, str):
                    yield cv
                else:
                    yield from id_leaves(cv, ck, False)
        else:
            for k, v in node.items():
                if k in ID_KEYS and isinstance(v, str):
                    yield v
                else:
                    yield from id_leaves(v, k, k in CONTAINER_KEYS)


def validate_component(name, body):
    for idv in id_leaves({name: body}, container=True):
        if not ID_RE.match(idv):
            _fail(EXIT_VALIDATION, f"entry {name!r}: id {idv!r} does not match \\d+:\\d+")
    if isinstance(body, dict) and body.get("variants") and "setId" not in body:
        _fail(EXIT_VALIDATION, f"entry {name!r}: variants present but no setId")


def validate_nine_slice(stem, applied):
    if not isinstance(applied, dict):
        _fail(EXIT_VALIDATION, f"stem {stem!r}: applied payload must be an object")
    for idv in id_leaves(applied):
        if not ID_RE.match(idv):
            _fail(EXIT_VALIDATION, f"stem {stem!r}: id {idv!r} does not match \\d+:\\d+")


def append_note(dst, key, incoming):
    incoming = str(incoming)
    existing = dst.get(key)
    chunk = incoming[:NOTE_CAP]
    if existing is None:
        dst[key] = chunk
        return len(chunk)
    segments = [s.strip() for s in str(existing).split(NOTE_SEP)]
    if incoming.strip() in segments:
        return None
    dst[key] = str(existing) + NOTE_SEP + chunk
    return len(chunk)


def merge(dst, src, replace):
    changes = []
    for k, v in src.items():
        if k in NOTE_KEYS:
            added = append_note(dst, k, v)
            if added is not None:
                changes.append(f"{k}(+{added} chars)")
            continue
        cur = dst.get(k, _MISSING)
        if cur is _MISSING:
            dst[k] = v
            changes.append("+" + k)
        elif isinstance(cur, dict) and isinstance(v, dict):
            sub = merge(cur, v, replace)
            if sub:
                changes.append(k + "(" + ",".join(sub) + ")")
        elif cur == v:
            continue
        elif replace:
            dst[k] = v
            changes.append("~" + k)
        else:
            raise Conflict(k, cur, v)
    return changes


def deep_copy(value):
    return json.loads(json.dumps(value))


@contextlib.contextmanager
def locked(path):
    fd = open(str(path) + ".lock", "w")
    try:
        fcntl.flock(fd, fcntl.LOCK_EX)
        yield
    finally:
        fcntl.flock(fd, fcntl.LOCK_UN)
        fd.close()


def load_json(path, default):
    if not path.exists():
        return default
    return json.loads(path.read_text(encoding="utf-8"))


def atomic_write(path, data):
    trailing_newline = path.exists() and path.read_bytes().endswith(b"\n")
    fd, tmp = tempfile.mkstemp(dir=str(path.parent), prefix=path.name + ".", suffix=".tmp")
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as fh:
            json.dump(data, fh, indent=2, ensure_ascii=False)
            if trailing_newline:
                fh.write("\n")
        os.replace(tmp, path)
    except BaseException:
        with contextlib.suppress(OSError):
            os.unlink(tmp)
        raise


def apply_component(store, name, incoming, replace):
    if name not in store:
        if isinstance(incoming, dict):
            for k in NOTE_KEYS:
                if k in incoming:
                    incoming[k] = str(incoming[k])[:NOTE_CAP]
        store[name] = incoming
        return "+" + name
    existing = store[name]
    if isinstance(existing, dict) and isinstance(incoming, dict):
        merged = deep_copy(existing)
        sub = merge(merged, incoming, replace)
        if not sub:
            return None
        store[name] = merged
        return "~" + name + "." + ",".join(sub)
    if existing == incoming:
        return None
    if not replace:
        raise Conflict(name, existing, incoming)
    store[name] = incoming
    return "~" + name


def apply_nine_slice(store, stem, applied, replace):
    entry = store.get(stem)
    if entry is None:
        store[stem] = {"applied": applied}
        return "+" + stem + ".applied"
    if "applied" not in entry:
        entry["applied"] = applied
        return "~" + stem + "(+applied)"
    merged = deep_copy(entry["applied"])
    sub = merge(merged, applied, replace)
    if not sub:
        return None
    entry["applied"] = merged
    return "~" + stem + ".applied(" + ",".join(sub) + ")"


def report(changes):
    real = [c for c in changes if c]
    if not real:
        print("registry: (no changes)")
        return
    for c in real:
        print("registry: " + c)


def die_conflict(scope, conflict):
    _fail(
        EXIT_CONFLICT,
        f"conflict at {scope}.{conflict.path}: "
        f"{_scalar(conflict.existing)} != {_scalar(conflict.incoming)} "
        "(pass --replace to overwrite)",
    )


def cmd_component(cfg, args):
    entry = load_json_entry(args.entry)
    if not isinstance(entry, dict):
        _fail(EXIT_VALIDATION, "component entry must be an object of name -> body")
    target = cfg.path("component_ids.json")
    with locked(target):
        store = load_json(target, {})
        working = deep_copy(store)
        changes = []
        for name, body in entry.items():
            try:
                token = apply_component(working, name, body, args.replace)
            except Conflict as c:
                die_conflict(name, c)
            validate_component(name, working[name])
            changes.append(token)
        if any(changes):
            atomic_write(target, working)
    report(changes)


def cmd_nine_slice(cfg, args):
    applied = load_json_entry(args.entry)
    validate_nine_slice(args.stem, applied)
    target = cfg.path("nine_slice.json")
    with locked(target):
        store = load_json(target, {})
        working = deep_copy(store)
        try:
            token = apply_nine_slice(working, args.stem, applied, args.replace)
        except Conflict as c:
            die_conflict(args.stem + ".applied", c)
        if token:
            atomic_write(target, working)
    report([token])


def cmd_style(cfg, args):
    entry = load_json_entry(args.entry)
    if not isinstance(entry, dict):
        _fail(EXIT_VALIDATION, "style entry must be an object")
    styles = entry.get("textStyles")
    if styles is not None and not isinstance(styles, dict):
        _fail(EXIT_VALIDATION, "style entry textStyles must be an object")
    target = cfg.path(args.sidecar)
    with locked(target):
        store = load_json(target, {})
        working = deep_copy(store)
        try:
            sub = merge(working, entry, args.replace)
        except Conflict as c:
            die_conflict(args.sidecar, c)
        if sub:
            atomic_write(target, working)
    listed = ensure_style_file_listed(cfg, args.sidecar)
    tokens = [f"{args.sidecar}: {s}" for s in sub]
    if listed:
        tokens.append(f"styleIdFiles(+{args.sidecar})")
    report(tokens)


def ensure_style_file_listed(cfg, sidecar):
    config_path = cfg.data_dir / "psd2figma.json"
    with locked(config_path):
        data = load_json(config_path, {})
        listed = data.setdefault("styleIdFiles", [])
        if sidecar in listed:
            return False
        listed.append(sidecar)
        atomic_write(config_path, data)
        return True


def load_json_entry(path):
    try:
        return json.loads(open(path, encoding="utf-8").read())
    except FileNotFoundError:
        _fail(2, f"entry file not found: {path}")
    except json.JSONDecodeError as e:
        _fail(EXIT_VALIDATION, f"entry file {path} is not valid JSON: {e}")


def build_parser():
    p = argparse.ArgumentParser(prog="registry_add.py", add_help=True)
    sub = p.add_subparsers(dest="command", required=True)

    c = sub.add_parser("component")
    c.add_argument("--entry", required=True)
    c.add_argument("--replace", action="store_true")

    n = sub.add_parser("nine-slice-applied")
    n.add_argument("--stem", required=True)
    n.add_argument("--entry", required=True)
    n.add_argument("--replace", action="store_true")

    s = sub.add_parser("style")
    s.add_argument("--entry", required=True)
    s.add_argument("--sidecar", required=True)
    s.add_argument("--replace", action="store_true")
    return p


def main():
    if "--self-test" in sys.argv[1:]:
        return self_test()
    cfg, remaining = resolve()
    args = build_parser().parse_args(remaining)
    if args.command == "component":
        cmd_component(cfg, args)
    elif args.command == "nine-slice-applied":
        cmd_nine_slice(cfg, args)
    elif args.command == "style":
        cmd_style(cfg, args)
    return 0


def self_test():
    import subprocess

    root = tempfile.mkdtemp(prefix="registry_selftest.")
    data = os.path.join(root, "data")
    os.makedirs(data)
    with open(os.path.join(data, "psd2figma.json"), "w") as fh:
        json.dump({"paths": {"projectRoot": root, "psdDir": "."}, "styleIdFiles": []}, fh)
    script = os.path.abspath(__file__)

    def run(*a, expect=0):
        r = subprocess.run(
            [sys.executable, script, "--data-dir", data, "--project-root", root, *a],
            capture_output=True, text=True,
        )
        assert r.returncode == expect, f"{a} -> {r.returncode} {r.stderr}"
        return r

    def write(name, obj):
        p = os.path.join(root, name)
        with open(p, "w") as fh:
            json.dump(obj, fh)
        return p

    e = write("a.json", {"Set": {"setId": "1:1", "variants": {"A": "1:2"}}})
    run("component", "--entry", e)
    e = write("b.json", {"Set": {"setId": "1:1", "variants": {"B": "1:3"}}})
    run("component", "--entry", e)
    store = json.load(open(os.path.join(data, "component_ids.json")))
    assert store["Set"]["variants"] == {"A": "1:2", "B": "1:3"}, store

    e = write("c.json", {"Set": {"setId": "9:9"}})
    run("component", "--entry", e, expect=EXIT_CONFLICT)
    store2 = json.load(open(os.path.join(data, "component_ids.json")))
    assert store2 == store, "conflict must leave the file unchanged"
    run("component", "--entry", e, "--replace")
    assert json.load(open(os.path.join(data, "component_ids.json")))["Set"]["setId"] == "9:9"

    e = write("bad.json", {"Bad": {"id": "not-an-id"}})
    run("component", "--entry", e, expect=EXIT_VALIDATION)

    e = write("note1.json", {"N": {"notes": "first"}})
    run("component", "--entry", e)
    e = write("note2.json", {"N": {"notes": "first"}})
    run("component", "--entry", e)
    e = write("note3.json", {"N": {"notes": "second"}})
    run("component", "--entry", e)
    assert json.load(open(os.path.join(data, "component_ids.json")))["N"]["notes"] == "first" + NOTE_SEP + "second"

    e = write("ns.json", {"node": "Plate 2:2", "border": [1, 2, 3, 4]})
    run("nine-slice-applied", "--stem", "plate", "--entry", e)
    e = write("ns2.json", {"node": "Plate 9:9", "border": [1, 2, 3, 4]})
    run("nine-slice-applied", "--stem", "plate", "--entry", e, expect=EXIT_CONFLICT)
    assert json.load(open(os.path.join(data, "nine_slice.json")))["plate"]["applied"]["node"] == "Plate 2:2"

    e = write("st.json", {"textStyles": {"T": "S:abc,"}})
    run("style", "--entry", e, "--sidecar", "style_ids.x.json")
    assert "style_ids.x.json" in json.load(open(os.path.join(data, "psd2figma.json")))["styleIdFiles"]
    assert json.load(open(os.path.join(data, "style_ids.x.json")))["textStyles"] == {"T": "S:abc,"}

    import shutil
    shutil.rmtree(root)
    print("registry_add self-test: OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
