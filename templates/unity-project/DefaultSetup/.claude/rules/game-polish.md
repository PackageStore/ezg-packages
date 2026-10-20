# GAME POLISH — mọi thứ người chơi thấy / chạm phải đạt `.claude/docs/PolishRules.md`

Áp cho MỌI task, kể cả task tay không qua skill và task `/run-backlog` — hễ đụng UI, flow màn hình, feedback /
motion, VFX, SFX, animation, số hiển thị, onboarding, shop / quảng cáo / IAP, hiệu năng gameplay.

1. **Đọc đúng mục trước khi quyết** (file dài, đọc phần cần): UI / popup / nút / chữ → §9 · feedback,
   motion, nhận thưởng → §6 + §3 · VFX → §4 · âm, rung → §5 · onboarding → §10 · shop / ads / IAP → §12 ·
   hiệu năng → §13 · camera / chuyển cảnh → §7 · module khai ở `project-profile.json` → `polish.modules` → M1–M4.
2. **Thứ tự ưu tiên:** lời dev trong task → art hoạ sĩ của đúng màn (PSD / Figma) → `ArtStyle.md` → PolishRules.
   Số trong PolishRules là mặc định trên canvas **1080×2400** (`designResolution`); nguyên tắc thì không bỏ.
3. **Dùng thứ template có, biết chỗ nó KHÔNG lo:** Back Android → `UIManager` → `CloseMe` (đặt logic đóng ở
   `CloseMe`); chạm ngoài → `ClickBackgroundToExit` (mặc định **false**, popup phải bật); `UIManager.Show` **không**
   khoá touch lúc mở — nút quan trọng tự khoá ~0.5 s; số lớn → `Utils.MoneyConvert`; rung → `VibrateManager`
   (`ButtonVibrate` rung mọi lần bấm — chỉ cho khoảnh khắc lớn); rewarded → `AdsController`; cấp thưởng qua
   `RewardsService` / `PurchaseManager` trước, animation sau.
4. **Chưa qua checklist = chưa xong.** Trước khi báo xong, tự chấm các ID `P01…P40` áp cho phần vừa làm
   (bảng cuối PolishRules). Dòng `block` fail → sửa rồi mới xong. Dòng `human` (cần máy thật / tai người) → ghi
   thành bước kiểm tay vào tài liệu đi kèm (task / DONE summary / `TechSpec/<Màn>-UIRefactor.md`), **không**
   tự đánh pass. Dòng không liên quan → `na`.
5. Mỗi dòng `block` không sửa được trong phạm vi task → report thêm một dòng `polish: P<id> chưa đạt — <lý do>`
   (ngoại lệ đã khai trong rule `output-format`).
6. **Không mở rộng phạm vi để polish** (§1.8): lỗi polish ngoài phần task chạm → ghi lại (task mới qua
   `/planning-task`), không tự sửa lan.

Gate tự chạy: validator ui-spec, `/new-ui` Phase C/D, `/refactor-ui`, các reviewer, `task-planner` (`[POLISH]`).
