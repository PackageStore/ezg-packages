---
name: update-ai
description: Cập nhật toàn bộ hệ thống AI dùng chung (skill, command, agent, rule, docs, script, CLAUDE.md, settings/.mcp.json) của project từ EZG Feature Hub về bản mới nhất bằng MỘT lệnh, không mất phần project đã tuỳ biến — so 3 chiều (bản gốc lúc cài / bản đang có / bản mới), cập nhật an toàn mọi file project chưa sửa, gộp JSON cho settings.json/.mcp.json, chuyển phần project lỡ sửa thẳng vào file base sang override (.claude/project/overrides/, CLAUDE_PROJECT.md) rồi mới nhận bản mới, cài item mới của bộ mặc định, commit local. Dùng khi user gõ "/update-ai", nói "update AI", "cập nhật AI feature", "cập nhật skill từ Feature Hub", "update skills/agents/rules", "kéo bản mới của base", "base có gì mới", hoặc khi SessionStart báo file base đang bị sửa tại chỗ. KHÔNG dùng để publish skill lên server (→ /publish-ai-features), không cập nhật package UPM com.ezg.* (chỉ báo package cần), không đụng Unity.
---

# /update-ai — cập nhật base AI của EZG, giữ phần riêng của project

Mọi file AI mà base EZG ship là **của base**: project không sửa tại chỗ (rule `base-ownership`).
Phần riêng của project nằm ở chỗ base không bao giờ ghi: `CLAUDE_PROJECT.md`,
`.claude/project/overrides/<kind>/<tên>.md`, `.claude/project/ezg-pins.json`, item `local-*`,
`ArtStyle.md`, `QaProfile.md`… Skill này đưa base lên bản mới nhất mà không mất phần đó.

Công cụ: `ezg_base.py` cạnh file này. Gọi là `$EZG` dưới đây:

```bash
EZG="python3 .claude/skills/update-ai/scripts/ezg_base.py"     # Windows: py -3 / python thay python3
```

Tự làm hết, **không hỏi xác nhận** — trừ khi một bước dưới đây nói phải hỏi. Không bao giờ bỏ nội
dung project đã viết: mọi thứ hoặc vào override, hoặc được giữ nguyên + pin.

---

## STEP 1 — Xem trạng thái (chỉ đọc)

```bash
$EZG status            # bảng cho người đọc
$EZG status --json     # dữ liệu cho bạn xử lý tiếp
```

- `LỖI: Gateway EZG từ chối` → báo dev đăng nhập lại (MCP `ezg` → `login_instructions`, hoặc
  `bash ~/.ezg/refresh-session.sh`), dừng.
- Project **chưa có `CLAUDE_PROJECT.md`** → chạy `bash .claude/scripts/bootstrap.sh`
  (Windows: `powershell -ExecutionPolicy Bypass -File .claude/scripts/bootstrap.ps1`) trước — nó tạo
  file đó từ khung; STEP 3 sẽ chuyển ghi chú riêng trong `CLAUDE.md` cũ sang.
- Mọi thứ `đã mới nhất` và không có file sửa local → báo một dòng, dừng.

Trạng thái từng file:

| state | Nghĩa | Xử lý |
|---|---|---|
| `update` / `add` / `remove` | base đổi, project chưa đụng | STEP 2 tự làm |
| `merge` | settings.json / .mcp.json: gộp JSON, giữ key project thêm | STEP 2 tự làm |
| `new` (+) | item mới của bộ mặc định | STEP 2 tự cài |
| `local-only` | project sửa thẳng file base, base không đổi | STEP 3 |
| `conflict` | project sửa thẳng file base **và** base cũng đổi | STEP 3 |
| `removed-modified` | base bỏ file, project lại có sửa | STEP 3 |
| `extra` | file lạ project thêm vào thư mục của base | STEP 3 |
| `pinned` / `removed-by-project` | project giữ lại / đã gỡ | chỉ báo cáo |

## STEP 2 — Áp mọi thay đổi an toàn

```bash
$EZG apply --json
```

Ghi file project chưa sửa, gộp JSON, cài item mới của bộ mặc định, cập nhật
`.claude/ezg-base.lock.json` + `ProjectSettings/EzgFeatureHub/install-record.json`. File sửa local
được **giữ nguyên** ở bước này. Ghi lại `written`, `deleted`, `kept`, `mergeConflicts`, `postSteps`.

Nếu `written` có `.claude/skills/update-ai/` → chính skill này vừa đổi: **đọc lại file SKILL.md này**
rồi làm tiếp STEP 3 theo bản mới.

## STEP 3 — Đưa phần project sửa tại chỗ sang đúng chỗ

Với từng file `local-only` / `conflict` / `removed-modified` trong status:

1. `$EZG diff <path>` — đọc **"Project đã đổi gì (baseline → local)"**: đó là ý đồ của project cần
   giữ. Phần "Base đã đổi gì" là thứ sắp nhận về. Không có baseline → diff 2 chiều: tự suy phần nào
   là của project (thứ base mới không có và không giống một thay đổi của base).
