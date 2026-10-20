# CLAUDE_PROJECT.md

**Status:** template — xoá dòng này khi đã điền các mục bên dưới.

Chỉ dẫn **riêng của project này**. `CLAUDE.md` (dùng chung, template quản lý — project không sửa) import
file này ở cuối, nên nó được nạp vào mọi phiên Claude Code, kể cả subagent. Hai file mâu thuẫn → file này thắng.

- File thuộc project: sửa thoải mái, commit cùng repo game. **Không bao giờ** đẩy lên server dùng chung
  (monorepo `ezg-packages`, Feature Hub, `defaultsetup.tgz`) — bộ dùng chung chỉ ship khung
  `.claude/docs/CLAUDE_PROJECT.template.md`.
- Giữ ngắn: mỗi dòng ở đây tốn token ở mọi lượt. Tài liệu dài (GDD, TechSpec, quy trình chi tiết) để ở
  `GDD/`, `TechSpec/`, `Docs/`… rồi trỏ tới bằng link thường — không import (import nạp cả file vào mọi lượt).
- Chỉ ghi điều `CLAUDE.md` chưa nói hoặc nói chưa đúng cho project này; điều đã có ở đó thì không chép lại.

## Game

_(Chưa điền — tên game / codename, thể loại, vòng lặp chính, nền tảng, chỗ để GDD.)_

## Gameplay buckets & scenes

_(Chưa điền — bucket gameplay của project dưới `Assets/_Project/Features/`, feature thêm vào các bucket của
template, scene chính.)_

## Hệ thống & package riêng

_(Chưa điền — CSV collection / PlayerData module thêm ngoài template, package hay plugin riêng, chỗ project
làm khác template; module polish Part II game có (khai ở `project-profile.json` → `polish.modules`) và chỗ game
lệch ngưỡng PolishRules có chủ ý.)_

## Quy ước & gotcha riêng

_(Chưa điền — luật riêng của team cho project này, bẫy đã gặp mà agent cần biết.)_
