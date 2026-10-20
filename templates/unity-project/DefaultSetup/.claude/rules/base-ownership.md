# BASE OWNERSHIP — file của base EZG không sửa tại chỗ; phần riêng của project để đúng chỗ

Mọi file AI mà base EZG ship (skill, command, agent, rule, docs, script, backlog template, ui-kit,
`CLAUDE.md`) là **của base**: `/update-ai` / Feature Hub thay bằng bản mới, nên sửa tại chỗ sẽ mất
(hoặc kẹt cập nhật). Danh sách chính xác: `.claude/ezg-base.lock.json` (key `files`).

**Phần riêng của project — base không bao giờ ship, update không bao giờ đụng:**

| Muốn | Ghi vào |
|---|---|
| Chỉ dẫn chung cho cả project (game, bucket, quy ước, gotcha) | `CLAUDE_PROJECT.md` |
| Chỉnh cách một skill / command / agent / rule / doc / backlog template của base làm việc | `.claude/project/overrides/<skills\|commands\|agents\|rules\|docs\|backlog-templates>/<tên>.md` |
| Skill / command / agent / rule mới của riêng project | `.claude/<skills\|commands\|agents\|rules>/local-<tên>` |
| Luật ghép UI template riêng | `.claude/project/ui-kit-usage.json` |
| Art style / QA / profile | `.claude/docs/ArtStyle.md`, `QaProfile.md`, `.claude/project-profile.json` |
| Thêm hook / MCP server / permission | `.claude/settings.json`, `.mcp.json` (được sửa — update gộp JSON, giữ phần project) |
| Giữ một item base không cập nhật | `/update-ai` → `pin` (có lý do) |

**Override — đọc trước, thắng khi mâu thuẫn.** Trước khi làm theo một skill/command/agent/rule/doc
của base, nếu có `.claude/project/overrides/<kind>/<tên>.md` thì đọc nó (SessionStart liệt kê các
override đang có). Điều override nói thắng bản base; phần override không nhắc tới thì theo base.
Override chỉ ghi **phần khác biệt** + lý do, không chép lại cả file base; không frontmatter, mở đầu
`# Override: <kind>/<tên>`, mỗi ý một gạch đầu dòng (`thay vì <base>, project này <...> — lý do`).

**Khi cần đổi một file base** (vd được bảo "sửa skill X", "sửa CLAUDE.md"): viết vào chỗ tương ứng ở
bảng trên thay vì sửa file đó, rồi làm tiếp task. Hook `PreToolUse` chặn Edit/Write vào file base và
chỉ đúng đường override — làm theo, đừng tìm đường vòng (Bash `sed`, `cat >`, copy đè).
Script / file code của base không override được bằng markdown: báo lỗi cho người bảo trì base
(sửa ở monorepo `ezg-packages` rồi `/publish-ai-features`), hoặc `/update-ai` → pin nếu project buộc
phải giữ bản vá.

**Người bảo trì base** sửa thẳng trong project game: mở Claude với `EZG_BASE_EDIT=1`. Đường chuẩn vẫn
là sửa trong monorepo rồi publish.

**Lý do:** base dùng chung cho mọi game; file nào bị sửa tại chỗ thì project đó hoặc mất phần sửa khi
cập nhật, hoặc không dám cập nhật nữa. Tách phần riêng ra file của project thì cập nhật luôn an toàn,
chỉ bằng một lệnh `/update-ai`.
