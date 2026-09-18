#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityFigmaBridge.Editor.Utils;

namespace Ezg.FigmaFeatureImporter.Editor
{
    public enum FigmaScreenLayout
    {
        Popup,
        FullScreen
    }

    public enum FigmaBodyAnchor
    {
        /// <summary>Thân = đúng khổ frame Figma neo tâm canvas (máy dài chia phần dôi đều, nếp PsdCanvas.Middle).</summary>
        CenterFixed,
        /// <summary>Thân trải theo canvas để constraint Figma bám mép có tác dụng (HUD, màn full).</summary>
        Stretch
    }

    [Serializable]
    public sealed class FigmaNodeRename
    {
        public string from;
        public string to;
    }

    [Serializable]
    public sealed class FigmaBinding
    {
        [Tooltip("Tên node Figma (sau renameNodes)")] public string node;
        [Tooltip("Tên field [SerializeField] trên controller")] public string field;
    }

    /// <summary>Một frame Figma → một màn. Xem bảng field trong README của package.</summary>
    [Serializable]
    public sealed class FigmaScreenEntry
    {
        [Tooltip("Tên frame trên page Screens của Figma")] public string figmaScreen;
        public bool enabled = true;

        [Tooltip("Assets/_Project/Features/<Domain>/<Feature>/Resources/screen_<snake>.prefab")]
        public string outputPrefab;

        [Tooltip("Thư mục nhận PNG copy từ Assets/Figma khi Features chưa có ảnh trùng byte")]
        public string spriteFolder;

        public FigmaScreenLayout layout = FigmaScreenLayout.Popup;
        public FigmaBodyAnchor bodyAnchor = FigmaBodyAnchor.CenterFixed;

        [Tooltip("Figma đã vẽ khung riêng: tắt BG/top_container_popup của template, stretch chuỗi container")]
        public bool replaceTemplateFrame;

        [Tooltip("FullScreen: giữ top_view/botview của template (mặc định tắt vì màn Figma tự vẽ chrome)")]
        public bool keepFullScreenChrome;

        [Tooltip("FullScreen: bật Image nền của full_screen_template")]
        public bool fullScreenBackdrop;

        [Tooltip("Giữ fill của chính frame Figma (artboard). Mặc định bỏ: nền trắng artboard sẽ che game phía sau popup")]
        public bool keepFrameFill;

        [Tooltip("Assembly-qualified, vd My.Game.Feature.ShopController, Assembly-CSharp. Trống = chỉ dựng cấu trúc")]
        public string controllerType;

        [Tooltip("Tên member của enum trên field FeatureType của controller, vd Shop")] public string featureType;
        public bool clickBackgroundToExit = true;
        public float backgroundAlpha = 0.6f;

        [Tooltip("Node bỏ hẳn: lớp tối nền designer vẽ, nền demo…")] public List<string> dropNodes = new();
        [Tooltip("Đổi tên node trước khi wire/codegen (node trùng tên đã được đánh số _1, _2)")]
        public List<FigmaNodeRename> renameNodes = new();
        [Tooltip("Node vẽ rời là nút → thay bằng defaultButtonTemplate")] public List<string> buttonNodes = new();
        [Tooltip("Text_* là giá trị runtime (không sinh key/fallback localize)")] public List<string> dynamicTextNodes = new();
        [Tooltip("Wire tường minh khi node Figma chưa theo quy ước tên")] public List<FigmaBinding> bindings = new();

        [Tooltip("Sinh <X>Controller.Figma.cs từ tên node (controller phải là partial)")]
        public bool generateBindings;

        [Tooltip("Tiền tố key localize, vd bulk_sale → bulk_sale_title")] public string localizeKeyPrefix;
    }

    /// <summary>Một component Figma (khớp prefix đường dẫn prefab) → một template project.</summary>
    [Serializable]
    public sealed class FigmaTemplateMap
    {
        [Tooltip("Prefix đường dẫn prefab component do bridge ghi, tính từ thư mục Components: btn_close, Bg_btn, Scroll View…")]
        public string figmaComponent;

        [Tooltip("Đường dẫn template tính từ Prefabs/Templates/, không .prefab")] public string template;
        [Tooltip("Path đủ từ root template tới node nhận chữ (TMP đầu tiên của node Figma)")] public string textSlot;
        [Tooltip("Path đủ tới Image nhận icon (node Figma tên Icon*)")] public string iconSlot;
        [Tooltip("Path đủ tới Image mặt nút (sprite plate lớn nhất của node Figma)")] public string faceSlot;
        [Tooltip("Path đủ tới node nhận toàn bộ con của node Figma (vd Viewport/Content)")] public string childrenSlot;
        [Tooltip("Node trong template tắt đi")] public List<string> deactivate = new();
        [Tooltip("Button của template này đi vào _closeButtons")] public bool isCloseButton;
    }

