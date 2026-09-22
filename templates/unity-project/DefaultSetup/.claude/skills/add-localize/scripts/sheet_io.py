#!/usr/bin/env python3
"""Sheets access and Unity LanguageData I/O — the parts `add-localize` needs.

Standard library only: the JWT for the service account is signed with
`cryptography` when it is installed and with `openssl` on PATH otherwise, so a
bare Python install is enough and nothing has to be pip-installed into a Unity
project.

Provenance: lifted verbatim from `fill-localize/scripts/localize_sheet.py`, the
skill where this code was written and proved against this sheet and these
assets. It is duplicated rather than imported because `add-localize` ships in
the default setup of every new project while `fill-localize` does not — a skill
that silently needs a second skill installed is a skill that is broken on
arrival. Keep the two copies in step when either is fixed.

What each piece is for
----------------------
  sheet_id / api / service_account_token  talk to the Sheets REST API
  read_project_csv / pipeline_text        the `key~value` CSVs the importer reads
  read_asset / write_asset                LanguageData .asset files, edited in
                                          place so an unchanged entry keeps its
                                          original bytes and the diff stays small
"""
import json
import os
import re
import urllib.error
import urllib.parse
import urllib.request


SHEET_URL_RE = re.compile(r"/spreadsheets/d/([A-Za-z0-9_-]+)")


LANG_COL_RE = re.compile(r"^(.*)-([a-z]{2}[a-z]*)$")


SCOPE = "https://www.googleapis.com/auth/spreadsheets"


DQ_ESCAPE = re.compile(r"\\(x[0-9a-fA-F]{2}|u[0-9a-fA-F]{4}|U[0-9a-fA-F]{8}|.)")


DQ_SIMPLE = {"n": "\n", "t": "\t", "r": "\r", "0": "\0", "\\": "\\",
             '"': '"', "/": "/", " ": " ", "a": "\a", "b": "\b", "f": "\f",
             "v": "\v", "e": "\x1b", "N": "\x85", "_": "\xa0"}


def sheet_id(url_or_id):
    m = SHEET_URL_RE.search(url_or_id)
    return m.group(1) if m else url_or_id


def _b64url(b):
    import base64
    return base64.urlsafe_b64encode(b).rstrip(b"=")


def service_account_token(path, scope=SCOPE):
    """Mint an access token from a service-account JSON key (RS256 JWT grant).

    Uses `cryptography` if present, else shells out to openssl, so the script
    stays dependency-free on a bare Python install.
    """
    import time
    d = json.load(open(path, encoding="utf-8"))
    now = int(time.time())
    header = _b64url(json.dumps({"alg": "RS256", "typ": "JWT"}).encode())
    claim = _b64url(json.dumps({
        "iss": d["client_email"], "scope": scope,
        "aud": d["token_uri"], "iat": now, "exp": now + 3600,
    }).encode())
    signing_input = header + b"." + claim

    try:
        from cryptography.hazmat.primitives import hashes, serialization
        from cryptography.hazmat.primitives.asymmetric import padding
        key = serialization.load_pem_private_key(
            d["private_key"].encode(), password=None)
        sig = key.sign(signing_input, padding.PKCS1v15(), hashes.SHA256())
    except ImportError:
        import subprocess
        import tempfile
        with tempfile.TemporaryDirectory() as td:
            pem = os.path.join(td, "k.pem")
            open(pem, "w", newline="\n").write(d["private_key"])
            sig = subprocess.run(
                ["openssl", "dgst", "-sha256", "-sign", pem],
                input=signing_input, stdout=subprocess.PIPE, check=True).stdout

    body = urllib.parse.urlencode({
        "grant_type": "urn:ietf:params:oauth:grant-type:jwt-bearer",
        "assertion": (signing_input + b"." + _b64url(sig)).decode(),
    }).encode()
    try:
        with urllib.request.urlopen(d["token_uri"], body) as r:
            return json.load(r)["access_token"]
    except urllib.error.HTTPError as e:
        raise SystemExit("service-account token request failed: %s\n%s"
                         % (e, e.read().decode("utf-8", "replace")[:400]))


