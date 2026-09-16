using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Ezg.UserSegment.Tests
{
    /// <summary>
    ///     Chạy EditMode test của assembly này từ code (MCP / CI) và ghi kết quả ra file text. Dùng:
    ///     <c>TestRunnerCli.Run("/tmp/seg_tests.txt")</c>; file xuất hiện khi run xong.
    /// </summary>
    public static class TestRunnerCli
    {
        public const string ASSEMBLY = "Ezg.Package.UserSegment.Tests";

        private sealed class Callbacks : ICallbacks
        {
            private readonly string _path;
            private readonly StringBuilder _sb = new StringBuilder();
            private int _passed, _failed, _skipped;

            public Callbacks(string path)
            {
                _path = path;
            }

            public void RunStarted(ITestAdaptor testsToRun)
            {
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                _sb.Insert(0, $"RESULT passed={_passed} failed={_failed} skipped={_skipped}\n");
                File.WriteAllText(_path, _sb.ToString());
                Debug.Log("[UserSegment.Tests] " + _sb.ToString().Split('\n')[0]);
            }

            public void TestStarted(ITestAdaptor test)
            {
            }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (result.Test.IsSuite) return;
                switch (result.TestStatus)
                {
                    case TestStatus.Passed: _passed++; break;
                    case TestStatus.Failed: _failed++; break;
                    default: _skipped++; break;
                }

                _sb.Append(result.TestStatus).Append("  ").Append(result.Test.FullName).Append('\n');
                if (result.TestStatus == TestStatus.Failed)
                    _sb.Append("    ").Append((result.Message ?? string.Empty).Replace("\n", "\n    ")).Append('\n');
            }
        }

        public static void Run(string outputPath)
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new Callbacks(outputPath));
            api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode, assemblyNames = new[] { ASSEMBLY } }));
        }
    }
}
