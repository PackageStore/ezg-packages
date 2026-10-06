#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Ezg.Editor.Shared.EzgKit;
using UnityEditor;
using UnityEditor.Build;

namespace Ezg.EzgKit
{
    /// <summary>
    ///     API công khai của EzgKit cho automation — skill <c>/setup-project</c> của Claude gọi qua Unity MCP
    ///     (<c>unity_execute_code</c>, ví dụ <c>return Ezg.EzgKit.EzgKitApi.GetStatusJson();</c>). Mọi hàm trả về CHUỖI
    ///     JSON, không mở dialog, không chờ input; dùng đúng các hàm Detect / Apply của trang mà cửa sổ dùng nên kết
    ///     quả giống hệt bấm nút trên UI.
    ///     <para>
    ///         Id trang ổn định: <c>overview, project, marketing, ads, iap, artstyle, localize, firebase, publisher,
    ///         social</c>. Ghi được (<see cref="Apply" />): <c>project, marketing, ads, localize, artstyle, social</c>.
    ///     </para>
    /// </summary>
    public static class EzgKitApi
    {
        /// <summary>Phiên bản contract của API (đổi khi JSON thay đổi không tương thích).</summary>
        public const int API_VERSION = 1;

        #region Read

        /// <summary>
        ///     Trạng thái mọi trang:
        ///     <c>{ api, project, androidId, iosId, version, progress:{done,total},
        ///     pages:[{id,title,group,status,marker,summary,todos:[{level,text,fix}]}], requests:[…] }</c>.
        ///     <c>status</c>: todo | partial | done | deferred | na | error | info.
        /// </summary>
        public static string GetStatusJson()
        {
            return Guard(() =>
            {
                var pages = SetupPages.Create();
                var list = new List<object>();
                var done = 0;
                var total = 0;
                foreach (var page in pages)
                {
                    var report = page.DetectResolved();
                    if (page.CountsInProgress)
                    {
                        total++;
                        if (report.State is SetupState.Done or SetupState.NotApplicable) done++;
                    }

                    var todos = new List<object>();
                    foreach (var todo in report.Todos) todos.Add(todo.ToJson());
                    list.Add(new JsonObject()
                        .Set("id", page.Id)
                        .Set("title", page.Title)
                        .Set("group", page.Group)
                        .Set("canApply", page.CanApply)
                        .Set("status", SetupStateText.Id(report.State))
                        .Set("marker", EzgKitState.GetMarker(page.Id) ?? string.Empty)
                        .Set("summary", report.Summary ?? string.Empty)
                        .Set("todos", todos));
                }

                var requests = new List<object>();
                foreach (var request in EzgKitState.Requests()) requests.Add(request);

                return new JsonObject()
                    .Set("api", API_VERSION)
                    .Set("project", ProjectProfileFile.Get("projectName") ?? PlayerSettings.productName)
                    .Set("productName", PlayerSettings.productName)
                    .Set("androidId", PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android) ?? string.Empty)
                    .Set("iosId", PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.iOS) ?? string.Empty)
                    .Set("version", PlayerSettings.bundleVersion)
                    .Set("progress", new JsonObject().Set("done", done).Set("total", total))
                    .Set("pages", list)
                    .Set("requests", requests);
            });
        }

        /// <summary>Giá trị hiện tại của một trang (secret che bằng •; gửi lại giá trị có • qua <see cref="Apply" /> = không đổi).</summary>
        public static string GetPageValuesJson(string pageId)
        {
            return Guard(() =>
            {
                var page = Find(pageId);
                return new JsonObject()
                    .Set("page", page.Id)
                    .Set("canApply", page.CanApply)
                    .Set("values", page.GetValues(true));
            });
        }

        #endregion

        #region Write

        /// <summary>
        ///     Ghi <paramref name="valuesJson" /> (object JSON — chỉ key có mặt mới được đụng) vào trang
        ///     <paramref name="pageId" />. <paramref name="dryRun" /> = true chỉ đối chiếu, trả về bảng thay đổi.
        ///     Kết quả: <c>{page, ok, dryRun, error, changed, rows:[{sink,field,old,new,changed}], notes:[…]}</c>.
        ///     Ghi thật thành công thì trang được đánh dấu <c>done</c>.
        /// </summary>
        public static string Apply(string pageId, string valuesJson, bool dryRun = true)
        {
            return Guard(() =>
            {
                var page = Find(pageId);
                if (!page.CanApply) throw new InvalidOperationException($"Trang '{page.Id}' chỉ đọc — không có Apply.");
                var values = string.IsNullOrWhiteSpace(valuesJson) ? new JsonObject() : MiniJson.Parse(valuesJson, out var error) as JsonObject;
                if (values == null) throw new ArgumentException("valuesJson không phải object JSON hợp lệ.");

                var result = page.Apply(values, dryRun);
                result.DryRun = dryRun;
                if (!dryRun)
                {
                    if (result.Ok) EzgKitState.SetMarker(page.Id, EzgKitState.MARKER_DONE);
                    AssetDatabase.SaveAssets();
                    RepaintWindow();
                }

                return result.ToJson(page.Id);
            });
        }

        /// <summary>Đặt quyết định của người cho trang: <c>done | deferred | na | clear</c>.</summary>
        public static string SetMarker(string pageId, string marker)
        {
            return Guard(() =>
            {
                var page = Find(pageId);
                EzgKitState.SetMarker(page.Id, marker);
                RepaintWindow();
                return new JsonObject().Set("page", page.Id).Set("marker", EzgKitState.GetMarker(page.Id) ?? string.Empty);
            });
        }

        /// <summary>Gỡ một yêu cầu "nhờ Claude" (ví dụ <c>artstyle</c>) sau khi đã xử lý xong.</summary>
        public static string ClearRequest(string id)
        {
            return Guard(() =>
            {
                EzgKitState.ClearRequest(id);
                RepaintWindow();
                var requests = new List<object>();
                foreach (var request in EzgKitState.Requests()) requests.Add(request);
                return new JsonObject().Set("requests", requests);
            });
        }

        #endregion

        #region Window

        /// <summary>Mở / focus cửa sổ EzgKit ở trang <paramref name="pageId" /> (null = trang đang mở).</summary>
        public static void Open(string pageId = null) => EzgKitWindow.Open(pageId);

        /// <summary>
        ///     Mở cửa sổ và bắt đầu luồng "Setup tất cả": nhảy tới trang Setup đầu tiên còn việc (todo / partial /
        ///     error), mỗi trang có nút "Tiếp →". Cửa sổ dựng UI ở frame kế tiếp nên luồng bắt đầu qua delayCall.
        /// </summary>
        public static void SetupAll()
        {
            var window = EzgKitWindow.Open(null);
            EditorApplication.delayCall += () =>
            {
                if (window != null) window.RunSetupAll();
            };
        }

        #endregion

        #region Helpers

        private static SetupPage Find(string pageId)
        {
            var page = SetupPages.Find(SetupPages.Create(), (pageId ?? string.Empty).Trim().ToLowerInvariant());
            if (page == null) throw new ArgumentException($"Không có trang '{pageId}'. Id hợp lệ: {string.Join(", ", Ids())}.");
            return page;
        }

        private static IEnumerable<string> Ids()
        {
            foreach (var page in SetupPages.Create()) yield return page.Id;
        }

        private static void RepaintWindow()
        {
            if (!EditorWindow.HasOpenInstances<EzgKitWindow>()) return;
            var window = EditorWindow.GetWindow<EzgKitWindow>(false, "EzgKit", false);
            window.RefreshAll();
            window.Rebuild();
        }

        private static string Guard(Func<JsonObject> build)
        {
            try
            {
                return MiniJson.Serialize(build(), false);
            }
            catch (Exception exception)
            {
                return MiniJson.Serialize(new JsonObject().Set("ok", false).Set("error", exception.Message), false);
            }
        }

        #endregion
    }
}
#endif
