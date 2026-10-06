# Evals của skill game-vfx

8 câu thử: 6 câu làm, duyệt, dùng thư viện (trúng đòn của đòn thường, hiệu ứng của thư viện với pool, hạt trên UI, duyệt một
hiệu ứng nặng, pack VFX mua sẵn, nổ ở game không dùng frame-by-frame) và 2 câu không được bật skill (nảy của nút UI, clip
animation nhân vật). Scaffold chép các file đang track của repo module (bỏ `.claude/` và mọi folder `Skill~`, bỏ file đã xoá
mà chưa commit), nên câu thử đọc quy chuẩn của module như trong một project thật và không đọc được đáp án.

Chạy (Claude Code bản dòng lệnh, đã `claude login`):

```bash
claude plugin eval <folder skill game-vfx> --scaffold --no-publish --output-dir <thư mục ngoài folder skill>
```

- `--scaffold`: bắt buộc, câu thử cần bản chép repo.
- `--output-dir` ngoài folder skill: kết quả không lẫn vào skill (skill được chép nguyên lên Feature Hub).
- Mặc định chạy cả nhánh không skill để so; `--ablation none` chỉ chạy nhánh có skill (rẻ bằng nửa).
- Grader `tool_used: Skill` chỉ tính ở nhánh có skill: câu 06, 07 đòi skill **không** bật.