def api(token, method, path, payload=None):
    url = "https://sheets.googleapis.com/v4/spreadsheets/%s" % path
    data = json.dumps(payload).encode() if payload is not None else None
    req = urllib.request.Request(url, data=data, method=method, headers={
        "Authorization": "Bearer " + token, "Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req) as r:
            return json.load(r)
    except urllib.error.HTTPError as e:
        raise SystemExit("Sheets API %s %s failed: %s\n%s"
                         % (method, path, e, e.read().decode("utf-8", "replace")[:600]))


def read_project_csv(path):
    """LocalizationData/{lang}/{cat}.csv  -  `key~value`, header `key~value`."""
    out = {}
    if not os.path.exists(path):
        return out
    with open(path, "r", encoding="utf-8-sig", errors="replace") as f:
        for i, line in enumerate(f):
            line = line.rstrip("\r\n")
            if not line or (i == 0 and line.startswith("key~")):
                continue
            k, _, v = line.partition("~")
            if k:
                out[k] = v
    return out


def pipeline_text(v):
    """Escape a sheet value the way the CSV->asset importer stores it.

    The shipped CSVs and .assets keep newlines as a literal two-character `\\n`
    (LocalizeDownloader writes `.Replace("\\n", "\\\\n")`). Writing a real
    newline here would silently change how every multi-line string renders.
    """
    return v.replace("\r\n", "\n").replace("\n", "\\n")


def split_asset(raw):
    """-> (head, key_lines, mid, value_lines, tail) or None.

    Line based on purpose: a regex spanning `keys:` .. `values:` backtracks
    catastrophically on the larger tables (common.asset is ~300 entries).
    Handles the empty form Unity writes as `keys: []` / `values: []`.
    """
    lines = raw.split("\n")
    ki = vi = None
    for i, ln in enumerate(lines):
        if ki is None and ln.rstrip() in ("    keys:", "    keys: []"):
            ki = i
        elif ki is not None and vi is None and \
                ln.rstrip() in ("    values:", "    values: []"):
            vi = i
    if ki is None or vi is None:
        return None
    ti = vi + 1
    while ti < len(lines) and (lines[ti].startswith("    - ")
                               or lines[ti].startswith("      ")):
        ti += 1
    return (lines[:ki + 1], lines[ki + 1:vi], lines[vi:vi + 1],
            lines[vi + 1:ti], lines[ti:])


def group_items(lines):
    """Group `    - x` entries with their continuation lines.

    Unity folds long scalars onto `      ` continuation lines and writes
    multi-line values as block scalars; both must stay attached to their item.
    """
    items = []
    for ln in lines:
        if ln.startswith("    - "):
            items.append([ln])
        elif items and (ln.startswith("      ") or ln.strip() == ""):
            items[-1].append(ln)
    return items


def decode_item(item_lines):
    body = item_lines[0][6:]
    rest = item_lines[1:]
    if body in ("|-", "|", ">-", ">"):
        return "\n".join(ln[6:] for ln in rest).rstrip("\n")
    if rest:
        # folded plain/quoted scalar: YAML joins continuation lines with a space
        body = body + " " + " ".join(ln.strip() for ln in rest if ln.strip())
    return _unyaml(body)


def _unyaml(v):
    """Decode a YAML scalar as Unity writes it (\\xNN / \\uNNNN escapes)."""
    v = v.strip()
    if len(v) > 1 and v[0] == "'" and v[-1] == "'":
        return v[1:-1].replace("''", "'")
    if len(v) > 1 and v[0] == '"' and v[-1] == '"':
        def sub(m):
            g = m.group(1)
            if g[0] in "xuU" and len(g) > 1:
                return chr(int(g[1:], 16))
            return DQ_SIMPLE.get(g, g)
        return DQ_ESCAPE.sub(sub, v[1:-1])
    return v


def _yaml_scalar(v):
    """Serialize one entry the way Unity's YAML writer does.

    Unity emits ASCII-only files: anything outside ASCII becomes a
    double-quoted scalar with \\xNN / \\uNNNN escapes.
    """
    v = v.replace("\r\n", "\n")
    if "\n" in v:
        return ["    - |-"] + ["      %s" % ln for ln in v.split("\n")]
    if any(ord(c) > 126 for c in v) or "\\" in v or '"' in v:
        out = []
        for c in v:
            if c == '"':
                out.append('\\"')
            elif c == "\\":
                out.append("\\\\")
            elif 32 <= ord(c) <= 126:
                out.append(c)
            elif ord(c) <= 0xFF:
                out.append("\\x%02X" % ord(c))
            else:
                out.append("\\u%04X" % ord(c))
        return ['    - "%s"' % "".join(out)]
    needs_quote = (
        v == "" or v[0] in " #&*!|>%@`'[]{}," or v[-1] == " "
        or ": " in v or v.endswith(":") or " #" in v
        or re.match(r"^[-+]?[\d.]+$", v)
    )
    if needs_quote:
        return ["    - '%s'" % v.replace("'", "''")]
    return ["    - %s" % v]


def read_asset(path):
    if not os.path.exists(path):
        return None, {}
    raw = open(path, "r", encoding="utf-8", errors="replace").read()
    parts = split_asset(raw)
    if not parts:
        return raw, {}
    _head, key_lines, _mid, val_lines, _tail = parts
    keys = [decode_item(it) for it in group_items(key_lines)]
    values = [decode_item(it) for it in group_items(val_lines)]
    n = min(len(keys), len(values))
    return raw, dict(zip(keys[:n], values[:n]))


def write_asset(path, raw, mapping):
    """Apply `mapping` to a LanguageData asset, editing only what changed.

    Entries whose value is unchanged keep their original bytes - Unity folds
    long scalars over continuation lines and escapes non-ASCII in its own way,
    and re-emitting every entry would churn the whole file (and the diff) for
    no reason.
    """
    parts = split_asset(raw)
    if not parts:
        raise SystemExit("unrecognised LanguageData layout: %s" % path)
    head, key_lines, mid, val_lines, tail = parts
    key_items = group_items(key_lines)
    val_items = group_items(val_lines)
    n = min(len(key_items), len(val_items))

    out_keys, out_vals, seen = [], [], set()
    for i in range(n):
        k = decode_item(key_items[i])
        seen.add(k)
        out_keys.extend(key_items[i])
        if k in mapping and mapping[k] != decode_item(val_items[i]):
            out_vals.extend(_yaml_scalar(mapping[k]))
        else:
            out_vals.extend(val_items[i])

    for k, v in mapping.items():
        if k in seen:
            continue
        out_keys.extend(_yaml_scalar(k))
        out_vals.extend(_yaml_scalar(v))

    # `keys: []` becomes `keys:` once there is anything to list
    head = head[:-1] + [head[-1].replace("keys: []", "keys:")] if out_keys else head
    mid = [mid[0].replace("values: []", "values:")] if out_vals else mid

    open(path, "w", encoding="utf-8", newline="\n").write(
        "\n".join(head + out_keys + mid + out_vals + tail))


def asset_template(name, script_guid="89e19c0109d646644add6c0016ea5829"):
    return (
        "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!114 &11400000\n"
        "MonoBehaviour:\n  m_ObjectHideFlags: 0\n"
        "  m_CorrespondingSourceObject: {fileID: 0}\n"
        "  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n"
        "  m_GameObject: {fileID: 0}\n  m_Enabled: 1\n  m_EditorHideFlags: 0\n"
        "  m_Script: {fileID: 11500000, guid: %s, type: 3}\n"
        "  m_Name: %s\n  m_EditorClassIdentifier: \n  data:\n"
        "    keys:\n    values:\n" % (script_guid, name)
    )