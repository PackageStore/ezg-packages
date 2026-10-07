# ART STYLE — mọi quyết định hình ảnh bám `.claude/docs/ArtStyle.md`

Nguồn chuẩn duy nhất về visual của game (palette token, kit UI, bố cục, chữ, icon/hero art, motion, màn
mẫu, hướng đã bị loại) là **`.claude/docs/ArtStyle.md`**. Áp cho mọi task — kể cả task ad-hoc không đi
qua skill — hễ có chọn màu, chọn/vẽ sprite, bố cục màn, sinh icon/art AI, recolor, viết spec/mockup UI,
hay chấm visual — và khi sinh SFX (§6c).

1. **Đọc trước khi quyết.** Mở ArtStyle.md + `Read` các board ở `.claude/docs/ArtStyle/*.png` mà nó khai.
   Không suy style từ tên game, tên file hay "màn gần nhất".
2. **Chỉ dùng token §2.** Cần màu/sprite/kiểu mới → thêm vào ArtStyle.md (kèm lý do) rồi mới dùng.
3. **Ưu tiên:** lời dev trong task → art hoạ sĩ giao cho đúng màn (PSD/Figma) → ArtStyle.md → màn mẫu §8
   → suy luận (ghi lại vào ArtStyle.md, `Status: draft`).
4. **Thiếu file / `Status: template`** → copy `.claude/docs/ArtStyle.template.md` thành `ArtStyle.md`
   (nếu thiếu) rồi làm § Bootstrap của nó trước; báo dev trong report là file cần duyệt.
5. **Dev phản hồi về visual = cập nhật ArtStyle.md ngay trong lượt đó:** duyệt màn → §8 (ảnh
   `ArtStyle/screens/pending/` → `approved/`), chê hướng nào → §9 (nguyên văn lý do + ngày, ảnh →
   `rejected/`) + luật concept §1 / dòng §11 nếu lỗi đó checklist chưa bắt được, đổi kit/font → §3/§5 +
   chạy lại `python3 .claude/scripts/art-style-board.py`.
   Không chỉ ghi vào memory riêng — memory không đi theo repo, ArtStyle.md thì có.
6. Nội dung ArtStyle.md cũng theo rule `no-external-game-refs`: tả style bằng lời trung tính, không tên game khác.
7. **Sửa skill / agent / doc / template dùng chung** (mọi thứ trong `.claude/` trừ `ArtStyle.md` +
   `ArtStyle/`): **không** ghi giá trị style vào đó — màu/hex, font, cỡ chữ, nhịp motion, concept/theme,
   asset kit cụ thể, bố cục khoá của một họ màn. Skill/agent chỉ ghi **step** đảm bảo style ("đọc §N",
   "chấm từng dòng §11", "block khi…"). Cần chỗ chứa mới → thêm mục/slot **trống** vào
   `ArtStyle.template.md`, điền giá trị ở `ArtStyle.md` của project. Lý do: các file đó được đẩy sang mọi
   project; giá trị style viết cứng sẽ lan sang game khác.

**Lý do:** không có nguồn chuẩn thì mỗi lần rework agent tự chế palette/concept, và màn lệch lại thành
mẫu cho màn sau — có màn đã phải làm lại 3–4 lần vì lệch style.
