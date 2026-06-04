# EZG Sample

Package mẫu để smoke-test toàn bộ pipeline registry (monorepo → GitHub Actions → R2 → Worker → Unity). Không có logic thật. **Xoá package này sau khi đã xác minh registry hoạt động.**

Kiểm chứng nhanh trong Unity sau khi cài:

```csharp
Ezg.Sample.SampleBootstrap.Ping();
```