2. Chọn chỗ cho phần đó theo cột `override` của status:
   - `.claude/project/overrides/<kind>/<tên>.md` (skill, command, agent, rule, doc, backlog template)
     → viết **chỉ phần khác biệt**, dạng chỉ dẫn đè lên base, không chép lại cả file:
     ```markdown
     # Override: <kind>/<tên>
     Áp cho project này, thắng bản base khi mâu thuẫn.
     - <thay vì <điều base nói>, project này <điều project muốn>> — lý do: <...>
     ```
     File override đã có → gộp thêm, không ghi đè ý cũ. Nếu bản base mới đã tự làm đúng điều project
     muốn → không cần override.
   - `CLAUDE_PROJECT.md` (cho `CLAUDE.md`) → chuyển ghi chú riêng của game vào đúng mục.
   - `.claude/project/ui-kit-usage.json` (cho `ui-kit-usage.json`) → chuyển các entry project thêm
     (cùng shape `{ "<TemplateName>": "<note>" }` dưới `templates`), rồi chạy lại `ui-kit-sync`.
   - **Không có override** (script, config, file code) → giữ bản local và pin item:
     `$EZG pin <item-id> --reason "<bản vá gì, vì sao>"`; ghi vào report để người bảo trì base
     đưa bản vá lên monorepo. Không tự viết lại script.
   - Thay đổi vô nghĩa (khoảng trắng, typo đã được base sửa, bản cũ hơn base) → không cần override.
3. Khi phần của project đã nằm ở chỗ mới (hoặc chắc chắn không cần):
   `$EZG apply --take-upstream <path> [<path> ...]` — nhận bản base cho đúng các file đó.

File `extra` (project thêm vào thư mục skill/doc của base): nội dung là chỉ dẫn → chuyển vào override
của item đó; là skill/công cụ riêng → chuyển sang thư mục `local-<tên>` tương ứng
(vd `.claude/skills/local-<tên>/`). Xong thì xoá file lạ khỏi thư mục base.

Chỉ **hỏi dev** khi không đoán được ý đồ của một thay đổi (vd xoá một đoạn lớn của base mà không rõ
lý do) — đưa đoạn diff, hỏi giữ (override) hay bỏ.

## STEP 4 — Bước sau cập nhật (theo `postSteps` của các lần apply)

| postStep | Chạy |
|---|---|
| `bootstrap` | `bash .claude/scripts/bootstrap.sh` (idempotent; làm mới link view `.agents/`) |
| `ui-kit-sync` | `python3 .claude/scripts/ui-kit-sync.py` |
| `unittest` | `python3 -m unittest discover -s .claude/scripts/tests` — fail thì báo, không tự sửa script base |
| `restart-session` | báo dev: mở lại phiên Claude Code để settings/hook/MCP mới có hiệu lực |

Item có `cần package: com.ezg.x >= v` trong status → chỉ báo dev cài qua Feature Hub (tab UPM),
không tự sửa `Packages/manifest.json`.

## STEP 5 — Commit local (không push)

Stage **đúng** các file vừa đổi — `written` + `deleted` của mọi lần apply, file override/
`CLAUDE_PROJECT.md`/`ezg-pins.json` vừa viết, `.claude/ezg-base.lock.json`,
`ProjectSettings/EzgFeatureHub/install-record.json`, file bootstrap/ui-kit-sync vừa sinh:

```bash
git add -- <từng file>
git commit -m "chore(ai): cập nhật EZG base (<N> file)"
```

Không `git add -A`, không push (dev push khi muốn, vd `/push-in-session`).
Repo chưa có git → bỏ qua bước này.

## STEP 6 — Report

Ngắn, theo thứ tự: số file cập nhật / thêm / xoá; item mới đã cài; override đã viết (file + một dòng
ý chính); item đã pin + lý do; xung đột JSON đã giữ giá trị của project; postStep đã chạy / dev cần
làm (mở lại phiên, cài package); commit hash.

---

## Ghi chú

- Chạy lại bất kỳ lúc nào: lệnh idempotent, lần sau chỉ xử lý phần còn lại.
- Project chưa có `.claude/ezg-base.lock.json` (sinh trước khi có cơ chế này): lần đầu so với
  `history.json` của server — file nào khớp một bản base từng publish là "chưa sửa"; còn lại là
  sửa local và đi qua STEP 3. Sau lần đó lock được tạo và commit.
- `$EZG modified` — liệt kê file base đang bị sửa tại chỗ, offline.
- `$EZG unpin <item-id>` — bỏ pin khi base đã có bản vá.
- Người bảo trì base cần sửa file base ngay trong project game: mở Claude với `EZG_BASE_EDIT=1`
  (hook guard cho qua), rồi `/publish-ai-features`. Đường chuẩn vẫn là sửa trong monorepo `ezg-packages`.
