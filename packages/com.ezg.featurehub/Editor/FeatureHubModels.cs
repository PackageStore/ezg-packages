// EZG Feature Hub — data models + hằng số cấu hình.
// Catalog/template được host trên Cloudflare R2 (xem repo ezg-packages).
// URL hardcode cố ý: tool này sẽ được đóng thành UPM package, đổi URL = bump version.
using System.Collections.Generic;

namespace Ezg.FeatureHub.Editor
{
    /// <summary>Hằng số cấu hình của Feature Hub (URL nguồn, đường dẫn local).</summary>
    public static class FeatureHubConstants
    {
        // Catalog .unitypackage (tải về → import → xóa temp).
        // Từ v0.2.0 mọi URL đi qua gateway có xác thực Google — xem EzgAuth.
        public const string CATALOG_URL =
            "https://upm-registry-worker.developer-a1f.workers.dev/template/asset-catalog.json";

        // Template UPM (dependencies + scopedRegistries + localPackages .tgz).
        public const string TEMPLATE_URL =
            "https://upm-registry-worker.developer-a1f.workers.dev/template/latest.json";

        // Index các dự án feature đã làm (A002, ST001, R001, M001...). Mỗi project trỏ tới một
        // catalog.json riêng (shape giống asset-catalog.json) — tải lazy khi user chọn project.
        public const string FEATURES_INDEX_URL =
            "https://upm-registry-worker.developer-a1f.workers.dev/template/features/index.json";

        // Catalog tài sản AI của Claude (skill, command, agent, rule, docs...). Khác 3 nguồn trên ở
        // chỗ nội dung KHÔNG nằm trong Assets/ mà là file tooling ở project root (.claude/...), nên
        // payload là .zip có entry ghi theo path tương đối project root — xem FeatureHubAiInstaller.
        public const string AI_INDEX_URL =
            "https://upm-registry-worker.developer-a1f.workers.dev/template/ai/index.json";

        // Thư mục temp tải file về (nằm trong Temp/ của project — đã gitignore).
        public const string TEMP_DIR_NAME = "EzgFeatureHub";

        // Record các .unitypackage đã cài (ProjectSettings/ — đi theo project, không vào Assets).
        public const string RECORD_DIR_NAME = "EzgFeatureHub";
        public const string RECORD_FILE_NAME = "install-record.json";

        // Prefix các package module hệ thống Unity (ẩn khỏi tab UPM cho đỡ nhiễu).
        public const string UNITY_MODULE_PREFIX = "com.unity.modules.";

        // Runtime dependency của chính Feature Hub: rlottie (cung cấp assembly LottiePlugin.Runtime),
        // dùng để render icon Lottie động trong editor. UPM KHÔNG resolve git-dependency gián tiếp khai
        // báo trong package.json -> không thể đưa vào "dependencies". Vì vậy Feature Hub tự ghi git-url
        // này vào Packages/manifest.json của project khi load (xem FeatureHubRuntimeDependency); code
        // dùng LottiePlugin được guard bằng define EZG_HAS_RLOTTIE nên vẫn compile khi gói chưa có.
        public const string RLOTTIE_PACKAGE_NAME = "com.gindemit.rlottie";
        public const string RLOTTIE_PACKAGE_URL =
            "https://github.com/gindemit/unity-rlottie.git?path=/unity/RLottieUnity/Assets/LottiePlugin#d692157d134918d63cdbfa11c16ade7212145ab1";

        // Scoped registry mặc định của EZG — khớp Packages/manifest.json của dự án hiện tại.
        // Dùng làm fallback khi template từ server chưa khai báo scopedRegistries.
        public const string EZG_REGISTRY_NAME = "Easygoing code base";
        public const string EZG_REGISTRY_URL = "https://upm-registry-worker.developer-a1f.workers.dev";
        public static readonly string[] EZG_REGISTRY_SCOPES =
            { "com.ezg", "com.cysharp", "com.google", "com.coffee" };
    }

    /// <summary>Cách import .unitypackage. Ask = hỏi user mỗi lần.</summary>
    public enum ImportMode
    {
        Ask = 0,
        Silent = 1,
        Dialog = 2,
    }

    /// <summary>Trạng thái một .unitypackage so với record local.</summary>
    public enum UnityPackageStatus
    {
        NotInstalled,
        Installed,
        UpdateAvailable,
    }

    /// <summary>Trạng thái một AI item (skill/command/...) so với record local + file trên đĩa.</summary>
    public enum AiItemStatus
    {
        NotInstalled,
        Installed,
        UpdateAvailable,
    }

    /// <summary>Trạng thái một UPM dependency so với Packages/manifest.json hiện tại.
    /// UpdateAvailable = đã cài nhưng target của template mới hơn bản hiện tại (chỉ nâng cấp, không hạ).</summary>
    public enum UpmStatus
    {
        NotInstalled,
        Installed,
        UpdateAvailable,
    }

    // ---- asset-catalog.json ----

    [System.Serializable]
    public class CatalogAsset
    {
        public string name;
        public string fileName;
        public string url;
        public string category;
        public string sha256;
        public bool installedByDefault;

        // Dấu chân nhận diện "đã có sẵn" cho .unitypackage (không để lại vết trong manifest).
        // Nếu BẤT KỲ path/guid nào dưới đây trỏ tới asset đang tồn tại trong project thì coi là đã cài,
        // kể cả khi import thủ công / trước khi có Feature Hub / trên máy khác (không có install-record).
        // Tùy chọn — entry không khai báo marker giữ nguyên hành vi cũ (chỉ dựa vào install-record).
        public string[] markerPaths;   // path tương đối project, vd "Assets/Plugins/Sirenix" (file hoặc folder)
        public string[] markerGuids;   // GUID asset đại diện (ổn định theo .meta của asset gốc)
    }

