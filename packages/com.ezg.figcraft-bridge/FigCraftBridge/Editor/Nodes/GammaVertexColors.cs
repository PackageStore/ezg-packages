using System.Collections.Generic;
using System.IO;
using UnityEditor;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using FigCraftBridge.Editor.Settings;

namespace FigCraftBridge.Editor.Nodes
{
    /// <summary>
    ///     In a Linear colour space project a Canvas converts each vertex colour to linear light and stores it in
    ///     8 bits, so a dark colour loses its channels: #0E0A06 becomes linear (1, 1, 0) and draws as (13, 13, 0),
    ///     #14100D as (22, 13, 13). With <see cref="Canvas.vertexColorAlwaysGammaSpace"/> the colour stays in
    ///     gamma and UI/Default and the TextMeshPro shaders convert it per pixel, exact to the 8-bit value.
    ///     Each imported screen and page prefab gets a Canvas of its own on its root with the flag on (a nested
    ///     Canvas keeps its own flag, whatever its parent canvas says), its shader channels for TextMeshPro and a
    ///     GraphicRaycaster, since a parent canvas's raycaster does not see the graphics of a nested one.
    ///     A shader that does not read <c>_UIVertexColorAlwaysGammaSpace</c> (an additive particle shader, say)
    ///     would then draw its vertex colour too bright, so the import warns about each one it finds.
    /// </summary>
    public static class GammaVertexColors
    {
        private const string ShaderFlag = "_UIVertexColorAlwaysGammaSpace";

        private const AdditionalCanvasShaderChannels TmpChannels =
            AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;

        private static readonly Dictionary<Shader, bool> s_Aware = new Dictionary<Shader, bool>();

        /// <summary>True when the project blends UI in linear light and the setting is on.</summary>
        public static bool IsNeeded(FigCraftBridgeSettings settings) =>
            settings != null && settings.GammaVertexColors && PlayerSettings.colorSpace == ColorSpace.Linear;

        /// <summary>Sets the flag on a canvas the bridge draws with (visual check, linear-alpha passes).</summary>
        public static void Configure(Canvas canvas)
        {
            if (PlayerSettings.colorSpace == ColorSpace.Linear) canvas.vertexColorAlwaysGammaSpace = true;
        }

        /// <summary>
        ///     Gives the root of a screen or page its own gamma vertex colour Canvas, and turns the flag on for every
        ///     Canvas under it. Returns the shaders under it that ignore the flag.
        /// </summary>
        public static List<string> Apply(GameObject root)
        {
            var canvas = root.GetComponent<Canvas>();
            if (canvas == null) canvas = root.AddComponent<Canvas>();
            canvas.additionalShaderChannels |= TmpChannels;
            if (root.GetComponent<GraphicRaycaster>() == null) root.AddComponent<GraphicRaycaster>();
            foreach (var nested in root.GetComponentsInChildren<Canvas>(true)) nested.vertexColorAlwaysGammaSpace = true;

            var unaware = new List<string>();
            foreach (var graphic in root.GetComponentsInChildren<Graphic>(true))
            {
                var material = graphic is TMP_Text text ? text.fontSharedMaterial : graphic.material;
                var shader = material != null ? material.shader : null;
                if (shader == null || Aware(shader) || unaware.Contains(shader.name)) continue;
                unaware.Add(shader.name);
            }
            return unaware;
        }

        private static bool Aware(Shader shader)
        {
            if (s_Aware.TryGetValue(shader, out var aware)) return aware;
            var path = AssetDatabase.GetAssetPath(shader);
            aware = shader.name == "UI/Default" ||
                    (path.EndsWith(".shader") && File.Exists(Path.GetFullPath(path)) &&
                     File.ReadAllText(Path.GetFullPath(path)).Contains(ShaderFlag));
            s_Aware[shader] = aware;
            return aware;
        }
    }
}
