// EZG Feature Hub — finalizer ghi install-record an toàn qua domain reload.
//
// Vì sao cần: import một .unitypackage CÓ SCRIPT khiến Unity recompile + domain reload ngay sau
// khi ghi asset ra đĩa. Domain reload xóa sạch managed state — kể cả closure đã subscribe
// importPackageCompleted trong FeatureHubService.ImportThenCleanup — nên MarkInstalled không bao
// giờ chạy => record trống => Feature Hub hiện "Chưa cài" dù gói đã vào project.
//
// Cách xử lý: trước khi ImportPackage, ghi 1 "pending install" vào SessionState (sống sót qua
// domain reload). Class [InitializeOnLoad] này chạy lại sau MỖI lần reload, re-subscribe event và
// dọn nốt pending còn sót (coi như đã import vì asset đã nằm trên đĩa từ trước khi reload).
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace Ezg.FeatureHub.Editor
{
    [InitializeOnLoad]
    public static class FeatureHubImportFinalizer
    {
        #region Constants

        private const string PENDING_KEY = "Ezg.FeatureHub.PendingInstalls";

        #endregion

        #region Initialize

        // Static ctor chạy lúc load editor VÀ sau mỗi domain reload -> luôn có subscriber sống.
        static FeatureHubImportFinalizer()
        {
            AssetDatabase.importPackageCompleted += OnImportCompleted;
            AssetDatabase.importPackageFailed += OnImportFailed;
            AssetDatabase.importPackageCancelled += OnImportCancelled;

            // Fallback: với gói có script, event ở trên có thể đã mất trước khi fire (chết theo
            // closure cũ trong lần reload). delayCall chạy sau khi reload ổn định -> dọn pending sót.
            EditorApplication.delayCall += RecoverAfterReload;
        }

        #endregion

        #region Public Methods

        /// <summary>Ghi nhận một import sắp chạy. Gọi NGAY trước AssetDatabase.ImportPackage.</summary>
        public static void AddPending(PendingInstall pending)
        {
            if (pending == null || string.IsNullOrEmpty(pending.name))
                return;

            var list = Load();
            list.items.RemoveAll(p => p.name == pending.name);
            list.items.Add(pending);
            Save(list);
        }

        /// <summary>Xóa pending (vd: import bị hủy) mà KHÔNG ghi record. Có xóa file tạm.</summary>
        public static void Drop(string name)
        {
            var list = Load();
            var entry = list.items.Find(p => p.name == name);
            if (entry == null)
                return;

            TryDelete(entry.tempPath);
            list.items.RemoveAll(p => p.name == name);
            Save(list);
        }

        #endregion

        #region Event Handlers

        // Hub chỉ chạy 1 import tại một thời điểm (window khóa _busy; batch chạy tuần tự) nên
        // terminal-event đầu tiên sau khi có pending CHÍNH là import của ta -> finalize pending cũ nhất.
        private static void OnImportCompleted(string packageName) => CompleteOldest();

        private static void OnImportFailed(string packageName, string errorMessage) => DropOldest();

        private static void OnImportCancelled(string packageName) => DropOldest();

        #endregion

        #region Private Methods

        /// <summary>Sau domain reload: asset đã ghi ra đĩa nên pending còn sót coi như đã cài.</summary>
        private static void RecoverAfterReload()
        {
            var list = Load();
            if (list.items.Count == 0)
                return;

            foreach (var pending in list.items)
                ApplyInstalled(pending);

            list.items.Clear();
            Save(list);
        }

        private static void CompleteOldest()
        {
            var list = Load();
            if (list.items.Count == 0)
                return;

            ApplyInstalled(list.items[0]);
            list.items.RemoveAt(0);
            Save(list);
        }

        private static void DropOldest()
        {
            var list = Load();
            if (list.items.Count == 0)
                return;

            TryDelete(list.items[0].tempPath);
            list.items.RemoveAt(0);
            Save(list);
        }

        /// <summary>Ghi record "đã cài" + xóa file tạm. Idempotent (MarkInstalled upsert theo name).</summary>
        private static void ApplyInstalled(PendingInstall pending)
        {
            var asset = new CatalogAsset
            {
                name = pending.name,
                fileName = pending.fileName,
                sha256 = pending.sha256,
            };
            FeatureHubInstallRecord.MarkInstalled(asset, pending.sha256);
            TryDelete(pending.tempPath);
        }

        private static PendingInstallList Load()
        {
            string json = SessionState.GetString(PENDING_KEY, string.Empty);
            if (string.IsNullOrEmpty(json))
                return new PendingInstallList();

            try
            {
                return JsonConvert.DeserializeObject<PendingInstallList>(json) ?? new PendingInstallList();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FeatureHub] Đọc pending-install lỗi, reset. {e.Message}");
                return new PendingInstallList();
            }
        }

        private static void Save(PendingInstallList list)
        {
            SessionState.SetString(PENDING_KEY, JsonConvert.SerializeObject(list));
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FeatureHub] Không xóa được file tạm '{path}': {e.Message}");
            }
        }

        #endregion
    }
}
