import json
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent.parent
doc = (HERE / "SKILL.md").read_text()
rules = json.loads((HERE / "rules.json").read_text())["rules"]

errors = []

rows = {}
for line in doc.splitlines():
    cells = [c.strip() for c in line.strip().strip("|").split("|")]
    if line.startswith("|") and len(cells) >= 4 and re.fullmatch(r"[SV]-\d+", cells[0]):
        rows[cells[0]] = cells[-1].lower()

for r in rules:
    want = "yes" if r["blocks"] else "no"
    got = rows.get(r["id"])
    if got is None:
        errors.append(f"{r['id']}: no tier table row")
    elif got != want:
        errors.append(f"{r['id']}: Blocks is {got!r}, rules.json says {want!r}")

removed = [
    "figma_extract_rest.py", "verify_figma_vs_psd.py", "--hygiene-strict",
    "figma_extract_save.py", "screens.json", "nine_slice.json",
    "accepted_debt.json", "hygiene_allow", "FIGMA_TOKEN", "verify_report.json",
]
for s in removed:
    if s in doc:
        errors.append(f"removed string present: {s}")

if "audit.mjs" not in doc:
    errors.append("audit.mjs missing")
if "icons_search" not in doc or "Advisory only" not in doc:
    errors.append("Icons advisory line missing")
if not re.search(r"^description:.*canvas|^description:.*tidy canvas", doc, re.M):
    errors.append("frontmatter description does not name the canvas rule")

for e in errors:
    print("FAIL:", e)
sys.exit(1 if errors else 0)
