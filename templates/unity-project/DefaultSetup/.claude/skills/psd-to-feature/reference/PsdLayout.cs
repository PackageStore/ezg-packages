#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// COPY MỘT LẦN vào Assets/_Project/Editor/ProjectSpecific/PsdLayoutImport/ rồi dùng chung cho
// mọi PSD. Không copy thành nhiều bản đổi tên — cùng một asmdef, hai bản là trùng type name.
//
// Ba thứ trong đây:
//   PsdLayout — đọc manifest do psd_export.py xuất: rect + sprite theo tên layer.
//   PsdCanvas — quy đổi rect PSD -> RectTransform trong Canvas (neo MÉP, xem chú thích).
//   PsdWorld  — quy đổi rect PSD -> toạ độ world cho SpriteRenderer trong scene.
//   PsdBuild  — dựng node Image/Text/rỗng, tìm node theo tên.
namespace Ezg.Editor.ProjectSpecific.PsdLayoutImport
{
    /// <summary>Đọc manifest `psd_export.py` xuất ra + nạp sprite theo tên layer.</summary>
    internal sealed class PsdLayout
    {
        [System.Serializable]
        private class Entry
        {
            public string name;
            public string group;
            public int x, y, w, h;
        }

        [System.Serializable]
        private class Manifest
        {
            public int width, height;
            public Entry[] layers;
        }

        private readonly Dictionary<string, Entry> _entries = new();
        private readonly string _dir;

        internal int Width { get; private set; }
        internal int Height { get; private set; }

        private PsdLayout(string dir, Manifest manifest)
        {
            _dir = dir;
            Width = manifest.width;
            Height = manifest.height;
            foreach (var entry in manifest.layers) _entries[entry.name] = entry;
        }

        internal static PsdLayout Load(string dir, string manifestName)
        {
            var path = Path.Combine(dir, manifestName);
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (asset == null)
            {
                Debug.LogError($"[PSD] Chưa có {path}. Chạy trước psd_export.py (xem skill psd-to-feature).");
                return null;
            }

            var manifest = JsonUtility.FromJson<Manifest>(asset.text);
            if (manifest?.layers == null || manifest.layers.Length == 0)
            {
                Debug.LogError($"[PSD] {path} rỗng hoặc sai định dạng.");
                return null;
            }

            return new PsdLayout(dir, manifest);
        }

        internal bool Has(string name)
        {
            return _entries.ContainsKey(name);
        }

        /// <summary>Bbox của layer trong PSD: gốc góc TRÊN-TRÁI artboard, y tăng xuống dưới.</summary>
        internal Rect Rect(string name)
        {
            if (_entries.TryGetValue(name, out var entry))
                return new Rect(entry.x, entry.y, entry.w, entry.h);
            Debug.LogWarning($"[PSD] Layer '{name}' không có trong manifest — dùng rect rỗng.");
            return new Rect(0f, 0f, 100f, 100f);
        }

