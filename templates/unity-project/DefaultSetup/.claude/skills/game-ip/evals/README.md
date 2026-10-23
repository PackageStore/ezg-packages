# Evals của skill game-ip

8 câu thử: 6 câu dùng skill (check bản quyền một project, feature giống game khác, đặt tên game, nhạc cho quảng cáo, nhận DMCA,
asset AI) và 2 câu không được bật skill (animation nhân vật, nảy của nút UI).

- Câu 01 cần project: scaffold dựng project Unity giả bằng `scripts/tests/make_fixture.mjs` (cùng project của selftest: font cấm
  nhúng, mp3 có tag nguồn lạ, thư viện GPL, ảnh chụp màn hình, code dịch ngược, file .apk lạ, tên game khác, yêu cầu ghi công, chưa
  có màn Credits). Project giả không có quy chuẩn: nhánh có skill đọc `docs/` của skill.
- Các câu khác không cần project.

Chạy (Claude Code bản dòng lệnh, đã `claude login`):

```bash
claude plugin eval <folder skill game-ip> --scaffold --no-publish --trust-plugin --output-dir <thư mục ngoài folder skill>
```

- `--scaffold`: bắt buộc cho câu 01 (dựng project giả).
- `--output-dir` ngoài folder skill: kết quả không lẫn vào skill (skill được chép nguyên lên Feature Hub).
- Mặc định chạy cả nhánh không skill để so; `--ablation none` chỉ chạy nhánh có skill.
- Grader `tool_used: Skill` chỉ tính ở nhánh có skill: câu 07, 08 đòi skill **không** bật.
