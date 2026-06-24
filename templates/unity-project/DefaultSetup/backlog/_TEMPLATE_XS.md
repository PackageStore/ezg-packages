# XS Task Template

Use for: copy/label/CSV tweak, dead-code removal, single-variable rename, constant adjust. No new logic, no risk of breaking anything beyond the line touched.

Filename: `backlog/todo/NNN-short-slug.md`

---

### [PRIORITY] Short output-focused title (≤10 words)

**Description:** 1 sentence stating the exact change, which file, which line/row (if known).

**File:** `Assets/_Project/path/to/File.cs` — one-line reason.

**Acceptance criteria:**
- [ ] Change matches the description | Verify: open file, visual check
- [ ] Compiles in Unity (no CS#### errors in Console) | Verify: open Unity Editor, check Console
- [ ] No new red errors or yellow warnings in Unity Console | Verify: Play the relevant scene
