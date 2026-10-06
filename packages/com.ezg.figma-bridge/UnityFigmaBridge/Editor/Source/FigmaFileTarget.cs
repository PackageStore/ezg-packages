using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityFigmaBridge.Editor.Bridge;
using UnityFigmaBridge.Editor.Settings;

namespace UnityFigmaBridge.Editor.Source
{
    public readonly struct FigmaFileTarget
    {
        public readonly string Key;
        public readonly string Name;

        public FigmaFileTarget(string key, string name)
        {
            Key = key;
            Name = name;
        }
    }

    public sealed class FigmaFileTargetException : Exception
    {
        public FigmaFileTargetException(string userMessage, Exception inner = null) : base(userMessage, inner) { }
    }

    public static class FigmaFileTargets
    {
        public static bool TryPick(UnityFigmaBridgeSettings settings, IReadOnlyList<HubFile> files,
            out HubFile file, out string error, Func<HubFile, bool> isUsable = null)
        {
            var candidates = Importable(files, isUsable);
            file = null;
            error = null;

            var boundKey = settings.BridgeFileKey;
            if (!string.IsNullOrEmpty(boundKey))
            {
                file = candidates.FirstOrDefault(f => f.FileKey == boundKey);
                if (file != null) return true;
            }

            if (candidates.Count == 1)
            {
                file = candidates[0];
                return true;
            }

            if (candidates.Count > 1)
            {
                error = FigmaSourceText.BridgeAmbiguous(
                    string.Join(", ", candidates.Select(f => FigmaSourceText.DescribeFile(f.FileName, f.FileKey))));
                return false;
            }

            var draft = files == null ? null : files.FirstOrDefault(f => f != null && string.IsNullOrEmpty(f.FileKey));
            error = draft != null
                ? FigmaSourceText.BridgeUnsavedFile(draft.FileName)
                : FigmaSourceText.BridgeNoFile(settings.BridgePort);
            return false;
        }

        public static IReadOnlyList<HubFile> Importable(IReadOnlyList<HubFile> files, Func<HubFile, bool> isUsable = null)
        {
            if (files == null) return new List<HubFile>();
            var lastIndexByKey = new Dictionary<string, int>();
            for (var i = 0; i < files.Count; i++)
                if (files[i] != null && !string.IsNullOrEmpty(files[i].FileKey))
                    lastIndexByKey[files[i].FileKey] = i;
            return lastIndexByKey.Values
                .OrderBy(i => i)
                .Select(i => files[i])
                .Where(f => isUsable == null || isUsable(f))
                .ToList();
        }

        public static async Task<FigmaFileTarget> ResolveAsync(UnityFigmaBridgeSettings settings, TimeSpan timeout)
        {
            if (settings.Source != FigmaSourceKind.Bridge)
            {
                var fileId = settings.FileId;
                if (string.IsNullOrEmpty(fileId))
                    throw new FigmaFileTargetException(FigmaSourceText.RestInvalidUrlBody);
                return new FigmaFileTarget(fileId, "");
            }

            HubClient client;
            try
            {
                client = await HubClient.Connect(settings.BridgePort, timeout).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                throw new FigmaFileTargetException(FigmaSourceText.BridgeNoHub(settings.BridgePort), e);
            }

            try
            {
                var deadline = DateTime.UtcNow.AddSeconds(1);
                while (client.Files.Count == 0 && DateTime.UtcNow < deadline)
                    await Task.Delay(50).ConfigureAwait(false);

                if (!TryPick(settings, client.Files, out var picked, out var error, client.IsUsable))
                    throw new FigmaFileTargetException(error);
                return new FigmaFileTarget(picked.FileKey, picked.FileName ?? "");
            }
            finally
            {
                client.Dispose();
            }
        }

        public static FigmaFileTarget Resolve(UnityFigmaBridgeSettings settings, TimeSpan timeout)
        {
            try
            {
                return ResolveAsync(settings, timeout).GetAwaiter().GetResult();
            }
            catch (AggregateException e) when (e.InnerExceptions.Count == 1)
            {
                throw e.InnerExceptions[0];
            }
        }
    }
}