    /// <summary>
    ///     Data cho <see cref="FigmaFeatureImporter" />: danh sách màn, bảng map component → template và
    ///     mọi đường dẫn/tên node của vỏ project. Asset tìm theo type ở bất cứ đâu trong Assets; chưa có
    ///     thì <see cref="LoadOrCreate" /> tạo tại <see cref="DEFAULT_ASSET_PATH" /> với seed mặc định.
    ///     ⚠️ Xoá asset là re-seed toàn bộ — mất map/entry tay.
    /// </summary>
    public sealed class FigmaFeatureImportSettings : ScriptableObject
    {
        /// <summary>Nơi tạo asset khi project chưa có. Asset đã tồn tại ở đâu cũng được — tìm theo type.</summary>
        public const string DEFAULT_ASSET_PATH = "Assets/FigmaFeatureImportSettings.asset";

        #region Vỏ project (mặc định = template EZG; đổi cho project khác)

        [Tooltip("Prefab vỏ mà mọi màn sinh ra là variant của nó.\n" +
                 "Ví dụ: Assets/_Project/Visual/ArtAsset/Shared/Resources/Prefabs/Templates/Popup_Template/screen_template.prefab")]
        public string screenTemplatePath =
            "Assets/_Project/Visual/ArtAsset/Shared/Resources/Prefabs/Templates/Popup_Template/screen_template.prefab";

        [Tooltip("Thư mục gốc chứa template project; field template của map tính tương đối từ đây.\n" +
                 "Ví dụ: Assets/_Project/Visual/ArtAsset/Shared/Resources/Prefabs/Templates/")]
        public string templatesRoot = "Assets/_Project/Visual/ArtAsset/Shared/Resources/Prefabs/Templates/";

        [Tooltip("Nơi chụp bản thô của bridge trước khi ghi đè tại chỗ.\nVí dụ: Assets/Figma/RawScreens")]
        public string rawScreensFolder = "Assets/Figma/RawScreens";

        [Tooltip("Thư mục quét PNG trùng byte để tái dùng sprite thay vì copy.\nVí dụ: Assets/_Project/Features")]
        public string spriteReuseRoot = "Assets/_Project/Features";

        [Tooltip("Font mặc định nạp khi field font trống.\nVí dụ: Assets/.../TiltWarp2 SDF.asset")]
        public string defaultFontPath = "Assets/_Project/Visual/ArtAsset/Shared/Resources/TiltWarp2 SDF.asset";

        [Tooltip("Node nền bấm-để-đóng trên vỏ.\nVí dụ: background_button")]
        public string backgroundNode = "background_button";

        [Tooltip("Nhánh popup trên vỏ.\nVí dụ: popup_template")]
        public string popupNode = "popup_template";

        [Tooltip("Nhánh full screen trên vỏ.\nVí dụ: full_screen_template")]
        public string fullScreenNode = "full_screen_template";

        [Tooltip("Đường dẫn từ nhánh popup tới node nhận thân; mọi node trên đường đi được stretch khi replaceTemplateFrame.\n" +
                 "Ví dụ: popup_container/container_content/container")]
        public string popupBodyPath = "popup_container/container_content/container";

        [Tooltip("Node khung của template popup tắt đi khi replaceTemplateFrame (tính từ nhánh popup).\n" +
                 "Ví dụ: popup_container/BG")]
        public List<string> popupFrameNodes = new() { "popup_container/BG", "popup_container/top_container_popup" };

        [Tooltip("Node nhận thân trên nhánh full screen.\nVí dụ: content")]
        public string fullScreenContentNode = "content";

        [Tooltip("Chrome của template full screen tắt đi khi keepFullScreenChrome = false (tính từ nhánh full screen).\n" +
                 "Ví dụ: top_view")]
        public List<string> fullScreenChromeNodes = new() { "top_view", "botview" };

        [Tooltip("Slot mặt nút trên template nút, dùng cho buttonNodes.\nVí dụ: btn_container/btn")]
        public string buttonFaceSlot = "btn_container/btn";

        [Tooltip("Slot chữ trên template nút; template không có slot này thì bỏ qua.\nVí dụ: btn_container/container/text_body")]
        public string buttonTextSlot = "btn_container/container/text_body";