    [System.Serializable]
    public class AssetCatalog
    {
        public int schemaVersion;
        public string description;
        public List<CatalogAsset> assets = new List<CatalogAsset>();
    }

    // ---- features/index.json ----

    /// <summary>Một dự án feature trong index (vd M001). catalogUrl trỏ tới catalog.json riêng của project.</summary>
    [System.Serializable]
    public class FeatureProject
    {
        public string id;                              // mã dự án, vd "M001"
        public string name;                            // tên hiển thị (fallback về id nếu rỗng)
        public string catalogUrl;                      // URL catalog.json của project (shape = asset-catalog.json)
        public int featureCount;                       // số feature trong project (để hiển thị nhanh)
        public List<string> categories = new List<string>(); // các danh mục có trong project
    }

    /// <summary>Index liệt kê mọi dự án feature. Data-driven: thêm project ở server là tự hiện lên UI.</summary>
    [System.Serializable]
    public class FeaturesIndex
    {
        public int schemaVersion;
        public string description;
        public List<FeatureProject> projects = new List<FeatureProject>();
    }

    // ---- ai/index.json ----

    /// <summary>Một danh mục AI (Skills, Commands, Agents...). Chỉ để hiển thị tên + mô tả nhóm.</summary>
    [System.Serializable]
    public class AiCategory
    {
        public string id;            // khóa dùng trong AiItem.category, vd "Skills"
        public string name;          // tên hiển thị, vd "Backlog Templates"
        public string description;   // mô tả ngắn của nhóm
        public int count;            // số item trong nhóm (server tính sẵn)
    }

    /// <summary>
    /// Một tài sản AI cài lẻ được: 1 skill, 1 command, 1 agent, 1 rule... Payload là .zip mà mỗi
    /// entry đã là path tương đối project root, nên cài = giải nén thẳng xuống project root.
    /// </summary>
    [System.Serializable]
    public class AiItem
    {
        public string id;                              // "<Category>/<name>", vd "Skills/ui-kit"
        public string name;
        public string category;                        // trỏ tới AiCategory.id
        public string description;
        public string fileName;                        // "<name>.zip"
        public string url;
        public string sha256;                          // hash của .zip
        public string installPath;                     // đích trong project, vd ".claude/skills/ui-kit"
        public bool isDirectory;                       // installPath là thư mục hay 1 file
        public List<string> files = new List<string>();// mọi file trong gói (path tương đối project)
        public int fileCount;
        public long size;
        public bool installedByDefault;
        public string source;                          // "defaultsetup" | "extra"
    }

    /// <summary>Catalog AI: danh mục + toàn bộ item. Data-driven như features/index.json.</summary>
    [System.Serializable]
    public class AiCatalog
    {
        public int schemaVersion;
        public string description;
        public List<AiCategory> categories = new List<AiCategory>();
        public List<AiItem> items = new List<AiItem>();
    }

    // ---- unity-template.json (latest.json) ----

    [System.Serializable]
    public class TemplateFile
    {
        public string fileName;
        public string url;
        public string sha256;
    }

    [System.Serializable]
    public class TemplateFiles
    {
        public List<TemplateFile> localPackages = new List<TemplateFile>();
        public List<TemplateFile> unityPackages = new List<TemplateFile>();
    }

    [System.Serializable]
    public class ScopedRegistry
    {
        public string name;
        public string url;
        public List<string> scopes = new List<string>();
    }

    [System.Serializable]
    public class UnityTemplate
    {
        public int schemaVersion;
        public string templateVersion;
        public Dictionary<string, string> dependencies = new Dictionary<string, string>();
        public TemplateFiles files = new TemplateFiles();
        public List<ScopedRegistry> scopedRegistries = new List<ScopedRegistry>();
    }

    // ---- pending install (SessionState, sống sót qua domain reload) ----

    /// <summary>
    /// Một .unitypackage đang được import. Lưu trước khi gọi ImportPackage để nếu gói chứa
    /// script gây domain reload (xóa closure trong RAM) thì finalizer vẫn ghi được record.
    /// </summary>
    [System.Serializable]
    public class PendingInstall
    {
        public string name;
        public string fileName;
        public string sha256;
        public string tempPath;
    }

    [System.Serializable]
    public class PendingInstallList
    {
        public List<PendingInstall> items = new List<PendingInstall>();
    }

    // ---- install-record.json (local) ----

    [System.Serializable]
    public class InstalledUnityPackage
    {
        public string name;
        public string fileName;
        public string sha256;
        public string installedAtUtc;
    }

    /// <summary>
    /// Một AI item đã cài. Lưu cả danh sách file đã ghi để gỡ/cập nhật xóa đúng thứ mình tạo ra,
    /// không đụng file khác mà user tự thêm vào cùng thư mục.
    /// </summary>
    [System.Serializable]
    public class InstalledAiItem
    {
        public string id;
        public string name;
        public string category;
        public string sha256;
        public string installPath;
        public List<string> files = new List<string>();
        public string installedAtUtc;
    }

    [System.Serializable]
    public class InstallRecord
    {
        public List<InstalledUnityPackage> unityPackages = new List<InstalledUnityPackage>();
        public List<InstalledAiItem> aiItems = new List<InstalledAiItem>();
    }
}
