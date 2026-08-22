# Changelog

## 0.3.1 — 2026-08-22

**Changed**
- Ngưỡng tự gia hạn phiên: dưới **2 ngày** thay vì dưới 2 tiếng. Gateway vừa nới cửa sổ không hoạt động từ 6 tiếng lên 7 ngày, mà ngưỡng 2 tiếng chỉ là 1% của cửa sổ đó — phiên chỉ được đẩy hạn nếu tình cờ có một lần compile lại rơi đúng 2 tiếng cuối. Trượt nhịp đó là phải đăng nhập lại dù máy vẫn dùng hằng ngày. 2 ngày cho biên an toàn 5 ngày.

## 0.3.0 — 2026-08-22

**Added**
- `Ezg > Đăng nhập EZG`: đăng nhập Google ngay trong Unity bằng đúng luồng ghép cặp thiết bị mà builder dùng (xin mã → mở trình duyệt → tự nhận kết quả). Phiên hết hạn giữa buổi làm không còn buộc phải bỏ Unity ra chạy lại `build_unity_template`.
- `Ezg > Trạng thái phiên EZG`: in ra đang đăng nhập bằng email nào, còn bao lâu.
- `EzgAuth` tự gia hạn phiên mỗi lần domain reload nếu còn dưới 2 tiếng (throttle 10 phút/lần). Phiên sống theo cửa sổ *không hoạt động*, nên Editor đang mở gần như không bao giờ chạm hạn.
- `EzgAuth.WriteUpmConfig`: mỗi lần nhận token mới là ghi lại `~/.upmconfig.toml` để Package Manager dùng được ngay, giữ nguyên entry của registry khác.
- `EditorDownloader.PostJson` — POST kèm bearer, trả cả status code vì luồng ghép cặp phân biệt trạng thái bằng 202/429/403.

**Changed**
- Dialog khi gặp 401 giờ mở thẳng cửa sổ đăng nhập thay vì bảo người dùng đi tìm shell script.

## 0.2.1 — 2026-08-22

**Fixed**
- Bổ sung `EzgAuth.cs.meta`. Package cài từ registry là immutable nên Unity không tự sinh được file `.meta` còn thiếu; 0.2.0 phát hành thiếu file này.

## 0.2.0 — 2026-08-22

**Added**
- `EzgAuth`: đọc token phiên từ `~/.ezg/credentials.json` (file do `build_unity_template` tạo) hoặc biến môi trường `EZG_TOKEN` cho máy CI. Một máy chỉ có một phiên dùng chung cho cả installer lẫn Feature Hub.

**Changed**
- Mọi URL catalog/template chuyển sang gateway `…workers.dev/template/…` (trước đây là bucket public `pub-*.r2.dev`).
- `EditorDownloader` gắn `Authorization: Bearer` cho request tới gateway — và chỉ tới gateway; file bên thứ ba (Firebase trên `dl.google.com`) vẫn tải không kèm token.
- Gặp 401/403 thì báo "cần đăng nhập" kèm lệnh `./build_unity_template.sh --login` thay vì để lộ lỗi HTTP thô.

## [0.1.8] - 2026-07-14
### Fixed
- **Installing `com.ezg.featurehub` alone now works out of the box.** Feature Hub's editor code uses `Newtonsoft.Json` unconditionally (`FeatureHubService`, install record, import finalizer), but the dependency was never declared, so a fresh project without `com.unity.nuget.newtonsoft-json` failed to compile with `CS0246`. Because the compile failed, the `[InitializeOnLoad]` self-heal bootstrap never ran either — so it could not add the missing package the way it does for rlottie.
  - Declared `com.unity.nuget.newtonsoft-json` `3.2.1` in `dependencies`. UPM resolves it automatically from the Unity registry on install, whether Feature Hub is added via scoped registry **or via git URL** (`?path=/packages/com.ezg.featurehub`). Newtonsoft cannot be self-healed by the C# bootstrap (that code needs Newtonsoft to compile in the first place), so it must be a declared dependency; `com.gindemit.rlottie` stays on the runtime bootstrap because it is a git-url dependency that UPM cannot express in `dependencies`.
### Added
- **Uninstall for the "Features" tab.** Each feature card now shows a **"Gỡ"** (uninstall) button next to the install button whenever the feature is present in the project. It removes the feature by deleting the folders/assets declared in the catalog entry's `markerPaths`/`markerGuids` (each feature is a self-contained folder such as `Assets/_Project/Features/<Category>/<Name>`) and clearing the local install record, after a confirm dialog that lists exactly what will be deleted. Status reverts to "Chưa cài" on the next refresh. Uninstall is intentionally limited to the Features tab; the Unity Packages tab (third-party plugins whose markers are only partial) is unchanged.
- `FeatureHubService.UninstallUnityPackage(asset, onDone)` — resolves marker paths/guids to existing project assets, deletes them via `AssetDatabase.DeleteAsset`, refreshes, then removes the install record (record cleared first so a script-triggered domain reload cannot leave a stale "installed" state).

