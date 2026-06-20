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
        public const string CATALOG_URL =
            "https://pub-d76b7e028ac14f9bb044ebd65bccd3d9.r2.dev/unity-template/asset-catalog.json";

        // Template UPM (dependencies + scopedRegistries + localPackages .tgz).
        public const string TEMPLATE_URL =
            "https://pub-d76b7e028ac14f9bb044ebd65bccd3d9.r2.dev/unity-template/latest.json";

        // Thư mục temp tải file về (nằm trong Temp/ của project — đã gitignore).
        public const string TEMP_DIR_NAME = "EzgFeatureHub";

        // Record các .unitypackage đã cài (ProjectSettings/ — đi theo project, không vào Assets).
        public const string RECORD_DIR_NAME = "EzgFeatureHub";
        public const string RECORD_FILE_NAME = "install-record.json";

        // Prefix các package module hệ thống Unity (ẩn khỏi tab UPM cho đỡ nhiễu).
        public const string UNITY_MODULE_PREFIX = "com.unity.modules.";

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

    /// <summary>Trạng thái một UPM dependency so với Packages/manifest.json hiện tại.</summary>
    public enum UpmStatus
    {
        NotInstalled,
        Installed,
        Different,
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
    }

    [System.Serializable]
    public class AssetCatalog
    {
        public int schemaVersion;
        public string description;
        public List<CatalogAsset> assets = new List<CatalogAsset>();
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

    [System.Serializable]
    public class InstallRecord
    {
        public List<InstalledUnityPackage> unityPackages = new List<InstalledUnityPackage>();
    }
}
