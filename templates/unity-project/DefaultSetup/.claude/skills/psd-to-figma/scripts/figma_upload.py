#!/usr/bin/env python3
"""Upload PNGs to Figma via single-use upload URLs, record imageHashes."""

import json
import sys
import os
from concurrent.futures import ThreadPoolExecutor, as_completed
import urllib.request
import urllib.error

from pipeline_config import resolve

def load_manifest(cfg, new_only=False):
    """Build ordered list of (name, filepath) from both indexes."""
    exclude = set()
    if new_only:
        hashes_path = cfg.path("image_hashes.json")
        if hashes_path.exists():
            with open(hashes_path) as f:
                exclude = set(json.load(f).keys())

    items = []
    with open(cfg.path("assets_index.json")) as f:
        assets = json.load(f)
    for name, info in sorted(assets.items()):
        if name not in exclude:
            items.append((name, cfg.path(info["file"])))

    icons_path = cfg.path("icons_index.json")
    if icons_path.exists():
        with open(icons_path) as f:
            icons = json.load(f)
        for name, info in sorted(icons.items()):
            if name not in exclude:
                items.append((name, cfg.path("icons", info["file"])))

    return items

def upload_one(name, filepath, url):
    """POST raw PNG bytes to a single-use URL. Return (name, imageHash) or (name, None, error)."""
    data = filepath.read_bytes()
    if len(data) > 10 * 1024 * 1024:
        return (name, None, f"File too large: {len(data)} bytes")

    boundary = b"----FigmaUploadBoundary"
    filename = filepath.name
    body = (
        b"--" + boundary + b"\r\n"
        b'Content-Disposition: form-data; name="file"; filename="' + filename.encode() + b'"\r\n'
        b"Content-Type: image/png\r\n\r\n"
        + data
        + b"\r\n--" + boundary + b"--\r\n"
    )

    req = urllib.request.Request(
        url,
        data=body,
        headers={
            "Content-Type": f"multipart/form-data; boundary={boundary.decode()}",
        },
        method="POST",
    )
    try:
        with urllib.request.urlopen(req, timeout=120) as resp:
            resp_body = resp.read().decode()
            result = json.loads(resp_body)
            image_hash = result.get("imageHash")
            if image_hash:
                return (name, image_hash, None)
            return (name, None, f"No imageHash in response: {resp_body[:200]}")
    except Exception as e:
        return (name, None, str(e))

def main():
    cfg, argv = resolve()
    urls_file = None
    new_only = False
    for arg in argv:
        if arg == "--new-only":
            new_only = True
        else:
            urls_file = arg

    if not urls_file:
        print("Usage: figma_upload.py <urls.json> [--new-only]")
        print("  urls.json: list of submitUrl strings from upload_assets")
        print("  --new-only: upload only keys not already in image_hashes.json")
        sys.exit(1)

    with open(urls_file) as f:
        urls = json.load(f)

    items = load_manifest(cfg, new_only=new_only)
    print(f"Assets to upload: {len(items)}, URLs available: {len(urls)}")

    if len(urls) < len(items):
        print(f"ERROR: need {len(items)} URLs but only have {len(urls)}")
        sys.exit(1)

    # A-1: seed from existing hashes so we MERGE, never overwrite
    out = cfg.path("image_hashes.json")
    if out.exists():
        with open(out) as f:
            hashes = json.load(f)
        before_count = len(hashes)
        print(f"Seeded {before_count} existing hashes from {out.name}")
    else:
        hashes = {}
        before_count = 0

    failures = []

    with ThreadPoolExecutor(max_workers=10) as pool:
        futures = {}
        for i, (name, filepath) in enumerate(items):
            fut = pool.submit(upload_one, name, filepath, urls[i])
            futures[fut] = name

        for fut in as_completed(futures):
            name, image_hash, err = fut.result()
            if err:
                print(f"  FAIL {name}: {err}")
                failures.append(name)
            else:
                print(f"  OK   {name}: {image_hash}")
                hashes[name] = image_hash

    if failures:
        print(f"\n{len(failures)} failures: {failures}")
        print("Pass a new urls.json with fresh URLs for retries.")

    # A-1: key count must only grow
    if len(hashes) < before_count:
        print(f"FATAL: hash count dropped from {before_count} to {len(hashes)}. Aborting write.")
        sys.exit(1)

    sorted_hashes = dict(sorted(hashes.items()))
    with open(out, "w") as f:
        json.dump(sorted_hashes, f, indent=2)
    print(f"\nWrote {len(sorted_hashes)} hashes to {out} (was {before_count})")

    null_hashes = [k for k, v in sorted_hashes.items() if v is None]
    if null_hashes:
        print(f"WARNING: null hashes: {null_hashes}")

    return len(sorted_hashes), failures

if __name__ == "__main__":
    main()