## [0.1.6] - 2026-06-23
### Added
- New **"Features"** tab (right of "UPM Packages") listing per-project features published as `.unitypackage`. Data-driven from a remote index (`features/index.json` → `projects[]`), with a project selector popup (A002 / ST001 / R001 / M001 / … appear automatically as they are added remotely — nothing hardcoded). Each project's catalog reuses the existing `.unitypackage` download → SHA-256 verify → import → marker/record status flow, plus a **"Cài tất cả feature còn thiếu"** batch button. Project catalogs are lazy-loaded and cached per project.
### Fixed
- **UPM tab no longer downgrades.** When the project already had a version *newer* than the template target, the card still showed an "update" button and clicking it wrote the older target version into `Packages/manifest.json` (a downgrade). Status now uses a proper semver comparison and only offers an update when the template target is strictly newer than the resolved version (`UpmStatus.Different` → `UpmStatus.UpdateAvailable`, labeled "Có bản mới").
- Renamed the UPM batch button **"Cài/cập nhật tất cả còn thiếu" → "Cập nhật tất cả còn thiếu"**; it now only upgrades already-installed packages to a newer version and skips packages that are not installed at all (never downgrades).

## [0.1.5] - 2026-06-23
### Fixed
- Installing Feature Hub on a machine that did not already have `com.gindemit.rlottie` failed to compile with `CS0246: The type or namespace name 'LottiePlugin' could not be found`. UPM cannot resolve a git dependency declared transitively in a package's `package.json`, so the rlottie runtime that powers the in-editor Lottie icons was never pulled in automatically.
  - The only file touching `LottiePlugin` (`LottieElement.cs`) is now guarded by the `EZG_HAS_RLOTTIE` define (added via asmdef `versionDefines` keyed on `com.gindemit.rlottie`). Feature Hub therefore **compiles whether or not rlottie is present** — when absent, Lottie elements degrade to an empty box instead of breaking the build.
  - A new `[InitializeOnLoad]` bootstrap (`FeatureHubRuntimeDependency`) **self-heals** the project: on editor load it ensures `com.gindemit.rlottie` (git url) is present in `Packages/manifest.json`, adding it once if missing and resolving. After Unity resolves, `EZG_HAS_RLOTTIE` switches on and the animated icons light up automatically — no manual manifest editing required. It never overwrites an existing entry.

## [0.1.3] - 2026-06-21
### Fixed
- Packages that are already present in the project are no longer reported as "not installed".
  - **UPM tab:** status is now computed from `PackageManager.Client.List` (offline, including indirect dependencies) instead of only reading direct dependencies in `Packages/manifest.json`. This detects packages that are resolved transitively, embedded, local, or are built-in modules — i.e. installed without being a literal `dependencies` entry. Version is compared against the actually resolved version; "different version" is only flagged when both the template target and the resolved version are concrete semver (so `file:`/git/range targets no longer show a false mismatch).
  - **Unity Packages tab:** a `.unitypackage` whose assets were imported manually / before the Hub existed / on another machine (no install record) is now detected via optional `markerPaths` / `markerGuids` declared per catalog entry. If any marker path/GUID resolves to an existing asset, the entry is treated as installed. Detection is evaluated live each refresh, so deleting the assets reverts the status.
### Added
- `CatalogAsset.markerPaths` and `CatalogAsset.markerGuids` (optional) in the asset catalog schema, used to recognize already-present `.unitypackage` content. Entries without markers keep the previous record-only behavior.

## [0.1.2] - 2026-06-20
### Added
- On opening the Feature Hub, the window now validates that the project's `Packages/manifest.json` declares the required scoped registries; if any are missing it shows a confirm popup and, on accept, registers them (URL + union of scopes) and resolves. Falls back to the built-in EZG registry when the remote template declares none.
### Fixed
- A `.unitypackage` that contains scripts no longer shows as "not installed" after a successful import. Importing such a package triggers a domain reload that wiped the in-memory completion callback before the install record was written; the record is now persisted via a `SessionState`-backed pending marker and finalized by an `[InitializeOnLoad]` handler that survives the reload.

## [0.1.1] - 2026-06-20
### Added
- Keyboard shortcut `Ctrl/Cmd + Shift + F` to open the Feature Hub window.
### Fixed
- Window no longer stays stuck "deactivated" after a `.unitypackage` import: teardown of the busy state no longer depends on the fragile package-name match, which fails in Dialog mode when Unity reports the package's embedded name instead of the downloaded temp file name.
- Cancelling/closing the native import dialog now reliably re-enables the window via a watchdog on the `PackageImport` window, covering the case where Unity does not fire `importPackageCancelled`.
### Added
- Initial release extracted from `Assets/_Project/Editor/FeatureHub`.
- Editor window (`Ezg > Feature Hub`) to install Unity packages from a remote catalog.
- Unity Packages tab: download + SHA-256 verify + import `.unitypackage`, with a local install record under `ProjectSettings/`.
- UPM Packages tab: write dependencies and scoped registries into `Packages/manifest.json`, download `file:` `.tgz` packages, and resolve.
- UI Toolkit interface with in-editor animated Lottie icons rendered via rlottie.
