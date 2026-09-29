# Evals của skill ui-motion

11 câu thử chạy trên project Unity GIẢ `FixtureGame` do `scripts/tests/make_fixture.mjs` dựng (class PopupBase, ClickScale,
ListStagger, TapArea, NewBadge, UIManager, AudioHub…). Không câu nào dùng dữ liệu của project thật; câu 11 và grader
`khong_dung_project_khac` bắt lỗi trả lời bằng trí nhớ về project khác.

Chạy (Claude Code bản dòng lệnh đã đăng nhập):

```bash
claude plugin eval <folder skill ui-motion> --allow-tools Bash
```

Scaffold cần Node và biết folder module: skill nằm trong `<module>/Skill~/ui-motion` thì tự thấy; bản cài ở
`~/.claude/skills` đọc `install.json`; hoặc đặt biến `UIMOTION_MODULE`. Câu 03 dựng project không có module.