        internal Sprite Sprite(string name)
        {
            if (!_entries.TryGetValue(name, out var entry)) return null;
            var path = $"{_dir}/{entry.group}/{name}.png";
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) Debug.LogWarning($"[PSD] Không nạp được sprite {path}.");
            return sprite;
        }
    }

    /// <summary>
    ///     Đặt node trong Canvas theo toạ độ PSD.
    ///
    ///     Vì sao neo MÉP chứ không neo theo tỉ lệ x/PsdW: CanvasScaler Expand + máy chạy từ
    ///     9:20 tới iPad nên bề ngang canvas co giãn rất rộng. Neo theo tỉ lệ thì trên máy rộng
    ///     cả cụm trôi vào giữa, lề trái/phải phình không đều. Neo mép giữ đúng khoảng cách tới
    ///     cạnh màn ở mọi tỉ lệ. Chiều DỌC luôn là offset tuyệt đối từ mép trên hoặc mép dưới,
    ///     phần dôi ra của máy dài rơi vào khoảng giữa màn — đúng chỗ nên dôi.
    /// </summary>
    internal sealed class PsdCanvas
    {
        private readonly float _w, _h;

        internal PsdCanvas(float psdWidth, float psdHeight)
        {
            _w = psdWidth;
            _h = psdHeight;
        }

        internal void Top(RectTransform rt, Rect psd, float? horizontal = null)
        {
            Edge(rt, psd, 1f, horizontal);
        }

        internal void Bottom(RectTransform rt, Rect psd, float? horizontal = null)
        {
            Edge(rt, psd, 0f, horizontal);
        }

        /// <summary>
        ///     `horizontal`: 0 = bám mép trái, 1 = bám mép phải, bỏ trống = tự chọn mép GẦN hơn.
        ///     Cụm phải đi liền nhau (vd hai ô tiền cạnh nhau) thì truyền tay, không thì cái
        ///     nghiêng phải sẽ dạt theo mép phải và tách khỏi cái kia.
        /// </summary>
        internal void Edge(RectTransform rt, Rect psd, float verticalAnchor, float? horizontal)
        {
            var centerX = psd.x + psd.width * 0.5f;
            var centerY = psd.y + psd.height * 0.5f;
            var toLeft = centerX;
            var toRight = _w - centerX;
            var horizontalAnchor = horizontal ?? (toLeft <= toRight ? 0f : 1f);

            rt.anchorMin = rt.anchorMax = new Vector2(horizontalAnchor, verticalAnchor);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(psd.width, psd.height);
            rt.anchoredPosition = new Vector2(
                horizontalAnchor == 0f ? toLeft : -toRight,
                verticalAnchor == 1f ? -centerY : _h - centerY);
        }

        /// <summary>
        ///     Node CANH GIỮA theo bề ngang, neo mép trên (`verticalAnchor` = 1) hoặc dưới (0).
        ///
        ///     Dùng cho thứ thiết kế đặt giữa màn — logo, thanh loading, popup. Không dùng
        ///     <see cref="Edge" /> cho mấy thứ này: rect canh giữa thì hai mép cách đều nhau,
        ///     Edge chọn bừa mép trái và trên máy rộng cả cụm lệch hẳn sang trái.
        /// </summary>
        internal void Center(RectTransform rt, Rect psd, float verticalAnchor)
        {
            var centerY = psd.y + psd.height * 0.5f;

            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, verticalAnchor);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(psd.width, psd.height);
            rt.anchoredPosition = new Vector2(
                psd.x + psd.width * 0.5f - _w * 0.5f,
                verticalAnchor == 1f ? -centerY : _h - centerY);
        }

        /// <summary>
        ///     Node trải NGANG hết canvas, giữ nguyên lề trái/phải của PSD. Dùng cho thanh/hàng
        ///     thẻ mà thiết kế cho chạm gần hai cạnh màn — để cỡ cố định thì trên máy rộng chúng
        ///     thành cụm nhỏ lọt thỏm giữa màn.
        /// </summary>
        internal void StretchEdge(RectTransform rt, Rect psd, float verticalAnchor)
        {
            var left = psd.x;
            var right = _w - (psd.x + psd.width);
            var centerY = psd.y + psd.height * 0.5f;

            rt.anchorMin = new Vector2(0f, verticalAnchor);
            rt.anchorMax = new Vector2(1f, verticalAnchor);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(-(left + right), psd.height);
            rt.anchoredPosition = new Vector2((left - right) * 0.5f,
                verticalAnchor == 1f ? -centerY : _h - centerY);
        }

        /// <summary>Con trải ngang hết node cha, giữ lề mà PSD vẽ giữa hai rect.</summary>
        internal static void StretchInParent(RectTransform rt, Rect psd, Rect parent)
        {
            var left = psd.x - parent.x;
            var right = parent.x + parent.width - (psd.x + psd.width);

            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(-(left + right), psd.height);
            rt.anchoredPosition = new Vector2((left - right) * 0.5f,
                -((psd.y + psd.height * 0.5f) - (parent.y + parent.height * 0.5f)));
        }

        /// <summary>Con neo mép PHẢI node cha (nút nằm đè đầu phải một thanh).</summary>
        internal static void RightInParent(RectTransform rt, Rect psd, Rect parent)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(psd.width, psd.height);
            rt.anchoredPosition = new Vector2(
                -(parent.x + parent.width - (psd.x + psd.width * 0.5f)),
                -((psd.y + psd.height * 0.5f) - (parent.y + parent.height * 0.5f)));
        }

        /// <summary>Con đặt theo toạ độ PSD tuyệt đối, bù theo rect PSD của node cha.</summary>
        internal static void InParent(RectTransform rt, Rect psd, Rect parent)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(psd.width, psd.height);
            rt.anchoredPosition = new Vector2(
                (psd.x + psd.width * 0.5f) - (parent.x + parent.width * 0.5f),
                -((psd.y + psd.height * 0.5f) - (parent.y + parent.height * 0.5f)));
        }
    }

    /// <summary>
    ///     Quy đổi rect PSD -> toạ độ world cho prop trong scene 2D.
    ///
    ///     `pxPerUnit` = bề ngang PSD (px) / bề ngang khung hình (unit). Camera của scene phải
    ///     GHIM bề ngang (kiểu ShopCameraFit ghim nửa bề ngang = 4 unit) thì con số này mới
    ///     đúng ở mọi tỉ lệ màn. Gốc world = tâm artboard.
    /// </summary>
    internal sealed class PsdWorld
    {
        private readonly float _pxPerUnit, _originX, _originY;

        internal PsdWorld(float psdWidth, float psdHeight, float pxPerUnit)
        {
            _pxPerUnit = pxPerUnit;
            _originX = psdWidth * 0.5f;
            _originY = psdHeight * 0.5f;
        }

        /// <summary>Tâm của layer, quy ra world.</summary>
        internal Vector3 Center(Rect rect, float z = 0f)
        {
            return new Vector3(
                (rect.x + rect.width * 0.5f - _originX) / _pxPerUnit,
                (_originY - (rect.y + rect.height * 0.5f)) / _pxPerUnit,
                z);
        }

        /// <summary>
        ///     GẠCH CHÂN của layer (giữa theo ngang, đáy theo dọc). Dùng cho chỗ đứng của nhân
        ///     vật: sprite agent có pivot y = 0 nên gốc transform chính là bàn chân — lấy tâm là
        ///     người lún nửa thân xuống sàn.
        /// </summary>
        internal Vector3 Feet(Rect rect, float z = 0f)
        {
            return new Vector3(
                (rect.x + rect.width * 0.5f - _originX) / _pxPerUnit,
                (_originY - (rect.y + rect.height)) / _pxPerUnit,
                z);
        }
    }

    internal static class PsdBuild
    {
        /// <summary>Font asset dùng cho mọi text dựng bằng tool — set một lần ở đầu importer.</summary>
        internal static string FontPath;

        internal static RectTransform NewNode(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        internal static Image NewImage(string name, Transform parent, Sprite sprite)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = sprite != null;
            return image;
        }

        internal static TextMeshProUGUI NewText(string name, Transform parent, string content,
            float size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            if (!string.IsNullOrEmpty(FontPath))
            {
                var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
                if (font != null) text.font = font;
            }

            text.text = content;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            return text;
        }

        internal static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        internal static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindDeep(root.GetChild(i), name);
                if (found != null) return found;
            }

            return null;
        }

        internal static void SetActiveDeep(Transform root, string name, bool active)
        {
            var node = FindDeep(root, name);
            if (node != null) node.gameObject.SetActive(active);
        }

        /// <summary>
        ///     Tắt phần NHÌN THẤY mà không tắt GameObject — dùng khi có hệ thống runtime bật lại
        ///     object đó mỗi lần boot (khoá/mở trạm, red dot…), tắt object là bị bật lại ngay.
        /// </summary>
        internal static void HideRenderer(Transform root, string path)
        {
            var node = root.Find(path);
            if (node == null) return;
            foreach (var renderer in node.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = false;
        }

        internal static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex.StartsWith("#") ? hex : "#" + hex, out var color);
            return color;
        }

        /// <summary>Ghi field private [SerializeField] của controller (wiring sau khi dựng node).</summary>
        internal static void Set(SerializedObject so, string field, Object value)
        {
            var property = so.FindProperty(field);
            if (property == null)
            {
                Debug.LogWarning($"[PSD] Không có field '{field}' trên controller — bỏ qua.");
                return;
            }

            property.objectReferenceValue = value;
        }
    }
}
#endif
