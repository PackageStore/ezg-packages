using Ezg.ProceduralAnimation;
using UnityEditor;
using UnityEngine;

namespace Ezg.ProceduralAnimation.Editor
{
    public static class PoseCombinationClipBuilder
    {
        public static AnimationClip BuildAndSaveClip(
            PoseCombinationGraphAsset graph,
            PoseCombinationPath path,
            string baseName,
            int batchIndex)
        {
            if (graph == null || path == null)
            {
                throw new System.ArgumentNullException();
            }

            string clipName = PoseCombinationGraphResolver.ResolveClipName(graph, path, baseName, batchIndex);
            InbetweenGenerationSettings settings = PoseCombinationGraphResolver.ConvertPathToGenerationSettings(graph, path, clipName);

            return AnimationClipWriter.GenerateClip(settings, graph.generationOptions.overwriteExistingClips);
        }

        public static AnimationClip BuildPreviewClip(
            PoseCombinationGraphAsset graph,
            PoseCombinationPath path)
        {
            if (graph == null || path == null)
            {
                throw new System.ArgumentNullException();
            }

            string clipName = $"__preview_{path.GetPathDisplayName(graph)}";
            InbetweenGenerationSettings settings = PoseCombinationGraphResolver.ConvertPathToGenerationSettings(graph, path, clipName);

            return AnimationClipWriter.GenerateClipInMemory(settings);
        }
    }
}
