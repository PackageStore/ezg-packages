using System;
using System.Collections.Generic;
using UnityFigmaBridge.Editor.FigmaApi;
using UnityFigmaBridge.Editor.Fonts;
using UnityFigmaBridge.Editor.Settings;

namespace UnityFigmaBridge.Editor.PostProcess
{
    /// <summary>
    ///     Everything a post-processor may need after the bridge has written its prefabs.
    ///     Built by <see cref="PostProcessorRunner"/>; read-only by convention.
    /// </summary>
    public sealed class FigmaImportContext
    {
        /// <summary>The bridge settings the import ran with.</summary>
        public UnityFigmaBridgeSettings Settings;

        /// <summary>
        ///     The Figma document. Null only when running <c>Run Post-Processors (no Sync)</c> and
        ///     no cached document exists at <c>Assets/FigmaOutput.json</c>.
        /// </summary>
        public FigmaFile SourceFile;

        /// <summary>Fonts resolved for the document. Null when <see cref="PostProcessOnly"/>.</summary>
        public FigmaFontMap FontMap;

        /// <summary>Figma document name, also the sub-folder image fills were written under.</summary>
        public string DocumentName;

        /// <summary>Resolved output folders for this import (project-relative, forward slashes).</summary>
        public string ScreenPrefabFolder;
        public string ComponentPrefabFolder;
        public string ImageFillFolder;

        /// <summary>One entry per screen prefab the bridge wrote (or found on disk).</summary>
        public IReadOnlyList<FigmaImportedScreen> Screens;

        /// <summary>
        ///     Figma component id to the prefab written for it. Empty in
        ///     <see cref="PostProcessOnly"/> mode; use <see cref="FigmaInstanceSource.ComponentPrefabPath"/>.
        /// </summary>
        public IReadOnlyDictionary<string, string> ComponentPrefabPaths;

        /// <summary>
        ///     With <c>PlainImages</c> on: nodes that carried a stroke, corner radius, gradient or a
        ///     non-rectangular shape the plain <c>Image</c> cannot draw. The node still became an
        ///     <c>Image</c> (flat colour or sprite) so layout is visible; this list says what was lost.
        /// </summary>
        public IReadOnlyList<ShapeOnlyNode> ShapeOnlyNodes;

        /// <summary>True when invoked from <c>Run Post-Processors (no Sync)</c>.</summary>
        public bool PostProcessOnly;

        /// <summary>True when the import was rebuilt from the cached document without the network.</summary>
        public bool Offline;
    }

    /// <summary>One screen frame that became a prefab.</summary>
    public sealed class FigmaImportedScreen
    {
        /// <summary>
        ///     The frame node. When the document is not available this is a stub carrying only
        ///     <c>name</c> and <c>type</c>.
        /// </summary>
        public Node Frame;

        /// <summary>Frame name as it appears in Figma (equals the prefab file name unless renamed).</summary>
        public string FrameName;

        /// <summary>Project-relative path of the raw screen prefab.</summary>
        public string PrefabPath;

        /// <summary>
        ///     Every component instance placed in this screen, flattened: nested instances inside an
        ///     instanced component are listed too, with their full <see cref="FigmaInstanceSource.SourcePathChain"/>.
        ///     Recorded before the temporary bridge markers were stripped from the prefab, which is
        ///     the last moment the component origin of an instance is still known.
        /// </summary>
        public IReadOnlyList<FigmaInstanceSource> Instances;
    }

    /// <summary>Where one component instance in a prefab came from.</summary>
    [Serializable]
    public sealed class FigmaInstanceSource
    {
        /// <summary>Figma node id of the INSTANCE node.</summary>
        public string NodeId;

        /// <summary>GameObject name of the instance root (after duplicate numbering, if enabled).</summary>
        public string NodeName;

        /// <summary>Figma component id the instance points at.</summary>
        public string ComponentId;

        /// <summary>Project-relative path of the component prefab that was instantiated.</summary>
        public string ComponentPrefabPath;

        /// <summary>
        ///     '/'-joined GameObject names from (and including) the prefab root down to the
        ///     instance root, so <c>Transform.Find</c> on the root with the part after the first
        ///     '/' reaches it.
        /// </summary>
        public string HierarchyPath;

        /// <summary>
        ///     Component prefab paths from the outermost instance down to this one. A direct
        ///     instance has one entry; an instance nested inside another component has two or
        ///     more. Match by prefix when mapping components: once an outer entry matches, the
        ///     inner ones belong to it.
        /// </summary>
        public string[] SourcePathChain;
    }

    /// <summary>A node the plain-image mode could only approximate.</summary>
    [Serializable]
    public sealed class ShapeOnlyNode
    {
        /// <summary>Prefab (screen or component) the node was written into.</summary>
        public string PrefabPath;

        /// <summary>'/'-joined GameObject names from the prefab root to the node.</summary>
        public string HierarchyPath;

        public string NodeId;
        public string NodeName;

        /// <summary>True when the node also carries an image fill, so only the decoration was lost.</summary>
        public bool HasSprite;

        public float StrokeWidth;
        public float CornerRadius;

        /// <summary>First fill's paint type (<c>SOLID</c>, <c>GRADIENT_LINEAR</c>, ...), or <c>NONE</c>.</summary>
        public string FillType;

        /// <summary>Figma node type (<c>ELLIPSE</c>, <c>STAR</c>, <c>RECTANGLE</c>, ...).</summary>
        public string Shape;
    }
}
