#if UNITY_EDITOR
using System;
using System.IO;
using System.Text.RegularExpressions;
using Ezg.Editor.Shared.EzgKit;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Ezg.Editor.Shared.Readiness
{
    /// <summary>
    ///     Nút "đi tới chỗ sửa" / "sửa luôn" gắn vào từng mục readiness. Hai loại, cố ý tách:
    ///     <list type="bullet">
    ///         <item><b>Điều hướng</b> (chọn asset, mở script đúng dòng, mở trang Project Settings, mở
    ///         tab khác của kit) — không ghi gì.</item>
    ///         <item><b>Sửa luôn</b> (tạo FirebaseConfig, sửa bucket, tắt debugAds, reimport json) — GHI
    ///         vào project, nên luôn hỏi xác nhận trước và nói rõ sẽ ghi gì.</item>
    ///     </list>
    ///     <para>
    ///         Mọi action chạy qua <see cref="Defer" /> — ngoài lượt OnGUI. Mở Project Settings, đổi
    ///         Selection, hiện dialog hay ghi asset ngay trong lúc vẽ là đổi số widget giữa Layout và
    ///         Repaint (xem quy tắc snapshot trong <c>FirebaseSetupPage</c>).
    ///     </para>
    /// </summary>
    internal static class ReadinessActions
    {
        #region Run

        /// <summary>Hoãn tới sau lượt vẽ. Exception bị bắt ở đây để không cắt đứt các delayCall khác.</summary>
        internal static void Defer(Action action)
        {
            EditorApplication.delayCall += () =>
            {
                try
                {
                    action();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            };
        }

        #endregion

        #region Navigate

        /// <summary>Chọn + ping asset theo đường dẫn `Assets/…` (Project window nhảy tới, Inspector hiện nó).</summary>
        internal static (string, Action) SelectAsset(string label, string assetPath) =>
            (label, () =>
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
                if (asset == null)
                {
                    Debug.LogWarning($"[Readiness] Không thấy asset: {assetPath}");
                    return;
                }

                Select(asset);
            });

        internal static (string, Action) SelectObject(string label, Object asset) =>
            (label, () => Select(asset));

        private static void Select(Object asset)
        {
            if (asset == null) return;
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        /// <summary>
        ///     Mở script trong IDE ở đúng dòng khớp <paramref name="linePattern" /> (regex); không khớp thì
        ///     mở dòng 1. <paramref name="absolutePath" /> là đường dẫn tuyệt đối (từ <c>FindScript</c>).
        /// </summary>
        internal static (string, Action) OpenScript(string label, string absolutePath, string linePattern = null) =>
            (label, () =>
            {
                if (string.IsNullOrEmpty(absolutePath) || !File.Exists(absolutePath))
                {
                    Debug.LogWarning($"[Readiness] Không thấy file: {absolutePath}");
                    return;
                }

                var line = 1;
                if (!string.IsNullOrEmpty(linePattern))
                {
                    var lines = File.ReadAllLines(absolutePath);
                    var regex = new Regex(linePattern);
                    for (var i = 0; i < lines.Length; i++)
                        if (regex.IsMatch(lines[i]))
                        {
                            line = i + 1;
                            break;
                        }
                }

                InternalEditorUtility.OpenFileAtLineExternal(absolutePath, line);
            });

        /// <summary>File ngoài Assets (script build, json ở ProjectSettings) — hiện trong Finder/Explorer.</summary>
        internal static (string, Action) Reveal(string label, string absolutePath) =>
            (label, () =>
            {
                if (File.Exists(absolutePath)) EditorUtility.RevealInFinder(absolutePath);
                else Debug.LogWarning($"[Readiness] Không thấy file: {absolutePath}");
            });

        /// <summary>Trang Project Settings, ví dụ <c>Project/Player</c>, <c>Project/Services</c>.</summary>
        internal static (string, Action) ProjectSettings(string label, string page) =>
            (label, () => SettingsService.OpenProjectSettings(page));

        internal static (string, Action) PackageManager(string label, string packageName) =>
            (label, () => UnityEditor.PackageManager.UI.Window.Open(packageName));

        /// <summary>Nhảy sang tab khác của kit — dữ liệu gốc (sheet marketing, app Firebase) sửa ở đó.</summary>
        internal static (string, Action) KitTab(string label, EzgKitWindow.Tab tab) =>
            (label, () => EzgKitWindow.Open(tab));

        #endregion

        #region Fix (ghi vào project — luôn hỏi trước)

        private static bool Confirm(string what) =>
            EditorUtility.DisplayDialog("EzgKit - Readiness", what + "\n\nTiep tuc?", "Sua", "Huy");

        /// <summary>Reimport để generator của Firebase ghi lại google-services.xml từ json.</summary>
        internal static (string, Action) Reimport(string label, string assetPath) =>
            (label, () =>
            {
                if (!Confirm($"Reimport {assetPath}.\nGenerator cua Firebase se ghi lai google-services.xml.")) return;
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            });

        /// <summary>Gán một string property trên asset qua SerializedObject (bucket, key…).</summary>
        internal static (string, Action) SetString(string label, Object asset, string property, string value) =>
            (label, () =>
            {
                var path = AssetDatabase.GetAssetPath(asset);
                if (!Confirm($"Ghi {property} = \"{value}\" vao {path}.")) return;

                var so = new SerializedObject(asset);
                var prop = so.FindProperty(property);
                if (prop == null)
                {
                    Debug.LogError($"[Readiness] {path} không có property `{property}`.");
                    return;
                }

                prop.stringValue = value;
                so.ApplyModifiedProperties();
                AssetDatabase.SaveAssetIfDirty(asset);
                Select(asset);
            });

        internal static (string, Action) SetBool(string label, Object asset, string property, bool value) =>
            (label, () =>
            {
                var path = AssetDatabase.GetAssetPath(asset);
                if (!Confirm($"Ghi {property} = {value} vao {path}.")) return;

                var so = new SerializedObject(asset);
                var prop = so.FindProperty(property);
                if (prop == null)
                {
                    Debug.LogError($"[Readiness] {path} không có property `{property}`.");
                    return;
                }

                prop.boolValue = value;
                so.ApplyModifiedProperties();
                AssetDatabase.SaveAssetIfDirty(asset);
                Select(asset);
            });

        /// <summary>
        ///     Tạo <c>Resources/FirebaseConfig.asset</c> (type <c>Ezg.Core.Firebase.FirebaseConfig</c> của
        ///     package com.ezg.firebase, tìm bằng reflection — package này không tham chiếu nó) với bucket
        ///     đúng project Firebase đang dùng. Đặt cạnh AdsConfig nếu có — cùng một thư mục Resources
        ///     cho config của dự án — không thì <c>Assets/Resources</c>.
        /// </summary>
        internal static (string, Action) CreateFirebaseConfig(string label, string bucketUrl) =>
            (label, () =>
            {
                Type type = null;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    type = assembly.GetType("Ezg.Core.Firebase.FirebaseConfig");
                    if (type != null) break;
                }

                if (type == null)
                {
                    EditorUtility.DisplayDialog("EzgKit - Readiness",
                        "Khong thay type Ezg.Core.Firebase.FirebaseConfig — du an chua cai com.ezg.firebase.", "OK");
                    return;
                }

                var folder = "Assets/Resources";
                var sibling = Resources.Load<ScriptableObject>("AdsConfig");
                if (sibling != null) folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(sibling))?.Replace('\\', '/') ?? folder;
                var path = folder + "/FirebaseConfig.asset";

                if (!Confirm($"Tao {path}\nstorageBucketUrl = {bucketUrl ?? "(de trong)"}")) return;

                if (!AssetDatabase.IsValidFolder(folder))
                {
                    Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? "", folder));
                    AssetDatabase.Refresh();
                }

                var asset = ScriptableObject.CreateInstance(type);
                if (!string.IsNullOrEmpty(bucketUrl))
                {
                    var so = new SerializedObject(asset);
                    var prop = so.FindProperty("storageBucketUrl");
                    if (prop != null)
                    {
                        prop.stringValue = bucketUrl;
                        so.ApplyModifiedPropertiesWithoutUndo();
                    }
                }

                AssetDatabase.CreateAsset(asset, path);
                AssetDatabase.SaveAssets();
                Select(asset);
            });

        #endregion
    }
}
#endif
