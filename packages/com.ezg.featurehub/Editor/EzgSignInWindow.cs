// EZG Feature Hub — đăng nhập Google ngay trong Unity.
//
// Cùng luồng ghép cặp thiết bị mà builder dùng, nhưng chạy trong Editor: xin mã, mở trình duyệt, poll
// cho tới khi người dùng đăng nhập xong. Nhờ vậy phiên hết hạn giữa buổi làm không buộc phải bỏ Unity
// ra chạy lại build_unity_template.
using System;
using UnityEditor;
using UnityEngine;

namespace Ezg.FeatureHub.Editor
{
    public class EzgSignInWindow : EditorWindow
    {
        #region Fields

        private const double PollIntervalSeconds = 3;
        private const string MenuPath = "Ezg/Đăng nhập EZG";

        private string _userCode;
        private string _verifyUrl;
        private string _deviceCode;
        private string _status = "Đang lấy mã xác thực...";
        private bool _failed;
        private bool _done;
        private double _nextPollAt;
        private double _expiresAt;
        private bool _requestInFlight;

        #endregion

        #region Public Methods

        [MenuItem(MenuPath, priority = 100)]
        public static void Open()
        {
            var window = GetWindow<EzgSignInWindow>(true, "Đăng nhập EZG", true);
            window.minSize = new Vector2(420, 260);
            window.Restart();
            window.Show();
        }

        /// <summary>Hiện trạng thái phiên trong Console — tiện khi cần kiểm tra nhanh.</summary>
        [MenuItem("Ezg/Trạng thái phiên EZG", priority = 101)]
        public static void LogSessionStatus()
        {
            if (!EzgAuth.IsSignedIn)
            {
                Debug.LogWarning("[EZG] Máy này chưa đăng nhập (hoặc phiên đã hết hạn).");
                return;
            }

            var left = TimeSpan.FromSeconds(EzgAuth.SecondsLeft);
            Debug.Log($"[EZG] Đang đăng nhập: {EzgAuth.Email} — còn {left.Hours}h{left.Minutes:00}m.");
        }

        #endregion

        #region Unity Methods

        private void OnEnable() => EditorApplication.update += Tick;

        private void OnDisable() => EditorApplication.update -= Tick;

        private void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Đăng nhập bằng tài khoản Google công ty", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Chỉ tài khoản @easygoing.vn mới truy cập được package và template của EZG.",
                EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(10);

            if (!string.IsNullOrEmpty(_userCode))
            {
                EditorGUILayout.LabelField("Mã xác thực", EditorStyles.miniBoldLabel);
                var style = new GUIStyle(EditorStyles.textField)
                {
                    fontSize = 22,
                    alignment = TextAnchor.MiddleCenter,
                    fixedHeight = 40,
                };
                EditorGUILayout.SelectableLabel(_userCode, style, GUILayout.Height(40));
                EditorGUILayout.LabelField("Kiểm tra mã trên khớp với mã hiện trong trình duyệt trước khi bấm đăng nhập.",
                    EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.Space(6);

                using (new EditorGUI.DisabledScope(_done))
                {
                    if (GUILayout.Button("Mở trình duyệt để đăng nhập", GUILayout.Height(30)))
                        Application.OpenURL(_verifyUrl);
                }
            }

            EditorGUILayout.Space(10);
            EditorGUILayout.HelpBox(_status, _failed ? MessageType.Error : _done ? MessageType.Info : MessageType.None);

            if (_failed && GUILayout.Button("Thử lại"))
                Restart();

            if (_done && GUILayout.Button("Đóng"))
                Close();
        }

        #endregion

        #region Private Methods

