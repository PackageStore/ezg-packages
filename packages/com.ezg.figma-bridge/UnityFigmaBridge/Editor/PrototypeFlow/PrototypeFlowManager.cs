using System;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using UnityFigmaBridge.Editor.FigmaApi;
using UnityFigmaBridge.Runtime.UI;
using Color = UnityEngine.Color;

namespace UnityFigmaBridge.Editor.PrototypeFlow
{
    public static class PrototypeFlowManager
    {
        private static string s_CompiledPatternSource;
        private static Regex s_CompiledPattern;
        private static bool s_PatternInvalid;

        /// <summary>
        /// Add in prototype flow functionality for this node, if required
        /// </summary>
        /// <param name="node"></param>
        /// <param name="nodeGameObject"></param>
        /// <param name="figmaImportProcessData"></param>
        public static void ApplyPrototypeFunctionalityToNode(Node node, GameObject nodeGameObject,
            FigmaImportProcessData figmaImportProcessData)
        {

            if (CheckAddButtonBehaviour(node, figmaImportProcessData))
            {
                if (nodeGameObject.GetComponent<Button>() == null)
                {
                    var newButtonComponent = nodeGameObject.AddComponent<Button>();

                    // Find target graphic if appropriate for showing selected state
                    for (var i = 0; i < nodeGameObject.transform.childCount; i++)
                    {
                        var child = nodeGameObject.transform.GetChild(i);
                        if (child.name.ToLower().Contains("selected"))
                        {
                            newButtonComponent.targetGraphic = child.GetComponent<Graphic>();
                            newButtonComponent.transition = Selectable.Transition.ColorTint;
                            newButtonComponent.colors = new ColorBlock
                            {
                                disabledColor = new Color(0, 0, 0, 0),
                                normalColor = new Color(0, 0, 0, 0),
                                highlightedColor = Color.white,
                                pressedColor = Color.white,
                                selectedColor = Color.white,
                                colorMultiplier = 1,
                            };
                        }
                    }
                }
            }

            if (!figmaImportProcessData.Settings.BuildPrototypeFlow) return;

            // Implement button if it has a prototype connection attached
            if (string.IsNullOrEmpty(node.transitionNodeID)) return;

            var prototypeFlowButton = nodeGameObject.GetComponent<FigmaPrototypeFlowButton>();
            if (prototypeFlowButton == null) prototypeFlowButton = nodeGameObject.AddComponent<FigmaPrototypeFlowButton>();
            prototypeFlowButton.TargetScreenNodeId = node.transitionNodeID;
            // Future options to add transition information
        }

        /// <summary>
        ///     A node becomes a Button when its name matches <c>ButtonNamePattern</c> (a regex,
        ///     case-insensitive; blank switches name matching off) or, with the prototype flow on,
        ///     when it carries a prototype transition.
        /// </summary>
        private static bool CheckAddButtonBehaviour(Node node, FigmaImportProcessData figmaImportProcessData)
        {
            var settings = figmaImportProcessData.Settings;
            if (MatchesButtonPattern(node.name, settings.ButtonNamePattern)) return true;
            if (settings.BuildPrototypeFlow && !string.IsNullOrEmpty(node.transitionNodeID))
                return true;
            return false;
        }

        private static bool MatchesButtonPattern(string nodeName, string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern) || string.IsNullOrEmpty(nodeName)) return false;

            if (s_CompiledPatternSource != pattern)
            {
                s_CompiledPatternSource = pattern;
                s_PatternInvalid = false;
                try
                {
                    s_CompiledPattern = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                }
                catch (ArgumentException e)
                {
                    s_CompiledPattern = null;
                    s_PatternInvalid = true;
                    Debug.LogWarning($"[PrototypeFlowManager] ButtonNamePattern '{pattern}' is not a valid regex " +
                                     $"({e.Message}) - falling back to a plain substring match.");
                }
            }

            if (s_PatternInvalid)
                return nodeName.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;
            return s_CompiledPattern != null && s_CompiledPattern.IsMatch(nodeName);
        }
    }
}
