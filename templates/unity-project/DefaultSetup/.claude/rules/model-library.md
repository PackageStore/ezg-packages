# MODEL LIBRARY — cần model 3D thì tìm trong thư viện công ty (MCP `ezg-3d`) trước khi tự tạo

Thư viện chung của EZG (~4.000 model đã duyệt từ các game: nhân vật, xe, nhà, cây, đồ vật, clip animation…) nằm sau
MCP `ezg-3d`. Áp cho mọi task cần model / mesh / clip 3D — kể cả task ad-hoc không qua skill.

1. **Tìm trước, tạo sau.** `search_models` (từ khoá tiếng Anh + `category` / `ext` / `rigged` / `max_tris`) →
   `view_thumbs` để nhìn tận mắt → `find_similar` khi gần đúng. Chỉ tự tạo (Tripo, code Blender, mua gói) khi
   thư viện không có model hợp — ghi một dòng lý do vào task / commit.
2. **Định dạng: FBX → GLB → .blend.** `.blend` là đường cuối (Unity cần Blender cài trên máy mới import được);
   cùng `family` có bản FBX / GLB thì lấy bản đó. GLB cần package `com.unity.cloud.gltfast`.
3. **Kiểm phụ thuộc trước khi tải:** `get_model` → `license` / `reuse_ok`, `deps.render_pipeline`,
   `deps.pink_risk`, `deps.shaders`, `deps.packages`, `deps.notes`. Thiếu package thì cài trước. `pink_risk`
   = material gốc dùng shader không phải URP: để `get_download` mode `auto` (= `portable`, Unity tự tạo
   material URP) hoặc cài đúng gói shader rồi mới lấy mode `original`.
4. **Tải bằng `get_download`**: chạy nguyên script nó trả ở thư mục gốc project Unity (link hết hạn sau vài
   phút — hết thì gọi lại). File vào `Assets/EZG3D/<game>/…`; không sửa tại chỗ — prefab / variant / material
   riêng của game để trong thư mục feature của mình.
5. **Kiểm sau khi import:** Console không lỗi import, material không hồng, scale đúng (`stats.dims`, mét).
   Ghi id model + license vào commit.
6. Chưa có MCP `ezg-3d` → `companion_mcp_servers` (MCP `ezg`) để cài, hỏi người dùng trước. Không lấy model
   ngoài Internet khi thư viện đã có thứ dùng được.
7. Model mới team làm ra mà game khác dùng lại được → `upload_model` (cần role uploader, owner cấp).
