using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Ezg.AutoTest.Editor
{
    /// <summary>
    ///     Import setting audio cho mobile: file quá nặng, nhạc dài bị giải nén toàn bộ khi load (tốn RAM), SFX ngắn
    ///     dùng Streaming (tốn I/O), nhạc dài bật Preload Audio Data (chậm load scene).
    /// </summary>
    public sealed class AudioRule : IStaticRule
    {
        const string ISSUE_CATEGORY = "Asset.Audio";
        const float LONG_CLIP_SECONDS = 10f;
        const float SHORT_SFX_SECONDS = 2f;
        const int PCM_BYTES_PER_SAMPLE = 2;

        public string Id => "audio";
        public string Name => "Audio nặng / sai Load Type";

        public string Description =>
            "File audio quá nặng, nhạc dài đặt Decompress On Load (giải nén cả bài vào RAM), SFX ngắn dùng Streaming, " +
            "nhạc dài bật Preload Audio Data.";

        public string Category => "Asset";
        public int Order => 45;

        public async Task Run(AutoTestContext ctx)
        {
            var cfg = ctx.Config.staticCheck;
            var sink = new AssetIssueSink(ctx, ISSUE_CATEGORY);
            var platforms = new[]
            {
                new KeyValuePair<string, string>("Android", NamedBuildTarget.Android.TargetName),
                new KeyValuePair<string, string>("iOS", NamedBuildTarget.iOS.TargetName)
            };
            var paths = StaticCheckUtil.FindAssets("t:AudioClip", cfg);
            var scanned = 0;
            var totalBytes = 0L;
            for (var i = 0; i < paths.Count; i++)
            {
                await StaticCheckUtil.Yield(ctx, i, paths.Count, "Quét audio");
                var path = paths[i];
                try
                {
                    if (!(AssetImporter.GetAtPath(path) is AudioImporter imp)) continue;
                    scanned++;
                    totalBytes += CheckClip(path, imp, platforms, cfg, sink);
                }
                catch (Exception e)
                {
                    sink.ReportUnreadable(path, e);
                }
            }

            ctx.Metric("Audio đã quét", scanned);
            ctx.Metric("Tổng dung lượng audio", AssetRuleUtil.ToMb(totalBytes), "MB");
            sink.Flush();
            EditorUtility.UnloadUnusedAssetsImmediate();
        }

        /// <summary>Kiểm tra một clip, trả kích thước file (byte).</summary>
        static long CheckClip(string path, AudioImporter imp, KeyValuePair<string, string>[] platforms,
            StaticCheckConfig cfg, AssetIssueSink sink)
        {
            var file = new FileInfo(StaticCheckUtil.AbsolutePath(path));
            var bytes = file.Exists ? file.Length : 0L;
            var mb = AssetRuleUtil.ToMb(bytes);
            if (mb > cfg.maxAudioFileMb)
                sink.Report(Severity.Minor, "File audio quá nặng",
                    $"File {Fmt(mb)} MB. Cắt bớt, giảm sample rate hoặc nén lại (Vorbis/MP3) trước khi đưa vào project.",
                    path, null, $"≤ {Fmt(cfg.maxAudioFileMb)} MB", $"{Fmt(mb)} MB");

            // Chỉ đọc header (length/frequency/channels) — không cần dữ liệu PCM.
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null) return bytes;
            var length = clip.length;

            var decompress = new List<string>();
            var streaming = new List<string>();
            var preload = false;
            var def = imp.defaultSampleSettings;
            foreach (var p in platforms)
            {
                var s = imp.ContainsSampleSettingsOverride(p.Value) ? imp.GetOverrideSampleSettings(p.Value) : def;
                if (s.loadType == AudioClipLoadType.DecompressOnLoad) decompress.Add(p.Key);
                if (s.loadType == AudioClipLoadType.Streaming) streaming.Add(p.Key);
                if (s.preloadAudioData) preload = true;
            }

            if (length > LONG_CLIP_SECONDS && decompress.Count > 0)
            {
                var pcmMb = AssetRuleUtil.ToMb((long)(length * clip.frequency * clip.channels * PCM_BYTES_PER_SAMPLE));
                sink.Report(Severity.Major, "Nhạc dài giải nén khi load (tốn RAM)",
                    $"Clip dài {Fmt(length)}s đặt Load Type = Decompress On Load trên {string.Join(", ", decompress)} → " +
                    $"giải nén cả bài vào RAM (~{Fmt(pcmMb)} MB PCM). Nhạc nền nên dùng Streaming, clip vừa dùng " +
                    "Compressed In Memory.", path, null, "Streaming / Compressed In Memory", "Decompress On Load");
            }

            if (length < SHORT_SFX_SECONDS && streaming.Count > 0)
                sink.Report(Severity.Minor, "SFX ngắn dùng Streaming",
                    $"Clip chỉ {Fmt(length)}s nhưng đặt Streaming trên {string.Join(", ", streaming)} → mỗi lần phát mở " +
                    "luồng đọc đĩa, dễ trễ tiếng. SFX ngắn nên dùng Decompress On Load.", path, null,
                    "Decompress On Load", "Streaming");

            if (length > LONG_CLIP_SECONDS && preload)
                sink.Report(Severity.Info, "Nhạc dài bật Preload Audio Data",
                    $"Clip dài {Fmt(length)}s bật Preload Audio Data → dữ liệu được nạp ngay khi scene/prefab tham chiếu " +
                    "nó load, làm chậm load. Cân nhắc tắt và để phát lúc cần.", path, null, "Preload tắt", "Preload bật");

            return bytes;
        }

        static string Fmt(double v)
        {
            return v.ToString("0.#", CultureInfo.InvariantCulture);
        }
    }
}
