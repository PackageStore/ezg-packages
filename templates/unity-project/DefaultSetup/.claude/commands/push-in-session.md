---
description: Commit and push only what this session changed, with a prefix + tag commit message
---

// turbo-all
# Push In Session

`/push-in-session [prefix] [[Tag]]` — ví dụ `/push-in-session`, `# /push-in-session`,
`/push-in-session + [Feat][Mon]`.

Thin entry point — chi tiết thực thi nằm trong skill **`push-in-session`**
(`.claude/skills/push-in-session/SKILL.md`). Nạp skill đó và làm theo.

## Tóm tắt

1. **Danh sách file = việc của chính session này**, lấy từ transcript trong context
   (Write/Edit/Bash/file sinh ra bởi lệnh mình cố ý chạy). KHÔNG `git status`/`git diff` dò tìm.
   File dev sửa tay, hay scene/SO bị editor tool tự sync sau `Assets/Refresh` → để nguyên.
2. **Stage scoped:** `bash .claude/scripts/git_prepare_scoped.sh "<path>"...`
   (Windows: `git_prepare_scoped.ps1`). `NO_CHANGES` → dừng.
3. **Message:** `<prefix> [Tag] <subject>` — **đúng MỘT tag**, chọn tag chiếm tỷ trọng cao nhất.
   - prefix: `+` thêm mới · `*` chỉnh sửa · `#` fix bug
   - tag loại việc: `Feat Enh Bug Ref Bal Cont Perf Clean Pol Sec`
   - tag vùng: `UI Play Meta Mon Save BE Audio Loc Track Ads Bundle Editor CI AI`
     (`AI` = hạ tầng agent trong `.claude/`, KHÔNG phải AI logic trong game — cái đó là `Play`)
   - Cả hai nhóm cùng đúng → `Bal/Perf/Ref/Clean/Pol/Sec/Cont` thắng; còn `Feat/Bug/Enh` dư
     thừa với prefix nên nhường tag vùng (`# [UI] fix shop pack refresh`).
   - subject tiếng Anh, imperative, ≤50 ký tự; body chỉ khi cần note, ≤2 dòng, không trailer.
   - Prefix/tag dev gõ trong prompt thắng tuyệt đối; tag lạ → hỏi lại, không tự chế; dev gõ
     nhiều tag → lấy tag đầu tiên.
4. **Push:** `git_push.sh "[Final Message]"`; repo không có `origin` thì chỉ commit.
5. **Report:** message, file đã commit, file cố tình bỏ lại, trạng thái push.
