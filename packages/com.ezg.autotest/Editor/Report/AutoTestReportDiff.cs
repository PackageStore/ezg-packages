using System;
using System.Collections.Generic;
using System.Linq;
using F = Ezg.AutoTest.Editor.AutoTestReportFormat;

namespace Ezg.AutoTest.Editor
{
    /// <summary>
    ///     So sánh lượt chạy hiện tại với lượt trước theo fingerprint issue (<see cref="TestIssue.id" />): đánh dấu lỗi
    ///     mới, đếm lỗi đã sửa / còn tồn, case mới lỗi / hết lỗi — điền vào <see cref="TestRunReport.diff" />.
    ///     <para>
    ///         Phạm vi so sánh (tránh báo "đã sửa"/"mới" sai):
    ///         <list type="bullet">
    ///             <item>Chỉ suite có mặt ở CẢ HAI lượt — suite lần này không chạy thì issue cũ không tính là đã sửa.</item>
    ///             <item>
    ///                 Trong suite chung, case bị Bỏ qua / Đã dừng / Chờ ở một trong hai lượt coi như chưa được kiểm
    ///                 tra: issue cũ của nó không tính là đã sửa, issue hiện tại không tính là mới. Case chưa từng có ở
    ///                 lượt trước (case mới thêm) thì issue của nó là mới.
    ///             </item>
    ///         </list>
    ///     </para>
    /// </summary>
    public static class AutoTestReportDiff
    {
        /// <summary>Số issue đã sửa tối đa lưu chi tiết trong report (số đếm vẫn đủ).</summary>
        const int MAX_FIXED_ISSUE_LIST = 200;

        const char KEY_SEPARATOR = '|';

        /// <summary>
        ///     Đặt lại <c>isNew</c> của mọi issue hiện tại rồi điền <c>current.diff</c>. <paramref name="previous" /> null
        ///     ⇒ diff rỗng (không có mốc so sánh nên không issue nào là "mới").
        /// </summary>
        public static void Apply(TestRunReport current, TestRunReport previous)
        {
            if (current == null) return;
            foreach (var c in F.AllCases(current))
            foreach (var issue in F.Issues(c))
                issue.isNew = false;

            var diff = new RunDiff();
            current.diff = diff;
            if (previous == null) return;

            diff.previousRunId = F.Safe(previous.runId);
            diff.previousStartedAt = F.Safe(previous.startedAt);

            var previousSuites = new Dictionary<string, TestSuiteResult>(StringComparer.Ordinal);
            foreach (var suite in F.Suites(previous))
                if (!string.IsNullOrEmpty(suite.suiteId) && !previousSuites.ContainsKey(suite.suiteId))
                    previousSuites.Add(suite.suiteId, suite);

            var fixedSeen = new HashSet<string>(StringComparer.Ordinal);
            var fixedList = new List<TestIssue>();
            foreach (var curSuite in F.Suites(current))
            {
                if (string.IsNullOrEmpty(curSuite.suiteId) ||
                    !previousSuites.TryGetValue(curSuite.suiteId, out var prevSuite))
                    continue;
                CompareSuite(diff, curSuite, prevSuite, fixedSeen, fixedList);
            }

            diff.fixedIssues = fixedSeen.Count;
            diff.fixedIssueList = fixedList.OrderByDescending(i => (int)i.severity).Take(MAX_FIXED_ISSUE_LIST).ToList();
        }

        #region Private

        static void CompareSuite(RunDiff diff, TestSuiteResult curSuite, TestSuiteResult prevSuite,
            HashSet<string> fixedSeen, List<TestIssue> fixedList)
        {
            var suiteId = curSuite.suiteId;
            var prevCases = IndexCases(prevSuite);
            var curCases = IndexCases(curSuite);

            // Issue của các case lượt trước đã thực sự chạy.
            var prevIssueKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var prevCase in prevCases.Values)
                if (Ran(prevCase.status))
                    foreach (var issue in F.Issues(prevCase))
                        prevIssueKeys.Add(IssueKey(suiteId, prevCase, issue));

            var curIssueKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var curCase in F.Cases(curSuite))
            {
                var hasPrev = prevCases.TryGetValue(CaseKey(curCase), out var prevCase);
                var comparable = Ran(curCase.status) && (!hasPrev || Ran(prevCase.status));
                foreach (var issue in F.Issues(curCase))
                {
                    var key = IssueKey(suiteId, curCase, issue);
                    curIssueKeys.Add(key);
                    if (!comparable) continue;
                    if (prevIssueKeys.Contains(key))
                    {
                        diff.persistingIssues++;
                    }
                    else
                    {
                        issue.isNew = true;
                        diff.newIssues++;
                    }
                }

                if (!Ran(curCase.status)) continue;
                var label = F.CaseLabel(curSuite, curCase);
                var curBad = IsBad(curCase.status);
                if (!hasPrev)
                {
                    // Case mới thêm mà đã lỗi ⇒ tính là mới lỗi.
                    if (curBad) diff.newlyFailingCases.Add(label);
                    continue;
                }

                if (!Ran(prevCase.status)) continue;
                var prevBad = IsBad(prevCase.status);
                if (curBad && !prevBad) diff.newlyFailingCases.Add(label);
                else if (prevBad && !curBad) diff.fixedCases.Add(label);
            }

            // Issue cũ biến mất ở case lần này có chạy thật ⇒ đã sửa.
            foreach (var prevCase in prevCases.Values)
            {
                if (!Ran(prevCase.status)) continue;
                if (!curCases.TryGetValue(CaseKey(prevCase), out var curCase) || !Ran(curCase.status)) continue;
                foreach (var issue in F.Issues(prevCase))
                {
                    var key = IssueKey(suiteId, prevCase, issue);
                    if (curIssueKeys.Contains(key) || !fixedSeen.Add(key)) continue;
                    fixedList.Add(issue);
                }
            }
        }

        static Dictionary<string, TestCaseResult> IndexCases(TestSuiteResult suite)
        {
            var map = new Dictionary<string, TestCaseResult>(StringComparer.Ordinal);
            foreach (var c in F.Cases(suite))
            {
                var key = CaseKey(c);
                if (!map.ContainsKey(key)) map.Add(key, c);
            }

            return map;
        }

        static string CaseKey(TestCaseResult c)
        {
            return !string.IsNullOrEmpty(c.caseId) ? c.caseId : F.Safe(c.name);
        }

        /// <summary>Fingerprint nếu có; thiếu (report cũ) thì ghép các field ổn định.</summary>
        static string IssueKey(string suiteId, TestCaseResult c, TestIssue issue)
        {
            if (!string.IsNullOrEmpty(issue.id)) return issue.id;
            return string.Join(KEY_SEPARATOR.ToString(), "raw", suiteId, CaseKey(c), F.Safe(issue.category),
                F.Safe(issue.title), F.Safe(issue.location), F.Safe(issue.objectPath));
        }

        /// <summary>Case đã thực sự được kiểm tra (có kết quả để so sánh).</summary>
        static bool Ran(TestStatus s)
        {
            return s == TestStatus.Passed || s == TestStatus.Warning || s == TestStatus.Failed ||
                   s == TestStatus.Error || s == TestStatus.Running;
        }

        static bool IsBad(TestStatus s)
        {
            return AutoTestStatusUtil.IsBad(s) || s == TestStatus.Running;
        }

        #endregion
    }
}