        private void Restart()
        {
            _userCode = null;
            _deviceCode = null;
            _verifyUrl = null;
            _failed = false;
            _done = false;
            _requestInFlight = true;
            _status = "Đang lấy mã xác thực...";

            string body = $"{{\"hostname\":\"{JsonEscape(SystemInfo.deviceName)}\",\"client\":\"unity-editor\"}}";
            EditorDownloader.PostJson($"{EzgAuth.GatewayUrl}/auth/device/start", body, null, (ok, response, code) =>
            {
                _requestInFlight = false;
                if (!ok || string.IsNullOrEmpty(response))
                {
                    Fail($"Không kết nối được máy chủ xác thực (HTTP {code}).");
                    return;
                }

                var start = JsonUtility.FromJson<DeviceStart>(response);
                if (start == null || string.IsNullOrEmpty(start.device_code))
                {
                    Fail("Máy chủ trả về dữ liệu không hợp lệ.");
                    return;
                }

                _deviceCode = start.device_code;
                _userCode = start.user_code;
                _verifyUrl = start.verification_uri_complete;
                _expiresAt = EditorApplication.timeSinceStartup + Math.Max(60, start.expires_in);
                _nextPollAt = EditorApplication.timeSinceStartup + PollIntervalSeconds;
                _status = "Mở trình duyệt và đăng nhập, cửa sổ này sẽ tự nhận kết quả.";

                Application.OpenURL(_verifyUrl);
                Repaint();
            });
        }

        private void Tick()
        {
            if (_done || _failed || _requestInFlight || string.IsNullOrEmpty(_deviceCode)) return;
            if (EditorApplication.timeSinceStartup < _nextPollAt) return;

            if (EditorApplication.timeSinceStartup > _expiresAt)
            {
                Fail("Mã xác thực đã hết hạn. Bấm \"Thử lại\" để lấy mã mới.");
                return;
            }

            _nextPollAt = EditorApplication.timeSinceStartup + PollIntervalSeconds;
            _requestInFlight = true;

            string body = $"{{\"device_code\":\"{JsonEscape(_deviceCode)}\"}}";
            EditorDownloader.PostJson($"{EzgAuth.GatewayUrl}/auth/device/poll", body, null, (ok, response, code) =>
            {
                _requestInFlight = false;

                // 202 = chưa duyệt, 429 = poll quá nhanh: cả hai đều là "chờ tiếp", không phải lỗi.
                if (code == 202 || code == 429) return;

                if (code == 403)
                {
                    var denied = SafeParse(response);
                    Fail(denied?.message ?? "Tài khoản không được phép.");
                    return;
                }

                if (!ok || string.IsNullOrEmpty(response))
                {
                    Fail($"Đăng nhập thất bại (HTTP {code}).");
                    return;
                }

                EzgAuth.SaveSession(response);
                var session = SafeParse(response);
                _done = true;
                _status = $"Đã đăng nhập: {session?.email}. Token đã ghi vào ~/.upmconfig.toml — bấm Refresh trong "
                          + "Package Manager nếu nó đang báo lỗi.";
                Debug.Log($"[EZG] Đăng nhập thành công: {session?.email}");
                Repaint();
            });
        }

        private void Fail(string message)
        {
            _failed = true;
            _status = message;
            Repaint();
        }

        // JsonUtility chỉ serialize được object có [Serializable], không escape nổi một string trần --
        // mà hostname là do máy đặt tên nên hoàn toàn có thể chứa dấu nháy hoặc backslash.
        private static string JsonEscape(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            var builder = new System.Text.StringBuilder(value.Length + 8);
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (c < 0x20) builder.Append("\\u").Append(((int)c).ToString("x4"));
                        else builder.Append(c);
                        break;
                }
            }
            return builder.ToString();
        }

        private static PollResult SafeParse(string json)
        {
            try { return JsonUtility.FromJson<PollResult>(json); }
            catch { return null; }
        }

        #endregion

        #region Nested Types

        [Serializable]
        private class DeviceStart
        {
            public string device_code;
            public string user_code;
            public string verification_uri_complete;
            public int expires_in;
        }

        [Serializable]
        private class PollResult
        {
            public string access_token;
            public string email;
            public string message;
        }

        #endregion
    }
}
