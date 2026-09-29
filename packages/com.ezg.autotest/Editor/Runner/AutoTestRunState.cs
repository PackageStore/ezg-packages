using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Ezg.AutoTest.Editor
{
    /// <summary>Tuỳ chọn một lượt chạy.</summary>
    [Serializable]
    public class AutoTestRunOptions
    {
        /// <summary>Tiêu đề report (rỗng = tự đặt theo danh sách suite).</summary>
        public string Title;

        /// <summary>Editor / CLI.</summary>
        public string Trigger = "Editor";

        /// <summary>Chỉ chạy kịch bản có tên class / tên hiển thị chứa chuỗi này (suite "scenarios").</summary>
        public string ScenarioFilter;

        /// <summary>Chỉ chạy các case "suiteId/caseId" này (rỗng = tất cả).</summary>
        public List<string> CaseFilter = new();

        /// <summary>Thư mục report tuyệt đối (rỗng = AutoTestReports/&lt;runId&gt;).</summary>
        public string OutputDir;

        /// <summary>Mở report.html khi xong (còn phụ thuộc settings openReportWhenFinished; batchmode không mở).</summary>
        public bool OpenReportWhenFinished = true;

        /// <summary>CLI: thoát Editor với exit code khi xong.</summary>
        public bool ExitEditorWhenDone;

        /// <summary>CLI: case lỗi có issue từ mức này trở lên ⇒ exit code 2.</summary>
        public Severity FailOn = Severity.Major;

        /// <summary>CLI: không fail khi chỉ có Warning.</summary>
        public bool NoFail;

        /// <summary>Giới hạn thời gian cả lượt (phút, 0 = không giới hạn).</summary>
        public float GlobalTimeoutMinutes;
    }

    /// <summary>
    ///     Trạng thái lượt chạy — ghi ra đĩa sau mỗi case để sống sót qua domain reload (vào/ra Play mode,
    ///     recompile) và cả Editor crash.
    /// </summary>
    [Serializable]
    internal class AutoTestRunState
    {
        public const string PHASE_IDLE = "Idle";
        public const string PHASE_EDIT = "Edit";
        public const string PHASE_ENTERING_PLAY = "EnteringPlay";
        public const string PHASE_PLAYING = "Playing";
        public const string PHASE_EXITING_PLAY = "ExitingPlay";
        public const string PHASE_FINALIZING = "Finalizing";
        public const string PHASE_DONE = "Done";

        const string FILE = "Library/EZGAutoTest/run-state.json";

        public string runId;
        public string phase = PHASE_IDLE;
        public List<string> suiteIds = new();
        public int suiteIndex;

        /// <summary>Id suite Play chạy trong phiên Play hiện tại (1 nếu cô lập, nhiều nếu gộp).</summary>
        public List<string> playGroup = new();

        public bool cancelRequested;
        public string cancelReason;
        public string startedAtIso;
        public string phaseChangedAtIso;
        public string previousPlayModeStartScene;
        public bool playModeStartSceneOverridden;
        public bool sandboxTaken;
        public int playAttempts;
        public string runnerError;
        public AutoTestRunOptions options = new();
        public AutoTestConfig config = new();
        public TestRunReport report = new();

        public bool IsActive => phase != PHASE_IDLE && phase != PHASE_DONE;

        public static string AbsolutePath()
        {
            return Path.Combine(Path.GetDirectoryName(Application.dataPath)!, FILE);
        }

        public static AutoTestRunState Load()
        {
            var path = AbsolutePath();
            if (!File.Exists(path)) return null;
            try
            {
                return JsonUtility.FromJson<AutoTestRunState>(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                Debug.LogWarning(AutoTestLog.PREFIX + "run-state hỏng, bỏ qua: " + e.Message);
                return null;
            }
        }

        public void Save()
        {
            var path = AbsolutePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonUtility.ToJson(this));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        public static void Delete()
        {
            var path = AbsolutePath();
            if (File.Exists(path)) File.Delete(path);
        }

        public void SetPhase(string newPhase)
        {
            phase = newPhase;
            phaseChangedAtIso = DateTime.Now.ToString("o");
            Save();
        }

        public double SecondsInPhase()
        {
            return DateTime.TryParse(phaseChangedAtIso, null, System.Globalization.DateTimeStyles.RoundtripKind,
                out var t)
                ? (DateTime.Now - t).TotalSeconds
                : 0;
        }

        public TestSuiteResult SuiteResult(string suiteId)
        {
            return report.suites.Find(s => s.suiteId == suiteId);
        }
    }
}