        [Tooltip("Regex tên node đánh dấu màn là popup khi frame chưa khai entry.\nVí dụ: ^(Bg_Dim|Bg_Popup|Container_Popup)")]
        public string popupMarkerPattern = "^(Bg_Dim|Bg_Popup|Container_Popup)";

        [Tooltip("Thư mục ghi PNG snapshot của audit (ngoài Assets cũng được).\nVí dụ: .claude/tmp")]
        public string snapshotFolder = ".claude/tmp";

        #endregion

        #region Codegen (file <X>Controller.Figma.cs)

        [Tooltip("using sinh vào đầu file partial.\nVí dụ: Ezg.Feature.Shared.Localize")]
        public List<string> codegenUsings = new()
            { "Ezg.Feature.Shared.Localize", "TMPro", "UnityEngine", "UnityEngine.UI" };

        [Tooltip("Biểu thức lấy chữ tĩnh; {0} = const KEY, {1} = const FALLBACK. Trống = gán thẳng FALLBACK.\n" +
                 "Ví dụ: LocalizeFallback.Get({0}, {1})")]
        public string localizeCallFormat = "LocalizeFallback.Get({0}, {1})";

        [Tooltip("Tên lớp cơ sở của controller màn hình (khớp theo tên đơn, không cần namespace).\n" +
                 "Ví dụ: FeatureBaseController")]
        public string baseControllerTypeName = "FeatureBaseController";

        #endregion

        public List<FigmaScreenEntry> screens = new();

        public List<FigmaTemplateMap> templates = new();

        [Tooltip("Template cho buttonNodes, tính từ Prefabs/Templates/")]
        public string defaultButtonTemplate = "Button_Template/button_template";

        [Tooltip("Font duy nhất của project (luật [FONT])")]
        public TMP_FontAsset font;

        public float textWidthPadding = 1.25f;
        public float textHeightPadding = 1.1f;

        /// <summary>
        ///     Asset của project: tìm theo type ở bất cứ đâu trong Assets (project tự chọn chỗ để),
        ///     chưa có thì tạo tại <see cref="DEFAULT_ASSET_PATH" /> với seed mặc định.
        /// </summary>
        public static FigmaFeatureImportSettings LoadOrCreate()
        {
            var settings = Find();
            if (settings == null)
            {
                settings = CreateInstance<FigmaFeatureImportSettings>();
                settings.SeedDefaults();
                var directory = Path.GetDirectoryName(DEFAULT_ASSET_PATH);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                AssetDatabase.CreateAsset(settings, DEFAULT_ASSET_PATH);
                AssetDatabase.SaveAssets();
                Debug.Log($"[FigmaImport] Tạo {DEFAULT_ASSET_PATH} với seed mặc định ({settings.templates.Count} map, {settings.screens.Count} màn).");
            }

            if (settings.font == null && !string.IsNullOrEmpty(settings.defaultFontPath))
            {
                settings.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(settings.defaultFontPath);
                if (settings.font != null) EditorUtility.SetDirty(settings);
            }
            return settings;
        }

