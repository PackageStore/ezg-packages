#!/usr/bin/env python3
"""Patch a .unitypackage in place: replace / add a few entries, keep every other entry byte-identical.

  list  <pkg> [filter]                      — print guid + pathname of entries
  diff  <pkg> <projectRoot> path...         — compare package asset bytes with the project files
  patch <pkg> <out> <projectRoot> path...   — rebuild <out> with the listed project files replacing
                                              (or adding, when a .meta sits next to the file) entries

Entries are written in the original order (asset.meta → asset → pathname) in GNU tar format,
which is what Unity's importer expects.
"""
import hashlib
import io
import os
import sys
import tarfile


def read_entries(pkg):
    """Return ordered list of (name, TarInfo, bytes) for every member."""
    out = []
    with tarfile.open(pkg, "r|gz") as tf:
        for m in tf:
            data = tf.extractfile(m).read() if m.isfile() else b""
            out.append((m.name, m, data))
    return out


def index_by_guid(entries):
    by_guid = {}
    for name, m, data in entries:
        parts = name.strip("./").split("/")
        if len(parts) < 2:
            continue
        guid, kind = parts[0], parts[1]
        by_guid.setdefault(guid, {})[kind] = (name, m, data)
    return by_guid


def pathname_of(group):
    if "pathname" not in group:
        return None
    raw = group["pathname"][2].decode("utf-8", "replace")
    return raw.split("\n")[0].strip()


def cmd_list(pkg, flt=None):
    for guid, g in index_by_guid(read_entries(pkg)).items():
        p = pathname_of(g)
        if p and (not flt or flt.lower() in p.lower()):
            print(f"{guid}  {p}")


def guid_from_meta(meta_path):
    with open(meta_path, "r", encoding="utf-8") as f:
        for line in f:
            if line.startswith("guid:"):
                return line.split(":", 1)[1].strip()
    raise SystemExit(f"no guid in {meta_path}")


def cmd_diff(pkg, root, paths):
    by_path = {pathname_of(g): g for g in index_by_guid(read_entries(pkg)).values() if pathname_of(g)}
    for p in paths:
        local = os.path.join(root, p)
        g = by_path.get(p)
        if g is None:
            print(f"ABSENT   {p}")
            continue
        pkg_bytes = g["asset"][2] if "asset" in g else b""
        with open(local, "rb") as f:
            loc = f.read()
        same = hashlib.sha256(pkg_bytes).hexdigest() == hashlib.sha256(loc).hexdigest()
        meta_guid = guid_from_meta(local + ".meta") if os.path.exists(local + ".meta") else "?"
        print(f"{'SAME ' if same else 'DIFF '}   {p}  pkg-guid={g['asset'][1].name.strip('./').split('/')[0]} local-guid={meta_guid}")


def make_info(name, size, template):
    ti = tarfile.TarInfo(name)
    ti.size = size
    ti.mtime = template.mtime
    ti.mode = template.mode
    ti.uid, ti.gid = template.uid, template.gid
    ti.uname, ti.gname = template.uname, template.gname
    ti.type = tarfile.REGTYPE
    return ti


def cmd_patch(pkg, out, root, paths):
    entries = read_entries(pkg)
    by_guid = index_by_guid(entries)
    by_path = {pathname_of(g): guid for guid, g in by_guid.items() if pathname_of(g)}
    # template TarInfo for brand-new entries: copy ownership/mode from any existing file entry
    template = next(m for _, m, _ in entries if m.isfile())

    replace = {}   # guid -> {"asset": bytes, "asset.meta": bytes}
    add = []       # (guid, path, asset bytes, meta bytes)
    for p in paths:
        local = os.path.join(root, p)
        with open(local, "rb") as f:
            asset = f.read()
        meta_bytes = None
        if os.path.exists(local + ".meta"):
            with open(local + ".meta", "rb") as f:
                meta_bytes = f.read()
        if p in by_path:
            guid = by_path[p]
            replace[guid] = {"asset": asset}
            if meta_bytes is not None:
                replace[guid]["asset.meta"] = meta_bytes
            print(f"replace  {p}  ({guid})")
        else:
            if meta_bytes is None:
                raise SystemExit(f"new entry {p} needs a .meta next to it")
            guid = guid_from_meta(local + ".meta")
            if guid in by_guid:
                raise SystemExit(f"guid {guid} of new entry {p} already exists in package")
            add.append((guid, p, asset, meta_bytes))
            print(f"add      {p}  ({guid})")

    missing = [p for p in paths if p not in by_path and not any(a[1] == p for a in add)]
    if missing:
        raise SystemExit(f"not in package: {missing}")

    written = 0
    with tarfile.open(out, "w:gz", format=tarfile.GNU_FORMAT, compresslevel=6) as tf:
        for name, m, data in entries:
            parts = name.strip("./").split("/")
            guid = parts[0] if len(parts) >= 2 else None
            kind = parts[1] if len(parts) >= 2 else None
            if guid in replace and kind in replace[guid]:
                data = replace[guid][kind]
                ti = make_info(name, len(data), m)
                tf.addfile(ti, io.BytesIO(data))
            elif m.isfile():
                tf.addfile(m, io.BytesIO(data))
            else:
                tf.addfile(m)
            written += 1
        for guid, p, asset, meta_bytes in add:
            # same layout + order as entries Unity itself exports: dir, asset.meta, asset, pathname
            d = tarfile.TarInfo(f"./{guid}")
            d.type = tarfile.DIRTYPE
            d.mode = 0o755
            d.mtime = template.mtime
            tf.addfile(d)
            # pathname không có newline cuối — giống các entry sẵn có trong package
            for kind, blob in (("asset.meta", meta_bytes), ("asset", asset), ("pathname", p.encode("utf-8"))):
                ti = make_info(f"./{guid}/{kind}", len(blob), template)
                tf.addfile(ti, io.BytesIO(blob))
                written += 1
    print(f"wrote {out}: {written} members")


if __name__ == "__main__":
    a = sys.argv[1:]
    if not a:
        raise SystemExit(__doc__)
    if a[0] == "list":
        cmd_list(a[1], a[2] if len(a) > 2 else None)
    elif a[0] == "diff":
        cmd_diff(a[1], a[2], a[3:])
    elif a[0] == "patch":
        cmd_patch(a[1], a[2], a[3], a[4:])
    else:
        raise SystemExit(__doc__)
