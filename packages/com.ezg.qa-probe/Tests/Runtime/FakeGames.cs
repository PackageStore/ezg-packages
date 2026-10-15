using System;

// Các "GameSystems" giả để thử phần tự dò config (ProbeAutoConfig) và bật/tắt cheat (ProbeGameCheat).
namespace Ezg.QaProbe.Tests.Template
{
    /// <summary>Như template EZG mới: cheat qua com.ezg.cheat-gate.</summary>
    public static class GameSystems
    {
        public static bool isCheat;
        public static int TryCalls;
        public static bool Grant = true;

        public static void TryEnableCheat(Action<bool> done = null)
        {
            TryCalls++;
            isCheat = Grant;
            done?.Invoke(Grant);
        }

        public static void DisableCheat(bool emitEvent = true)
        {
            isCheat = false;
        }
    }
}

namespace Ezg.QaProbe.Tests.RemoteConfig
{
    /// <summary>Như game đời cũ: TryEnableCheat() hỏi Remote Config và có thể TẮT cheat — không được dùng làm cheatOn.</summary>
    public static class GameSystems
    {
        public static bool isCheat;

        public static bool TryEnableCheat()
        {
            isCheat = false;
            return false;
        }

        public static void DisableCheat(bool emitEvent = true)
        {
            isCheat = false;
        }
    }
}

namespace Other.Game
{
    /// <summary>Cùng tên, ngoài namespace Ezg.* — chỉ được chọn khi không có bản Ezg.</summary>
    public static class GameSystems
    {
        public static bool isCheat;
    }
}

namespace Ezg.QaProbe.Tests.WrongFlag
{
    /// <summary>isCheat không phải bool → bỏ qua.</summary>
    public static class GameSystems
    {
        public static int isCheat;
    }
}

namespace Ezg.QaProbe.Tests.BugLogger
{
    /// <summary>Thay <c>Ezg.BugLogger.UI.BugLoggerUI</c>: ghi lại các lần probe ẩn/hiện UI Bug Logger quanh lúc chụp.</summary>
    public static class FakeBugLoggerUI
    {
        public static readonly System.Collections.Generic.List<float> Calls = new System.Collections.Generic.List<float>();

        public static void HideForCapture(float seconds)
        {
            Calls.Add(seconds);
        }
    }
}