        /// <summary>Asset đang có trong project (null nếu chưa tạo). Nhiều asset → lấy cái đầu + cảnh báo.</summary>
        public static FigmaFeatureImportSettings Find()
        {
            var guids = AssetDatabase.FindAssets("t:" + nameof(FigmaFeatureImportSettings));
            if (guids.Length == 0) return null;
            if (guids.Length > 1)
                Debug.LogWarning($"[FigmaImport] Có {guids.Length} FigmaFeatureImportSettings trong project — dùng " +
                                 $"'{AssetDatabase.GUIDToAssetPath(guids[0])}'. Xoá bớt để khỏi nhầm.");
            return AssetDatabase.LoadAssetAtPath<FigmaFeatureImportSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        /// <summary>Đường dẫn asset này (để in ra log/report).</summary>
        public string AssetPath()
        {
            var path = AssetDatabase.GetAssetPath(this);
            return string.IsNullOrEmpty(path) ? DEFAULT_ASSET_PATH : path;
        }

        public FigmaScreenEntry FindScreen(string frameName)
        {
            if (string.IsNullOrEmpty(frameName)) return null;
            return screens.Find(s => s != null && s.figmaScreen == frameName);
        }

        public string TemplatePath(string relative)
        {
            if (string.IsNullOrWhiteSpace(relative)) return null;
            var value = relative.Trim().Replace('\\', '/');
            if (value.StartsWith("Assets/", StringComparison.Ordinal))
                return value.EndsWith(".prefab", StringComparison.Ordinal) ? value : value + ".prefab";
            if (!value.EndsWith(".prefab", StringComparison.Ordinal)) value += ".prefab";
            var root = string.IsNullOrWhiteSpace(templatesRoot) ? string.Empty : templatesRoot.Replace('\\', '/').TrimEnd('/') + "/";
            return root + value;
        }

        /// <summary>Map cho prefab component <paramref name="componentPrefabPath" /> (khớp prefix, ăn mọi variant).</summary>
        public FigmaTemplateMap FindTemplateMap(string componentPrefabPath)
        {
            var relative = RelativeComponentPath(componentPrefabPath);
            if (string.IsNullOrEmpty(relative)) return null;
            foreach (var map in templates)
            {
                if (map == null || string.IsNullOrWhiteSpace(map.figmaComponent) || string.IsNullOrWhiteSpace(map.template)) continue;
                var key = NormalizeKey(map.figmaComponent);
                if (key.Length == 0) continue;
                if (relative == key) return map;
                if (relative.StartsWith(key + "/", StringComparison.Ordinal)) return map;
                if (Regex.IsMatch(relative, "^" + Regex.Escape(key) + @"_\d+$")) return map;
            }
            return null;
        }

        /// <summary>Đường dẫn prefab component tính từ thư mục Components của bridge, bỏ .prefab.</summary>
        private static string RelativeComponentPath(string componentPrefabPath)
        {
            if (string.IsNullOrEmpty(componentPrefabPath)) return null;
            var path = componentPrefabPath.Replace('\\', '/');
            var folder = FigmaPaths.FigmaComponentPrefabFolder.Replace('\\', '/').TrimEnd('/') + "/";
            if (path.StartsWith(folder, StringComparison.Ordinal)) path = path.Substring(folder.Length);
            else
            {
                var marker = path.IndexOf("/Components/", StringComparison.Ordinal);
                if (marker >= 0) path = path.Substring(marker + "/Components/".Length);
            }
            if (path.EndsWith(".prefab", StringComparison.Ordinal)) path = path.Substring(0, path.Length - ".prefab".Length);
            return path;
        }

        private static string NormalizeKey(string key)
        {
            var value = key.Trim().Replace('\\', '/');
            const string absolute = "Assets/Figma/Components/";
            const string shortPrefix = "Components/";
            if (value.StartsWith(absolute, StringComparison.OrdinalIgnoreCase)) value = value.Substring(absolute.Length);
            else if (value.StartsWith(shortPrefix, StringComparison.OrdinalIgnoreCase)) value = value.Substring(shortPrefix.Length);
            if (value.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) value = value.Substring(0, value.Length - ".prefab".Length);
            return value.Trim('/');
        }

        /// <summary>
        ///     Seed cho project dựng từ template EZG: bảng map component Figma → template project +
        ///     template nút mặc định. Danh sách màn để trống — mỗi project tự khai. Sửa lại ở Inspector.
        /// </summary>
        public void SeedDefaults()
        {
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(defaultFontPath);
            defaultButtonTemplate = "Button_Template/button_template";

            templates = new List<FigmaTemplateMap>
            {
                new()
                {
                    figmaComponent = "btn_close",
                    template = "Button_Template/button_template_icon/button_close",
                    faceSlot = "btn_container/btn",
                    isCloseButton = true
                },
                new()
                {
                    figmaComponent = "Bg_btn",
                    template = "Button_Template/button_template_yellow",
                    faceSlot = "btn_container/btn"
                },
                new()
                {
                    figmaComponent = "_btn_base",
                    template = "Button_Template/button_template",
                    faceSlot = "btn_container/btn"
                },
                new()
                {
                    figmaComponent = "IconButton",
                    template = "Button_Template/button_template_icon/button_template_icon",
                    iconSlot = "btn_container/container/icon"
                },
                new()
                {
                    figmaComponent = "Menu_BottomBtn",
                    template = "Button_Template/button_template_common_txt&icon/button_home_bottom",
                    textSlot = "btn_container/container/text_body",
                    iconSlot = "btn_container/container/icon"
                },
                new()
                {
                    figmaComponent = "_currency_template",
                    template = "Money_Bar_Template/button_money_bar"
                },
                new()
                {
                    figmaComponent = "Scroll View",
                    template = "Templates/ScrollViewTemplate",
                    childrenSlot = "Viewport/Content"
                }
            };

            screens = new List<FigmaScreenEntry>();
        }
    }
}
#endif
