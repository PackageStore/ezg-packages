using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Ezg.GameVfx.EditorTools
{
    /// <summary>
    /// Data-driven VFX prefab builder of the game-vfx skill. Reads a <c>*.gamevfx.json</c> spec written by
    /// <c>make_vfx.py make</c>, imports the sheet as a Sprite (Multiple, Full Rect) sliced per frame, then builds a
    /// pool-friendly prefab on the game-vfx template (GameVFX_QuyChuan.md 7.1, 8.4): a control root (no emission,
    /// renderer off) → <c>containers</c> → one layer per element named <c>&lt;role&gt;_&lt;ab|add&gt;[_n][_sec]</c>: the
    /// flipbook (Texture Sheet Animation in Sprites mode with every frame), spark / glow layers, extra flipbooks, streams.
    /// Each entry picks its blend: <c>ab</c> (default) = base layer on the shared <c>_mat_ab</c> (Mobile/Particles/Alpha
    /// Blended, glow baked into the sheet's alpha); <c>add</c> = optional additive layer on top, shared <c>_mat_add</c>
    /// (Mobile/Particles/Additive), only when the ab layer is not bright enough. When the project has the GameVFX module, the root gets <c>FXEffect</c> and stop
    /// action Callback; otherwise one-shots disable themselves (stop action Disable) so a pool reclaims them.
    /// New VFX need no C# change and no recompile. Also renders offline previews and builds a showcase scene.
    /// Needs package com.unity.2d.sprite (sprite slicing API).
    /// </summary>
    public static class GameVfxPrefabBuilder
    {
        #region Fields

        // game-vfx 8.2 / 8.4: shared material per blend. ab = base layer (glow baked into alpha), add = additive layer drawn
        // on top of the ab layers (optional: only when the ab layer is not bright enough)
        private const string BLEND_AB = "ab";
        private const string BLEND_ADD = "add";
        private const string MATERIAL_AB = "_mat_ab";
        private const string MATERIAL_ADD = "_mat_add";
        private const string SHADER_AB = "Mobile/Particles/Alpha Blended";
        private const string SHADER_ADD = "Mobile/Particles/Additive";
        private const string DEFAULT_ROOT = "Assets/_Project/Visual/ArtAsset/Shared/VFX/Generated";   // = make_vfx DEFAULT_VFX_ROOT
        private const string PROFILE_PATH = ".claude/project-profile.json";
        private const string SHOWCASE_TYPE = "Ezg.GameVfx.GameVfxShowcaseLoop";
        private const string FX_EFFECT_TYPE = "FXEffect";                        // GameVFX module (game-vfx 7.8)
        private const string CONTAINERS = "containers";
        private const string SECONDARY = "_sec";
        private const float LOOP_LIFETIME = 600f;        // looping flipbooks: one long-lived particle cycling its sheet
        // flipbooks span up to ~half the screen: game-vfx 7.3's 0.5 would clamp them (it cuts, not shrinks); small
        // particles keep the standard cap
        private const float FLIPBOOK_MAX_PARTICLE_SIZE = 3f;
        private const float MAX_PARTICLE_SIZE = 0.5f;
        private const float TAU = Mathf.PI * 2f;
        private const int CURVE_SAMPLES = 7;
        private const int SPRITE_PPU = 100;
        private const float FRAME_END = 0.9999f;          // < 1 so the last cycle value never indexes past the last sprite

        /// <summary>Spark layer: stretched needles burst from a circle / arc.</summary>
        [Serializable]
        public class SparkSpec
        {
            public int count = 8;
            public float[] life = { 0.2f, 0.4f };
            public float[] speed = { 8f, 16f };
            public float[] size = { 0.15f, 0.25f };
            public float arc = 360f;
            public float radius = 0.2f;
            public float delay;
            public float drag = 4f;
            public float gravity;
            public string blend = BLEND_AB;   // "ab" | "add" (game-vfx 8.4)
        }

        /// <summary>Mote layer: glowing dots — "drift" (burst, floats off), "rise" (stream upward), "trail" (world-space, per distance),
        /// "birth" (only inside <see cref="FlipSpec.trail"/>: world-space stream following each particle of that layer).</summary>
        [Serializable]
        public class MoteSpec
        {
            public string mode = "drift";
            public int count;
            public float rate;
            public float[] life = { 0.6f, 1f };
            public float[] speed = { 0f, 0f };
            public float[] size = { 0.15f, 0.25f };
            public float radius = 0.5f;
            public float scaleY = 1f;
            public float delay;
            public float duration;
            public float[] rise = { 0f, 0f };
            public float gravity;
            public float drag;
            public float noise;
            public bool stretch;              // stretched needles (spark sprite) instead of round glow dots
            public string blend = BLEND_AB;   // "ab" | "add" (game-vfx 8.4)
        }

        /// <summary>Extra flipbook layer of a multi-sheet effect (e.g. a falling head, a ground mark, a crater): its own sheet,
        /// timing, sorting, constant motion and optional birth sub-emitter trails.</summary>
        [Serializable]
        public class FlipSpec
        {
            public string name = "Layer";
            public string role = "glow";      // game-vfx 8.4 role of the layer
            public string blend = BLEND_AB;   // "ab" base layer | "add" additive layer on top (game-vfx 8.4)
            public bool secondary;            // "_sec": dropped on low tier (game-vfx 2.2, 6.4)
            public string sheet;
            public int cols = 4;
            public int rows = 2;
            public int frames = 8;
            public float life = 0.5f;          // particle lifetime (s)
            public float cycle;               // > 0: seconds per sheet cycle (sheet loops over the life); 0: plays once
            public float size = 2f;
            public float delay;
            public float[] position = { 0f, 0f };   // local start (units)
            public float[] velocity = { 0f, 0f };   // constant local velocity (units/s)
            public bool randomRotation;
            public float lengthScale;         // > 0: stretched billboard along the velocity (U=0 on the particle, quad trails behind)
            public float velocityScale;
            public float[] fade = { 0f, 0f };       // alpha fade-in / fade-out as fractions of the life
            public float[] grow = { 1f, 1f };       // size multiplier at birth / death
            public string sortingLayer = "";        // empty: the spec's layer
            public int sortingOrder = 10;
            public MoteSpec[] trail = Array.Empty<MoteSpec>();
            public float trailBack;           // trails emit this far behind the particle along -velocity (stretched heads: on the painted head)
        }

        /// <summary>Stream layer: a looping emitter of a painted single sprite (e.g. thrown daggers) flung out of a circle,
        /// stretched along its velocity; the emission point can sweep the circle and the flight can curve (spiral).</summary>
        [Serializable]
        public class StreamSpec
        {
            public string name = "Stream";
            public string role = "debris";
            public string blend = BLEND_AB;   // "ab" | "add" (game-vfx 8.4)
            public string sprite;             // png next to the spec, painted with its head on the LEFT (stretched U=0)
            public float rate = 10f;          // particles per second, forever (loop prefabs) or for 1 s (one-shots)
            public float[] life = { 0.4f, 0.5f };
            public float[] speed = { 8f, 12f };
            public float[] size = { 0.5f, 0.5f };
            public float radius = 0.5f;       // emission circle (units)
            public float arcSpeed;            // > 0: emission point sweeps the circle CCW (loops/s); 0: random directions
            public float orbital;             // CCW turn of the flight around the centre (deg/s): curved, spiral paths
            public float drag;
            public float lengthScale = 2.5f;  // stretched billboard length (x size); 0: plain billboard
            public float squash = 1f;         // y scale of the layer transform (3/4 ground plane)
            public float[] fade = { 0.1f, 0.35f };   // alpha fade-in / fade-out as fractions of the life
            public int sortingOrder = 12;
        }

        /// <summary>The spec written by make_vfx.py (unknown fields such as "qa" are ignored).</summary>
        [Serializable]
        public class Spec
        {
            public string name;
            public string role = "impact";    // main flipbook layer role (game-vfx 8.4)
            public string blend = BLEND_AB;   // main flipbook = the ab base layer (game-vfx 8.4)
            public string recipe;
            public string element;
            public string kind;
            public string sheet;
            public int cols = 4;
            public int rows = 4;
            public int frames = 16;
            public float life = 0.5f;
            public float size = 4f;
            public float delay;               // start delay of the main flipbook (multi-sheet effects)
            public bool loop;
            public bool randomRotation;
            public bool alignLocal;
            public float offsetX;
            public float offsetY;
            public string sortingLayer = "FX";
            public int sortingOrder = 10;
            public string material = "";      // optional explicit material path for ab layers; empty = the project's _mat_ab
            public string materialAdd = "";   // optional explicit material path for add layers; empty = the project's _mat_add
            public string[] sparkColors = { "#ffffff", "#ffdb66", "#ff801f", "#f2380f" };
            public string sharedDir = DEFAULT_ROOT + "/_Shared";
            public SparkSpec[] sparks = Array.Empty<SparkSpec>();
            public MoteSpec[] motes = Array.Empty<MoteSpec>();
            public FlipSpec[] flipbooks = Array.Empty<FlipSpec>();
            public float spin;                // main flipbook turns at this rate (deg/s, + = CCW): top-down whirls spin smoothly
            public float squash = 1f;         // y scale of the main flipbook transform (top-down sheet seen in 3/4 view)
            public float intro;               // loops: seconds the main flipbook fades + grows in after the prefab is enabled
            public StreamSpec[] streams = Array.Empty<StreamSpec>();
        }

        #endregion

        #region Public

        /// <summary>Warnings of the last <see cref="Build"/> (missing sorting layer, material fallback…).</summary>
        public static readonly List<string> LastWarnings = new List<string>();

        /// <summary>Pick a spec file and build its prefab.</summary>
        [MenuItem("Tools/GameVFX/Build Prefab From Spec...")]
        public static void BuildFromPanel()
        {
            string abs = EditorUtility.OpenFilePanel("VFX spec", VfxRoot(), "json");
            if (string.IsNullOrEmpty(abs))
            {
                return;
            }
            Debug.Log("[GameVfxKit] Built " + Build(ToAssetPath(abs)));
        }

        /// <summary>Rebuild every *.gamevfx.json under Assets.</summary>
        [MenuItem("Tools/GameVFX/Rebuild All Specs")]
        public static void BuildAllMenu()
        {
            Debug.Log("[GameVfxKit] Built " + BuildAll("Assets").Length + " prefabs");
        }

        /// <summary>Builds every spec found under <paramref name="root"/>; returns the prefab paths.</summary>
        public static string[] BuildAll(string root)
        {
            var results = new List<string>();
            foreach (var file in Directory.GetFiles(root, "*.gamevfx.json", SearchOption.AllDirectories))
            {
                results.Add(Build(file.Replace('\\', '/')));
            }
            return results.ToArray();
        }

        /// <summary>Imports + slices the sheet and builds (or rebuilds in place) the prefab for one spec. Returns the prefab
        /// path; warnings land in <see cref="LastWarnings"/> and the console.</summary>
        public static string Build(string specPath)
        {
            LastWarnings.Clear();
            var spec = JsonUtility.FromJson<Spec>(File.ReadAllText(specPath));
            string dir = Path.GetDirectoryName(specPath).Replace('\\', '/');
            AssetDatabase.Refresh();

            // material per blend, resolved once per build (ab always; add only when a layer asks for it)
            var mats = new Dictionary<string, Material>();
            _matFor = blend =>
            {
                string b = BlendOf(blend);
                if (!mats.TryGetValue(b, out var m))
                {
                    m = ResolveMaterial(spec, dir, b);
                    mats[b] = m;
                }
                return m;
            };
            var frames = SliceSheet(dir + "/" + spec.sheet, spec.cols, spec.rows, spec.frames);
            var layerFrames = new Sprite[spec.flipbooks.Length][];
            for (int i = 0; i < spec.flipbooks.Length; i++)
            {
                var fb = spec.flipbooks[i];
                layerFrames[i] = SliceSheet(dir + "/" + fb.sheet, fb.cols, fb.rows, fb.frames);
            }

            Sprite spark = null, mote = null;
            if (spec.sparks.Length > 0 || spec.motes.Length > 0 || spec.flipbooks.Any(fb => fb.trail.Length > 0))
            {
                spark = ImportSingleSprite(spec.sharedDir + "/FX_TX_Spark.png");
                mote = ImportSingleSprite(spec.sharedDir + "/FX_TX_Mote.png");
            }
            var streamSprites = spec.streams.Select(st => ImportSingleSprite(dir + "/" + st.sprite)).ToArray();

            string prefabPath = dir + "/" + spec.name + ".prefab";
            // The user may be inspecting this prefab in Prefab Mode: a stale stage could later be saved over the
            // rebuilt asset, so refuse when it has edits and refresh it afterwards otherwise.
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            bool reopen = stage != null && stage.assetPath == prefabPath;
            if (reopen && stage.scene.isDirty)
            {
                throw new InvalidOperationException("[GameVfxKit] " + prefabPath + " is open in Prefab Mode with unsaved changes - save or close it, then rebuild");
            }
            _names.Clear();
            var fxEffect = FindType(FX_EFFECT_TYPE, typeof(MonoBehaviour));
            var root = new GameObject(spec.name);
            try
            {
                var rootPs = ControlSystem(root);
                var containers = new GameObject(CONTAINERS);
                containers.transform.SetParent(root.transform, false);
                var containersPs = ControlSystem(containers);
                var parent = containers.transform;

                float end = BuildFlipbook(parent, spec, _matFor(spec.blend), frames);
                var colors = ParseColors(spec.sparkColors);
                for (int i = 0; i < spec.sparks.Length; i++)
                {
                    end = Mathf.Max(end, BuildSparks(parent, spec, spec.sparks[i], _matFor(spec.sparks[i].blend), spark, colors));
                }
                for (int i = 0; i < spec.motes.Length; i++)
                {
                    end = Mathf.Max(end, BuildMotes(parent, spec, spec.motes[i], _matFor(spec.motes[i].blend), mote, colors));
                }
                for (int i = 0; i < spec.flipbooks.Length; i++)
                {
                    end = Mathf.Max(end, BuildLayer(parent, spec, spec.flipbooks[i], _matFor(spec.flipbooks[i].blend), layerFrames[i], spark, mote, colors));
                }
                for (int i = 0; i < spec.streams.Length; i++)
                {
                    end = Mathf.Max(end, BuildStream(parent, spec, spec.streams[i], _matFor(spec.streams[i].blend), streamSprites[i]));
                }

                foreach (var ps in new[] { rootPs, containersPs })
                {
                    var main = ps.main;
                    main.duration = spec.loop ? 1f : end + 0.05f;
                    main.loop = spec.loop;
                    main.stopAction = ParticleSystemStopAction.None;
                }
                var rootMain = rootPs.main;
                if (fxEffect != null)
                {
                    // game-vfx 7.1 / 7.8: FXEffect hears the root's Callback and finishes (Disable by default -> pool)
                    root.AddComponent(fxEffect);
                    rootMain.stopAction = ParticleSystemStopAction.Callback;
                }
                else if (!spec.loop)
                {
                    // no GameVFX module: a Callback would reach nobody, so one-shots disable themselves and a pool that
                    // reclaims on OnDisable (PoolingManager) takes them back. Loops are stopped/disabled by the caller.
                    rootMain.stopAction = ParticleSystemStopAction.Disable;
                }
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
            AssetDatabase.SaveAssets();
            if (reopen)
            {
                StageUtility.GoToMainStage();
                PrefabStageUtility.OpenPrefab(prefabPath);
            }
            CheckSortingLayers(spec);
            foreach (var w in LastWarnings)
            {
                Debug.LogWarning("[GameVfxKit] " + w);
            }
            return prefabPath;
        }

        /// <summary>One line per particle system of a prefab (path, loop, duration, stop action, max particles, renderer,
        /// sorting, material, sprites) plus warnings: to verify a build and to report it.</summary>
        public static string Describe(string prefabPath)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (go == null)
            {
                return "missing prefab " + prefabPath;
            }
            var layers = new HashSet<string>(SortingLayer.layers.Select(l => l.name));
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(prefabPath + "  components: " + string.Join(", ", go.GetComponents<Component>().Select(c => c.GetType().Name)));
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                var r = ps.GetComponent<ParticleSystemRenderer>();
                var main = ps.main;
                string path = AnimationUtility.CalculateTransformPath(ps.transform, go.transform);
                sb.Append(string.IsNullOrEmpty(path) ? "(root)" : path)
                  .Append($"  loop={main.loop} dur={main.duration:0.##} stop={main.stopAction} max={main.maxParticles}")
                  .Append($" emit={ps.emission.enabled} render={(r.enabled ? r.renderMode.ToString() : "off")}");
                if (r.enabled)
                {
                    string flag = r.sortingLayerName == "Default" ? "(DEFAULT: 7.3)" : layers.Contains(r.sortingLayerName) ? "" : "(MISSING)";
                    sb.Append($" layer={r.sortingLayerName}{flag}/{r.sortingOrder}")
                      .Append($" mat={(r.sharedMaterial != null ? r.sharedMaterial.name : "NONE")} sprites={ps.textureSheetAnimation.spriteCount}");
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        /// <summary>Adds the VFX sorting layers when missing: <paramref name="ground"/> right below <paramref name="anchor"/>
        /// (the characters' layer) and <paramref name="main"/> right above it (game-vfx 7.3). Run only after the dev agreed
        /// on the names; returns what changed.</summary>
        public static string EnsureSortingLayers(string ground = "FX_Ground", string main = "FX", string anchor = "Default")
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var list = tagManager.FindProperty("m_SortingLayers");
            var names = Enumerable.Range(0, list.arraySize).Select(i => list.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue).ToList();
            if (!names.Contains(anchor))
            {
                return "anchor sorting layer '" + anchor + "' not found: nothing changed";
            }
            var ids = new HashSet<long>(Enumerable.Range(0, list.arraySize).Select(i => list.GetArrayElementAtIndex(i).FindPropertyRelative("uniqueID").longValue));
            var rng = new System.Random();
            var added = new List<string>();
            void Insert(string name, bool below)
            {
                if (string.IsNullOrEmpty(name) || names.Contains(name))
                {
                    return;
                }
                int at = names.IndexOf(anchor) + (below ? 0 : 1);
                list.InsertArrayElementAtIndex(at);
                var e = list.GetArrayElementAtIndex(at);
                long id;
                do
                {
                    id = (uint)rng.Next(1, int.MaxValue);
                } while (ids.Contains(id));
                ids.Add(id);
                e.FindPropertyRelative("name").stringValue = name;
                e.FindPropertyRelative("uniqueID").longValue = id;
                e.FindPropertyRelative("locked").boolValue = false;
                names.Insert(at, name);
                added.Add(name);
            }
            Insert(ground, true);
            Insert(main, false);
            tagManager.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            return added.Count == 0 ? "sorting layers already present" : "added " + string.Join(", ", added) + " -> " + string.Join(" < ", names);
        }

        /// <summary>Renders a prefab offline in a preview scene (deterministic seeds) to numbered PNGs.
        /// <paramref name="background"/>: "#rrggbb" (solid) or a Sprite asset path (tiled ground).</summary>
        /// <returns>Number of frames written.</returns>
        public static int RenderFrames(string prefabPath, string outDir, float orthoSize, float camY, float fps, float length, int px, uint seed, string background = "#1c2027")
        {
            Directory.CreateDirectory(outDir);
            var scene = EditorSceneManager.NewPreviewScene();
            var rt = new RenderTexture(px, px, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var tex = new Texture2D(px, px, TextureFormat.RGB24, false);
            int frames = 0;
            try
            {
                var fx = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath));
                SceneManager.MoveGameObjectToScene(fx, scene);
                var cam = CreateCamera(scene, orthoSize, camY, background);
                CreateGround(scene, orthoSize + Mathf.Abs(camY), background);
                cam.scene = scene;
                cam.targetTexture = rt;
                cam.enabled = false;

                var systems = fx.GetComponentsInChildren<ParticleSystem>(true);
                for (int i = 0; i < systems.Length; i++)
                {
                    systems[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    systems[i].useAutoRandomSeed = false;
                    systems[i].randomSeed = seed + (uint)i * 7919u;
                }
                var root = fx.GetComponent<ParticleSystem>();
                int count = Mathf.CeilToInt(length * fps) + 1;
                for (int i = 0; i < count; i++)
                {
                    root.Simulate(i / fps, true, true, true);
                    cam.Render();
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, px, px), 0, 0);
                    tex.Apply();
                    RenderTexture.active = null;
                    File.WriteAllBytes(Path.Combine(outDir, $"f_{i:000}.png"), tex.EncodeToPNG());
                    frames++;
                }
            }
            finally
            {
                RenderTexture.active = null;
                EditorSceneManager.ClosePreviewScene(scene);
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(tex);
            }
            return frames;
        }

        /// <summary>Builds a play-mode showcase scene (additive, saved, closed — the active scene is untouched). Needs the
        /// runtime driver (<c>make_vfx.py install --showcase</c>).</summary>
        public static void BuildShowcase(string[] prefabPaths, string scenePath, float orthoSize = 14f, int cols = 2, float spacingX = 8f, float spacingY = 6.5f, string background = "#1c2027")
        {
            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
            {
                // creating a scene leaves Prefab Mode -> a modal "Prefab Has Been Modified" can block the Editor (and MCP)
                throw new InvalidOperationException("[GameVfxKit] Close Prefab Mode before building the showcase scene");
            }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                var cam = CreateCamera(scene, orthoSize, 0f, background);
                cam.tag = "MainCamera";
                cam.gameObject.AddComponent<AudioListener>();
                CreateGround(scene, orthoSize, background);
                var go = new GameObject("VfxShowcase");
                SceneManager.MoveGameObjectToScene(go, scene);
                var type = FindType(SHOWCASE_TYPE, typeof(MonoBehaviour));
                if (type == null)
                {
                    Debug.LogWarning("[GameVfxKit] GameVfxShowcaseLoop not installed (make_vfx.py install --showcase) — scene saved without driver");
                }
                else
                {
                    var so = new SerializedObject(go.AddComponent(type));
                    var slots = so.FindProperty("_slots");
                    slots.arraySize = prefabPaths.Length;
                    int rows = Mathf.CeilToInt(prefabPaths.Length / (float)cols);
                    for (int i = 0; i < prefabPaths.Length; i++)
                    {
                        var prefab = AssetDatabase.LoadAssetAtPath<ParticleSystem>(prefabPaths[i]);
                        var slot = slots.GetArrayElementAtIndex(i);
                        float x = (i % cols - (cols - 1) * 0.5f) * spacingX;
                        float y = ((rows - 1) * 0.5f - i / cols) * spacingY;
                        slot.FindPropertyRelative("Prefab").objectReferenceValue = prefab;
                        slot.FindPropertyRelative("Position").vector3Value = new Vector3(x, y, 0f);
                        bool loop = prefab != null && prefab.main.loop;
                        slot.FindPropertyRelative("Interval").floatValue = loop ? 0f : Mathf.Max(1.2f, prefab != null ? prefab.main.duration + 0.6f : 1.6f);
                        slot.FindPropertyRelative("OrbitRadius").floatValue = Path.GetFileName(prefabPaths[i]).Contains("_proj_") ? 1.6f : 0f;
                    }
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                Directory.CreateDirectory(Path.GetDirectoryName(scenePath));
                EditorSceneManager.SaveScene(scene, scenePath);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        #endregion

        #region Private - Assets

        private static readonly Dictionary<string, int> _names = new Dictionary<string, int>();
        private static readonly Dictionary<string, string> _materialPaths = new Dictionary<string, string>();
        private static Func<string, Material> _matFor;     // set by Build: blend -> material of the spec being built

        /// <summary>"ab" unless the entry says "add" (game-vfx 8.4: ab = base layer, add = additive layer on top).</summary>
        private static string BlendOf(string blend)
        {
            return string.Equals(blend, BLEND_ADD, StringComparison.OrdinalIgnoreCase) ? BLEND_ADD : BLEND_AB;
        }

        /// <summary>Layer name of game-vfx 8.4: &lt;role&gt;_&lt;ab|add&gt;, numbered when role + blend repeat, "_sec" for
        /// secondary layers.</summary>
        private static string LayerName(string role, bool secondary, string blend)
        {
            string key = (string.IsNullOrEmpty(role) ? "glow" : role.ToLowerInvariant()) + "_" + BlendOf(blend);
            _names.TryGetValue(key, out int n);
            _names[key] = ++n;
            return key + (n > 1 ? "_" + n : "") + (secondary ? SECONDARY : "");
        }

        /// <summary>vfxRoot of .claude/project-profile.json (same default as make_vfx.py).</summary>
        private static string VfxRoot()
        {
            if (File.Exists(PROFILE_PATH))
            {
                var m = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(PROFILE_PATH), "\"vfxRoot\"\\s*:\\s*\"([^\"]+)\"");
                if (m.Success)
                {
                    return m.Groups[1].Value;
                }
            }
            return DEFAULT_ROOT;
        }

        private static Type FindType(string name, Type baseType)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (System.Reflection.ReflectionTypeLoadException e)
                {
                    types = e.Types.Where(t => t != null).ToArray();
                }
                foreach (var t in types)
                {
                    if ((t.FullName == name || t.Name == name) && baseType.IsAssignableFrom(t) && !t.IsAbstract)
                    {
                        return t;
                    }
                }
            }
            return null;
        }

        /// <summary>Material of a layer by its blend (game-vfx 8.2, 8.4): "ab" -> the project's <c>_mat_ab</c> (alpha blend,
        /// glow baked into the sheet's alpha), "add" -> <c>_mat_add</c> (additive, drawn on top of the ab layers). The spec's
        /// explicit <c>material</c> / <c>materialAdd</c> win. Several -> the one referenced by most prefabs; none -> created
        /// next to the VFX root (Mobile/Particles).</summary>
        private static Material ResolveMaterial(Spec spec, string specDir, string blend)
        {
            bool add = blend == BLEND_ADD;
            string materialName = add ? MATERIAL_ADD : MATERIAL_AB;
            string materialShader = add ? SHADER_ADD : SHADER_AB;
            string explicitPath = add ? spec.materialAdd : spec.material;
            if (!string.IsNullOrEmpty(explicitPath))
            {
                var explicitMat = AssetDatabase.LoadAssetAtPath<Material>(explicitPath);
                if (explicitMat == null)
                {
                    throw new InvalidOperationException("[GameVfxKit] spec material not found: " + explicitPath);
                }
                return explicitMat;
            }
            if (_materialPaths.TryGetValue(blend, out string cached) && AssetDatabase.LoadAssetAtPath<Material>(cached) != null)
            {
                return AssetDatabase.LoadAssetAtPath<Material>(cached);
            }
            var candidates = AssetDatabase.FindAssets(materialName + " t:Material")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => string.Equals(Path.GetFileNameWithoutExtension(p), materialName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (candidates.Count > 1)
            {
                var blended = candidates.Where(p => AssetDatabase.LoadAssetAtPath<Material>(p).shader.name == materialShader).ToList();
                if (blended.Count > 0)
                {
                    candidates = blended;
                }
            }
            if (candidates.Count > 1)
            {
                var uses = candidates.ToDictionary(p => p, p => 0);
                foreach (var prefab in AssetDatabase.FindAssets("t:Prefab").Select(AssetDatabase.GUIDToAssetPath))
                {
                    foreach (var dep in AssetDatabase.GetDependencies(prefab, false))
                    {
                        if (uses.ContainsKey(dep))
                        {
                            uses[dep]++;
                        }
                    }
                }
                candidates = candidates.OrderByDescending(p => uses[p]).ToList();
                LastWarnings.Add("several " + materialName + " materials, using the most referenced: " + candidates[0]);
            }
            if (candidates.Count == 0)
            {
                if (GraphicsSettings.defaultRenderPipeline != null)
                {
                    throw new InvalidOperationException("[GameVfxKit] no " + materialName + " in the project and it uses a Scriptable Render Pipeline: " +
                                                        "create a " + (add ? "additive" : "alpha-blended") + " particle material named " + materialName +
                                                        " (or set \"" + (add ? "materialAdd" : "material") + "\" in the spec)");
                }
                string folder = Path.GetDirectoryName(Path.GetDirectoryName(specDir)).Replace('\\', '/') + "/Materials";
                Directory.CreateDirectory(folder);
                var created = new Material(Shader.Find(materialShader)) { name = materialName };
                AssetDatabase.CreateAsset(created, folder + "/" + materialName + ".mat");
                candidates.Add(folder + "/" + materialName + ".mat");
                LastWarnings.Add("created " + candidates[0] + " (" + materialShader + "): the project had no " + materialName);
            }
            var mat = AssetDatabase.LoadAssetAtPath<Material>(candidates[0]);
            if (mat.shader.name != materialShader)
            {
                LastWarnings.Add(candidates[0] + " uses shader " + mat.shader.name + ", not " + materialShader +
                                 (add ? ": additive layers expect an additive particle shader" : ": the sheets bake glow into alpha for alpha blending"));
            }
            _materialPaths[blend] = candidates[0];
            return mat;
        }

        /// <summary>game-vfx 7.3: no VFX on <c>Default</c> or on a layer the project does not have (Unity silently falls back to Default).</summary>
        private static void CheckSortingLayers(Spec spec)
        {
            var existing = new HashSet<string>(SortingLayer.layers.Select(l => l.name));
            var used = new HashSet<string> { spec.sortingLayer };
            foreach (var fb in spec.flipbooks)
            {
                if (!string.IsNullOrEmpty(fb.sortingLayer))
                {
                    used.Add(fb.sortingLayer);
                }
            }
            foreach (var layer in used)
            {
                if (!existing.Contains(layer))
                {
                    LastWarnings.Add("sorting layer '" + layer + "' does not exist: renderers fall back to Default. Add the game's VFX layers " +
                                     "(game-vfx 7.3; GameVfxPrefabBuilder.EnsureSortingLayers after the dev agrees) or pass --layer");
                }
                else if (layer == "Default")
                {
                    LastWarnings.Add("VFX on sorting layer Default (game-vfx 7.3, gate V-14)");
                }
            }
        }

        private static string ToAssetPath(string abs)
        {
            abs = abs.Replace('\\', '/');
            int i = abs.IndexOf("/Assets/", StringComparison.Ordinal);
            return i >= 0 ? abs.Substring(i + 1) : abs;
        }

        /// <summary>Project FX texture convention: Sprite (2D and UI), Full Rect, centre pivot, no mips, clamp.</summary>
        private static TextureImporter ApplySpriteSettings(string path, SpriteImportMode mode, bool mips)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException("[GameVfxKit] Missing texture " + path + " (run make_vfx.py make / shared)");
            }
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = mode;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            settings.spritePivot = new Vector2(0.5f, 0.5f);
            settings.spritePixelsPerUnit = SPRITE_PPU;
            settings.spriteGenerateFallbackPhysicsShape = false;
            importer.SetTextureSettings(settings);
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = mips;             // sheets: no mips so frames never bleed into each other
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            // game-vfx 4.5: ASTC 4x4 on mobile (smooth glow gradients band at 6x6), Android override present (gate V-5)
            foreach (var platform in new[] { "Android", "iPhone" })
            {
                var ps = importer.GetPlatformTextureSettings(platform);
                ps.overridden = true;
                ps.maxTextureSize = 2048;
                ps.format = TextureImporterFormat.ASTC_4x4;
                importer.SetPlatformTextureSettings(ps);
            }
            importer.SaveAndReimport();
            return importer;
        }

        /// <summary>Slices the sheet into cols x rows frames named "&lt;texture&gt;_&lt;i&gt;" (row-major from the top-left,
        /// Unity texture-sheet order); existing sprite IDs are kept so references survive a rebuild.</summary>
        private static Sprite[] SliceSheet(string path, int cols, int rows, int frames)
        {
            var importer = ApplySpriteSettings(path, SpriteImportMode.Multiple, false);
            importer.GetSourceTextureWidthAndHeight(out int texW, out int texH);
            int w = texW / cols, h = texH / rows;
            string baseName = Path.GetFileNameWithoutExtension(path);

            var factory = new SpriteDataProviderFactories();
            factory.Init();
            var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
            provider.InitSpriteEditorDataProvider();
            var existing = provider.GetSpriteRects().GroupBy(r => r.name).ToDictionary(g => g.Key, g => g.First().spriteID);
            var rects = new SpriteRect[frames];
            for (int i = 0; i < frames; i++)
            {
                string name = baseName + "_" + i;
                rects[i] = new SpriteRect
                {
                    name = name,
                    rect = new Rect(i % cols * w, texH - (i / cols + 1) * h, w, h),
                    alignment = SpriteAlignment.Center,
                    pivot = new Vector2(0.5f, 0.5f),
                    spriteID = existing.TryGetValue(name, out var id) ? id : GUID.Generate(),
                };
            }
            provider.SetSpriteRects(rects);
            var nameIds = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
            nameIds?.SetNameFileIdPairs(rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)).ToList());
            provider.Apply();
            importer.SaveAndReimport();

            var sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToDictionary(sp => sp.name);
            var result = new Sprite[frames];
            for (int i = 0; i < frames; i++)
            {
                if (!sprites.TryGetValue(baseName + "_" + i, out result[i]))
                {
                    throw new InvalidOperationException("[GameVfxKit] Slice failed: no sprite " + baseName + "_" + i);
                }
            }
            return result;
        }

        private static Sprite ImportSingleSprite(string path)
        {
            ApplySpriteSettings(path, SpriteImportMode.Single, true);   // tiny sprites keep mips (minified a lot)
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>Texture Sheet Animation in Sprites mode with the given frames (single sprite = static).</summary>
        private static void UseSprites(ParticleSystem ps, IList<Sprite> sprites, float cycles)
        {
            var sheet = ps.textureSheetAnimation;
            sheet.enabled = true;
            sheet.mode = ParticleSystemAnimationMode.Sprites;
            while (sheet.spriteCount > 0)
            {
                sheet.RemoveSprite(0);
            }
            foreach (var sprite in sprites)
            {
                sheet.AddSprite(sprite);
            }
            sheet.timeMode = ParticleSystemAnimationTimeMode.Lifetime;
            sheet.frameOverTime = sprites.Count > 1
                ? new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, FRAME_END))
                : new ParticleSystem.MinMaxCurve(0f);
            sheet.startFrame = 0f;
            sheet.cycleCount = Mathf.Max(1, Mathf.RoundToInt(cycles));
        }

        #endregion

        #region Private - Layers

        /// <summary>The painted flipbook: one particle plays the sliced sprites once (or cycles them forever for loops).</summary>
        private static float BuildFlipbook(Transform parent, Spec spec, Material mat, Sprite[] frames)
        {
            var ps = NewSystem(parent, LayerName(spec.role, false, spec.blend), mat, spec.sortingLayer, spec.sortingOrder, FLIPBOOK_MAX_PARTICLE_SIZE);
            ps.transform.localPosition = new Vector3(spec.offsetX * spec.size, spec.offsetY * spec.size, 0f);
            var main = ps.main;
            float lifetime = spec.loop ? LOOP_LIFETIME : spec.life;
            main.startDelay = spec.loop ? 0f : spec.delay;
            main.duration = lifetime;
            main.startLifetime = lifetime;
            main.startSize = spec.size;
            main.maxParticles = 1;
            main.startRotation = spec.randomRotation ? new ParticleSystem.MinMaxCurve(0f, TAU) : new ParticleSystem.MinMaxCurve(0f);
            Burst(ps, 0f, 1);
            UseSprites(ps, frames, spec.loop ? LOOP_LIFETIME / spec.life : 1f);

            if (spec.alignLocal)
            {
                ps.GetComponent<ParticleSystemRenderer>().alignment = ParticleSystemRenderSpace.Local;   // follows the transform (barrel / swing)
            }
            if (spec.squash > 0f && !Mathf.Approximately(spec.squash, 1f))
            {
                // Unity applies the transform scale after the particle rotation (6000.3), so a turning top-down
                // sheet stays a correct 3/4 ground ellipse
                ps.transform.localScale = new Vector3(1f, spec.squash, 1f);
            }
            if (spec.spin != 0f)
            {
                var rot = ps.rotationOverLifetime;
                rot.enabled = true;
                rot.z = new ParticleSystem.MinMaxCurve(-spec.spin * Mathf.Deg2Rad);   // particle rotation is clockwise-positive
            }
            if (spec.loop && spec.intro > 0f)
            {
                IntroOverLife(ps, spec.intro / lifetime);
            }
            return spec.loop ? 0f : spec.delay + spec.life;
        }

        /// <summary>Looping emitter of a painted sprite flung out of a circle (thrown daggers…): stretched along its velocity,
        /// emission point sweeping the circle (arc Loop) or random, flight curving with an orbital velocity, alpha in/out.</summary>
        private static float BuildStream(Transform parent, Spec spec, StreamSpec st, Material mat, Sprite sprite)
        {
            var ps = NewSystem(parent, LayerName(st.role, false, st.blend), mat, spec.sortingLayer, st.sortingOrder, FLIPBOOK_MAX_PARTICLE_SIZE);
            if (st.squash > 0f && !Mathf.Approximately(st.squash, 1f))
            {
                ps.transform.localScale = new Vector3(1f, st.squash, 1f);
            }
            UseSprites(ps, new[] { sprite }, 1f);
            var main = ps.main;
            main.loop = spec.loop;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(At(st.life, 0), At(st.life, 1));
            main.startSpeed = new ParticleSystem.MinMaxCurve(At(st.speed, 0), At(st.speed, 1));
            main.startSize = new ParticleSystem.MinMaxCurve(At(st.size, 0), At(st.size, 1));
            main.maxParticles = Mathf.Max(4, Mathf.CeilToInt(st.rate * At(st.life, 1)) + 2);
            var emission = ps.emission;
            emission.rateOverTime = st.rate;
            var shape = Circle(ps, st.radius);
            if (st.arcSpeed > 0f)
            {
                shape.arcMode = ParticleSystemShapeMultiModeValue.Loop;
                shape.arcSpeed = st.arcSpeed;
            }
            if (st.orbital != 0f)
            {
                var vel = ps.velocityOverLifetime;
                vel.enabled = true;
                vel.space = ParticleSystemSimulationSpace.Local;
                vel.orbitalZ = new ParticleSystem.MinMaxCurve(st.orbital * Mathf.Deg2Rad);
            }
            if (st.drag > 0f)
            {
                Drag(ps, st.drag);
            }
            float fadeIn = At(st.fade, 0), fadeOut = At(st.fade, 1);
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(fadeIn > 0f ? 0f : 1f, 0f), new GradientAlphaKey(1f, Mathf.Max(fadeIn, 0.001f)),
                    new GradientAlphaKey(1f, Mathf.Min(1f - fadeOut, 0.999f)), new GradientAlphaKey(fadeOut > 0f ? 0f : 1f, 1f),
                });
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(g);
            if (st.lengthScale > 0f)
            {
                var r = ps.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.Stretch;   // U=0 on the particle: the painted head leads
                r.lengthScale = st.lengthScale;
                r.velocityScale = 0f;                              // constant length while drag slows it
            }
            return spec.loop ? 0f : 1f + At(st.life, 1);
        }

        /// <summary>Extra flipbook layer: one particle playing its own sheet (once, or cycling every <c>cycle</c> s),
        /// optionally moving at a constant velocity as a stretched billboard, with birth sub-emitter trails.</summary>
        private static float BuildLayer(Transform parent, Spec spec, FlipSpec fb, Material mat, Sprite[] frames, Sprite spark, Sprite mote, Color[] colors)
        {
            string layer = string.IsNullOrEmpty(fb.sortingLayer) ? spec.sortingLayer : fb.sortingLayer;
            var ps = NewSystem(parent, LayerName(fb.role, fb.secondary, fb.blend), mat, layer, fb.sortingOrder, FLIPBOOK_MAX_PARTICLE_SIZE);
            ps.transform.localPosition = new Vector3(At(fb.position, 0), At(fb.position, 1), 0f);
            var main = ps.main;
            main.startDelay = fb.delay;
            main.duration = fb.life;
            main.startLifetime = fb.life;
            main.startSize = fb.size;
            main.maxParticles = 1;
            main.startRotation = fb.randomRotation ? new ParticleSystem.MinMaxCurve(0f, TAU) : new ParticleSystem.MinMaxCurve(0f);
            Burst(ps, 0f, 1);
            UseSprites(ps, frames, fb.cycle > 0f ? fb.life / fb.cycle : 1f);

            float vx = At(fb.velocity, 0), vy = At(fb.velocity, 1);
            if (vx != 0f || vy != 0f)
            {
                // constant velocity integrates exactly, so the particle lands where make_vfx aimed it
                var vel = ps.velocityOverLifetime;
                vel.enabled = true;
                vel.space = ParticleSystemSimulationSpace.Local;
                vel.x = new ParticleSystem.MinMaxCurve(vx);
                vel.y = new ParticleSystem.MinMaxCurve(vy);
                vel.z = new ParticleSystem.MinMaxCurve(0f);
            }
            if (fb.lengthScale > 0f)
            {
                var r = ps.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.Stretch;   // U=0 on the particle, the quad trails behind it
                r.lengthScale = fb.lengthScale;
                r.velocityScale = fb.velocityScale;
            }
            float fadeIn = At(fb.fade, 0), fadeOut = At(fb.fade, 1);
            if (fadeIn > 0f || fadeOut > 0f)
            {
                var g = new Gradient();
                g.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[]
                    {
                        new GradientAlphaKey(fadeIn > 0f ? 0f : 1f, 0f), new GradientAlphaKey(1f, Mathf.Max(fadeIn, 0.001f)),
                        new GradientAlphaKey(1f, Mathf.Min(1f - fadeOut, 0.999f)), new GradientAlphaKey(fadeOut > 0f ? 0f : 1f, 1f),
                    });
                var col = ps.colorOverLifetime;
                col.enabled = true;
                col.color = new ParticleSystem.MinMaxGradient(g);
            }
            float g0 = At(fb.grow, 0, 1f), g1 = At(fb.grow, 1, 1f);
            if (g0 != 1f || g1 != 1f)
            {
                SizeOverLife(ps, x => Mathf.Lerp(g0, g1, x));
            }

            float end = fb.delay + fb.life;
            for (int i = 0; i < fb.trail.Length; i++)
            {
                end = Mathf.Max(end, fb.delay + fb.life + BuildTrail(ps, layer, fb.sortingOrder - 1, fb.trail[i], _matFor(fb.trail[i].blend), spark, mote, colors, i, fb.trailBack));
            }
            return end;
        }

        /// <summary>Birth sub-emitter: a world-space stream of motes (or stretched needles) left behind each particle of
        /// <paramref name="owner"/> for its whole life. Returns the longest mote life (how long the stream outlives its owner).
        /// Unity orients a Birth sub-emitter's shape along the parent's velocity (shape +Z = direction of travel, shape +Y =
        /// world Z; measured in 6000.3), so the shape is a sphere and "behind" is shape -Z.</summary>
        private static float BuildTrail(ParticleSystem owner, string layer, int order, MoteSpec m, Material mat, Sprite spark, Sprite mote, Color[] colors, int index, float back)
        {
            // a Birth sub-emitter stays a child of its owner (Unity drives it from there), one level below 7.1's flat layout
            var ps = NewSystem(owner.transform, LayerName("trail", true, m.blend), mat, layer, order, MAX_PARTICLE_SIZE);
            UseSprites(ps, new[] { m.stretch ? spark : mote }, 1f);
            var main = ps.main;
            main.loop = true;              // a non-looping sub-emitter would stop emitting after its duration
            main.duration = 1f;
            main.playOnAwake = false;      // driven by the owner
            main.startLifetime = new ParticleSystem.MinMaxCurve(m.life[0], m.life[1]);
            main.startSpeed = new ParticleSystem.MinMaxCurve(m.speed[0], m.speed[1]);
            main.startSize = new ParticleSystem.MinMaxCurve(m.size[0], m.size[1]);
            main.gravityModifier = m.gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 96;
            var emission = ps.emission;
            emission.rateOverTime = m.rate;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = Mathf.Max(0.0001f, m.radius);
            shape.radiusThickness = 1f;
            shape.position = new Vector3(0f, 0f, -back);
            if (m.drag > 0f)
            {
                Drag(ps, m.drag);
            }
            if (m.noise > 0f)
            {
                var noise = ps.noise;
                noise.enabled = true;
                noise.strength = m.noise;
                noise.frequency = 0.8f;
                noise.scrollSpeed = 0.5f;
                noise.damping = true;
                noise.quality = ParticleSystemNoiseQuality.Medium;
            }
            if (m.stretch)
            {
                SizeOverLife(ps, x => Mathf.Lerp(1f, 0.3f, x));
                ColorOverLife(ps, colors, 0f);
                var r = ps.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.velocityScale = 0.04f;
                r.lengthScale = 1.4f;
            }
            else
            {
                SizeOverLife(ps, x => Mathf.Lerp(1f, 0.4f, x));
                ColorOverLife(ps, colors, 0.08f);
            }
            var subs = owner.subEmitters;
            subs.enabled = true;
            subs.AddSubEmitter(ps, ParticleSystemSubEmitterType.Birth, ParticleSystemSubEmitterProperties.InheritNothing);
            return m.life[1];
        }

        private static float At(float[] values, int i, float fallback = 0f)
        {
            return values != null && i < values.Length ? values[i] : fallback;
        }

        private static float BuildSparks(Transform parent, Spec spec, SparkSpec s, Material mat, Sprite sprite, Color[] colors)
        {
            var ps = NewSystem(parent, LayerName("spark", true, s.blend), mat, spec.sortingLayer, spec.sortingOrder + 2, MAX_PARTICLE_SIZE);
            UseSprites(ps, new[] { sprite }, 1f);
            var main = ps.main;
            main.startDelay = s.delay;
            main.duration = Mathf.Max(0.05f, s.life[1]);
            main.startLifetime = new ParticleSystem.MinMaxCurve(s.life[0], s.life[1]);
            main.startSpeed = new ParticleSystem.MinMaxCurve(s.speed[0], s.speed[1]);
            main.startSize = new ParticleSystem.MinMaxCurve(s.size[0], s.size[1]);
            main.gravityModifier = s.gravity;
            main.maxParticles = Mathf.Max(8, s.count * 2);
            Burst(ps, 0f, s.count);
            var shape = Circle(ps, s.radius);
            shape.arc = s.arc;
            if (s.arc < 360f)
            {
                shape.rotation = new Vector3(0f, 0f, -s.arc * 0.5f);   // fan centred on +X
            }
            Drag(ps, s.drag);
            SizeOverLife(ps, x => Mathf.Lerp(1f, 0.3f, x));
            ColorOverLife(ps, colors, 0f);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.04f;
            r.lengthScale = 1.4f;
            return s.delay + s.life[1];
        }

        private static float BuildMotes(Transform parent, Spec spec, MoteSpec m, Material mat, Sprite sprite, Color[] colors)
        {
            var ps = NewSystem(parent, LayerName(m.mode == "trail" ? "trail" : "glow", true, m.blend), mat, spec.sortingLayer, spec.sortingOrder + 3, MAX_PARTICLE_SIZE);
            UseSprites(ps, new[] { sprite }, 1f);
            var main = ps.main;
            main.startDelay = m.delay;
            main.startLifetime = new ParticleSystem.MinMaxCurve(m.life[0], m.life[1]);
            main.startSpeed = new ParticleSystem.MinMaxCurve(m.speed[0], m.speed[1]);
            main.startSize = new ParticleSystem.MinMaxCurve(m.size[0], m.size[1]);
            main.gravityModifier = m.gravity;
            main.maxParticles = 64;
            var emission = ps.emission;
            var shape = Circle(ps, m.radius);
            shape.scale = new Vector3(1f, m.scaleY, 1f);
            float end;
            switch (m.mode)
            {
                case "rise":
                    bool forever = spec.loop || m.duration <= 0f;
                    main.loop = forever;
                    main.duration = forever ? 1f : m.duration;
                    emission.rateOverTime = m.rate;
                    var vel = ps.velocityOverLifetime;
                    vel.enabled = true;
                    vel.space = ParticleSystemSimulationSpace.Local;
                    vel.x = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
                    vel.y = new ParticleSystem.MinMaxCurve(m.rise[0], m.rise[1]);
                    vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
                    end = forever ? 0f : m.delay + m.duration + m.life[1];
                    break;
                case "trail":
                    main.loop = true;
                    main.duration = 1f;
                    main.simulationSpace = ParticleSystemSimulationSpace.World;
                    emission.rateOverDistance = m.rate;
                    end = 0f;
                    break;
                default: // drift
                    main.duration = Mathf.Max(0.05f, m.life[1]);
                    Burst(ps, 0f, m.count);
                    end = m.delay + m.life[1];
                    break;
            }
            if (m.drag > 0f)
            {
                Drag(ps, m.drag);
            }
            if (m.noise > 0f)
            {
                var noise = ps.noise;
                noise.enabled = true;
                noise.strength = m.noise;
                noise.frequency = 0.8f;
                noise.scrollSpeed = 0.5f;
                noise.damping = true;
                noise.quality = ParticleSystemNoiseQuality.Medium;
            }
            SizeOverLife(ps, x => Mathf.Lerp(1f, 0.4f, x));
            ColorOverLife(ps, colors, 0.08f);
            return end;
        }

        #endregion

        #region Private - Particle helpers

        /// <summary>Control system of the root / containers (game-vfx 7.1): no emission, renderer off.</summary>
        private static ParticleSystem ControlSystem(GameObject go)
        {
            var ps = go.AddComponent<ParticleSystem>();
            Reset(ps, 1f);
            var main = ps.main;
            main.maxParticles = 1;
            var emission = ps.emission;
            emission.enabled = false;
            go.GetComponent<ParticleSystemRenderer>().enabled = false;
            return ps;
        }

        private static ParticleSystem NewSystem(Transform parent, string name, Material mat, string layer, int order, float maxParticleSize)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            Reset(ps, 1f);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.sortingLayerName = layer;
            r.sortingOrder = order;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.maxParticleSize = maxParticleSize;
            r.minParticleSize = 0f;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return ps;
        }

        private static void Reset(ParticleSystem ps, float duration)
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = duration;
            main.loop = false;
            main.playOnAwake = true;
            main.startDelay = 0f;
            main.startLifetime = 1f;
            main.startSpeed = 0f;
            main.startSize = 1f;
            main.startRotation = 0f;
            main.startColor = Color.white;
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 32;
            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 0f;
            var shape = ps.shape;
            shape.enabled = false;
        }

        private static void Burst(ParticleSystem ps, float time, int count)
        {
            var emission = ps.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(time, (short)count) });
        }

        private static ParticleSystem.ShapeModule Circle(ParticleSystem ps, float radius)
        {
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = Mathf.Max(0.0001f, radius);
            shape.radiusThickness = 1f;
            shape.arc = 360f;
            return shape;
        }

        private static void Drag(ParticleSystem ps, float drag)
        {
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 1000f;
            limit.drag = drag;
            limit.multiplyDragByParticleSize = false;
            limit.multiplyDragByParticleVelocity = false;
        }

        private static void SizeOverLife(ParticleSystem ps, Func<float, float> f)
        {
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, FromFunc(f));
        }

        /// <summary>Intro of a loop flipbook (its particle lives 600 s, so <paramref name="k"/> is a tiny fraction of the life):
        /// alpha 0 -> 1 and size 0.55 -> 1 with an ease-out over the first k of the life, then steady.</summary>
        private static void IntroOverLife(ParticleSystem ps, float k)
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, k), new GradientAlphaKey(1f, 1f) });
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(g);

            const float FROM = 0.55f;
            var keys = new Keyframe[CURVE_SAMPLES + 2];
            for (int i = 0; i <= CURVE_SAMPLES; i++)
            {
                float u = i / (float)CURVE_SAMPLES;
                float v = Mathf.Lerp(FROM, 1f, 1f - Mathf.Pow(1f - u, 3f));
                float d = (1f - FROM) * 3f * Mathf.Pow(1f - u, 2f) / k;
                keys[i] = new Keyframe(u * k, v, d, d);
            }
            keys[CURVE_SAMPLES + 1] = new Keyframe(1f, 1f, 0f, 0f);
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(keys));
        }

        /// <summary>White-hot -> palette colours; alpha holds then fades (optional fade-in for motes).</summary>
        private static void ColorOverLife(ParticleSystem ps, Color[] c, float fadeIn)
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(c[0], 0f), new GradientColorKey(c[1], 0.2f), new GradientColorKey(c[2], 0.6f), new GradientColorKey(c[3], 1f) },
                fadeIn > 0f
                    ? new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, fadeIn), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) }
                    : new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(g);
        }

        private static Color[] ParseColors(string[] hex)
        {
            var result = new Color[4];
            for (int i = 0; i < 4; i++)
            {
                result[i] = Color.white;
                if (hex != null && i < hex.Length)
                {
                    ColorUtility.TryParseHtmlString(hex[i], out result[i]);
                }
            }
            return result;
        }

        private static AnimationCurve FromFunc(Func<float, float> f)
        {
            const float EPS = 0.002f;
            var keys = new Keyframe[CURVE_SAMPLES + 1];
            for (int i = 0; i <= CURVE_SAMPLES; i++)
            {
                float t = i / (float)CURVE_SAMPLES;
                float a = Mathf.Max(t - EPS, 0f);
                float b = Mathf.Min(t + EPS, 1f);
                float d = (f(b) - f(a)) / (b - a);
                keys[i] = new Keyframe(t, f(t), d, d);
            }
            return new AnimationCurve(keys);
        }

        #endregion

        #region Private - Scene helpers

        private static Camera CreateCamera(Scene scene, float orthoSize, float camY, string background)
        {
            var go = new GameObject("Camera");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.position = new Vector3(0f, camY, -10f);
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = orthoSize;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = background != null && background.StartsWith("#") && ColorUtility.TryParseHtmlString(background, out var c)
                ? c
                : new Color(0.11f, 0.125f, 0.153f);
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 100f;
            return cam;
        }

        /// <summary>Tiles a ground sprite (when <paramref name="background"/> is a Sprite asset path) so effects are judged on
        /// the real gameplay ground; a "#rrggbb" background is the camera colour instead.</summary>
        private static void CreateGround(Scene scene, float orthoSize, string background)
        {
            if (string.IsNullOrEmpty(background) || background.StartsWith("#"))
            {
                return;
            }
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(background);
            if (sprite == null)
            {
                LastWarnings.Add("preview ground sprite not found: " + background);
                return;
            }
            var root = new GameObject("Ground");
            SceneManager.MoveGameObjectToScene(root, scene);
            var size = sprite.bounds.size;
            int nx = Mathf.CeilToInt(orthoSize * 2f / size.x) + 2;
            int ny = Mathf.CeilToInt(orthoSize * 2f / size.y) + 2;
            for (int x = 0; x < nx; x++)
            {
                for (int y = 0; y < ny; y++)
                {
                    var tile = new GameObject("Tile");
                    tile.transform.SetParent(root.transform, false);
                    tile.transform.localPosition = new Vector3((x - (nx - 1) * 0.5f) * size.x, (y - (ny - 1) * 0.5f) * size.y, 1f);
                    var sr = tile.AddComponent<SpriteRenderer>();
                    sr.sprite = sprite;
                    sr.sortingOrder = short.MinValue;
                }
            }
        }

        #endregion
    }
}
