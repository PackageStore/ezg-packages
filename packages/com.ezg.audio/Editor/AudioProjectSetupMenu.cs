using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace Ezg.Package.Audio.EditorTools
{
    /// <summary>
    /// Editor menu that scaffolds the game-side glue for the EZG Audio package:
    /// <c>Create &gt; Ezg &gt; Audio &gt; Project setup</c>.
    ///
    /// It writes three portable, ready-to-edit template scripts into
    /// <see cref="TARGET_DIR"/> — a startup bootstrap, an <c>ISoundSettings</c> bridge, and a
    /// <c>SoundConfig</c> clip catalog — so a developer dropping the package into a new project
    /// gets a working starting point. Existing files are detected and the user is asked to
    /// confirm before any of them is overwritten.
    ///
    /// After the generated scripts compile, it also creates a ready-to-fill
    /// <c>SoundConfig.asset</c> next to them (only if one does not already exist), so there is no
    /// separate "Create asset" step. The <c>SoundConfig</c> template therefore carries no
    /// <c>[CreateAssetMenu]</c> of its own.
    /// </summary>
    public static class AudioProjectSetupMenu
    {
        #region Fields

        private const string MENU_PATH = "Assets/Create/Ezg/Audio/Project setup";
        private const string TARGET_DIR = "Assets/_Project/Features/_Shared/AudioGame";
        private const int MENU_PRIORITY = 80;

        // The generated SoundConfig type lives in this namespace (see SOUND_CONFIG_TEMPLATE).
        private const string SOUND_CONFIG_TYPE = "Ezg.Game.Audio.SoundConfig";
        private const string SOUND_CONFIG_ASSET_PATH = TARGET_DIR + "/SoundConfig.asset";

        // SessionState survives the domain reload triggered by importing the new scripts, so we
        // can defer asset creation until the SoundConfig type is actually compiled and loadable.
        private const string PENDING_ASSET_KEY = "Ezg.Package.Audio.PendingSoundConfigAsset";

        private struct TemplateFile
        {
            public string Name;
            public string Content;
        }

        #endregion

        #region Public Methods

        [MenuItem(MENU_PATH, false, MENU_PRIORITY)]
        private static void GenerateProjectSetup()
        {
            var files = new List<TemplateFile>
            {
                new TemplateFile { Name = "GameAudioBootstrap.cs", Content = BOOTSTRAP_TEMPLATE },
                new TemplateFile { Name = "GameSoundSettings.cs", Content = SETTINGS_TEMPLATE },
                new TemplateFile { Name = "SoundConfig.cs", Content = SOUND_CONFIG_TEMPLATE },
            };

            Directory.CreateDirectory(TARGET_DIR);

            var existing = new List<string>();
            foreach (var file in files)
            {
                if (File.Exists(Path.Combine(TARGET_DIR, file.Name)))
                    existing.Add(file.Name);
            }

            if (existing.Count > 0)
            {
                bool overwrite = EditorUtility.DisplayDialog(
                    "EZG Audio — Project setup",
                    "These files already exist in\n" + TARGET_DIR + ":\n\n  - " +
                    string.Join("\n  - ", existing) +
                    "\n\nOverwrite them with fresh templates?",
                    "Overwrite",
                    "Cancel");

                if (!overwrite)
                {
                    Debug.Log("[EZG Audio] Project setup cancelled — no files were written.");
                    return;
                }
            }

            foreach (var file in files)
            {
                File.WriteAllText(Path.Combine(TARGET_DIR, file.Name), file.Content);
            }

            // Queue the SoundConfig asset for creation once the freshly written scripts compile.
            // The SoundConfig type does not exist in the current domain yet, so we cannot
            // instantiate it here — OnScriptsReloaded picks this up after the reload.
            SessionState.SetString(PENDING_ASSET_KEY, SOUND_CONFIG_ASSET_PATH);

            AssetDatabase.Refresh();
            Debug.Log("[EZG Audio] Project setup generated " + files.Count +
                      " template script(s) in " + TARGET_DIR +
                      ". A SoundConfig.asset will be created automatically after scripts compile. " +
                      "Next: edit GameSoundSettings to read/persist volumes from your own save system.");
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Runs after every script compilation. If <see cref="GenerateProjectSetup"/> queued a
        /// SoundConfig asset, the type is now compiled, so create the asset and clear the flag.
        /// </summary>
        [DidReloadScripts]
        private static void OnScriptsReloaded()
        {
            var assetPath = SessionState.GetString(PENDING_ASSET_KEY, string.Empty);
            if (string.IsNullOrEmpty(assetPath))
                return;

            // Clear first so a later failure cannot trap us in a retry loop on every reload.
            SessionState.EraseString(PENDING_ASSET_KEY);
            TryCreateSoundConfigAsset(assetPath);
        }

        private static void TryCreateSoundConfigAsset(string assetPath)
        {
            if (File.Exists(assetPath))
                return; // Respect an existing, possibly customized, asset.

            var type = FindLoadedType(SOUND_CONFIG_TYPE);
            if (type == null)
            {
                Debug.LogWarning("[EZG Audio] Generated scripts compiled but type '" + SOUND_CONFIG_TYPE +
                                 "' was not found — skipping SoundConfig.asset creation.");
                return;
            }

            var instance = ScriptableObject.CreateInstance(type);
            if (instance == null)
                return;

            var dir = Path.GetDirectoryName(assetPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            AssetDatabase.CreateAsset(instance, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[EZG Audio] Created " + assetPath + " — fill in the clips in the Inspector.");
        }

        private static Type FindLoadedType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullName, false);
                if (type != null)
                    return type;
            }

            return null;
        }

        #endregion

        #region Templates

        private const string BOOTSTRAP_TEMPLATE =
@"using Ezg.Package.Audio;
using UnityEngine;

namespace Ezg.Game.Audio
{
    /// <summary>
    /// Wires this game's <see cref=""GameSoundSettings""/> into the EZG Audio package and assigns
    /// the shared <see cref=""AudioService.Default""/> instance before any scene loads, so any
    /// script can call <c>AudioService.Default.PlaySound(clip)</c> / <c>PlayMusic(clip)</c>.
    ///
    /// Generated by: Create > Ezg > Audio > Project setup. Safe to edit.
    /// </summary>
    public static class GameAudioBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init()
        {
            var service = new AudioService(new GameSoundSettings());
            service.Initialize();     // creates DontDestroyOnLoad Music/Sound AudioSources
            service.UpdateVolumes();  // apply persisted volumes to the live sources
            AudioService.Default = service;
        }
    }
}
";

        private const string SETTINGS_TEMPLATE =
@"using Ezg.Package.Audio;
using UnityEngine;

namespace Ezg.Game.Audio
{
    /// <summary>
    /// Game-side bridge that lets the EZG Audio package read/persist volumes through your own
    /// data layer, without the package depending on it.
    ///
    /// This generated template is backed by PlayerPrefs as a placeholder so it compiles and
    /// runs immediately. Replace the body of the five methods with your real save system
    /// (e.g. PlayerDataManager.Settings) — keep the method signatures unchanged.
    /// </summary>
    public class GameSoundSettings : ISoundSettings
    {
        private const string MUSIC_KEY = ""ezg.audio.music"";
        private const string SOUND_KEY = ""ezg.audio.sound"";
        private const float DEFAULT_VOLUME = 1f;

        // TODO: [Developer] - swap PlayerPrefs for your own data layer.
        public float GetMusicVolume() => PlayerPrefs.GetFloat(MUSIC_KEY, DEFAULT_VOLUME);

        public float GetSoundVolume() => PlayerPrefs.GetFloat(SOUND_KEY, DEFAULT_VOLUME);

        public void SetMusicVolume(float value) => PlayerPrefs.SetFloat(MUSIC_KEY, value);

        public void SetSoundVolume(float value) => PlayerPrefs.SetFloat(SOUND_KEY, value);

        public void Save() => PlayerPrefs.Save();
    }
}
";

        private const string SOUND_CONFIG_TEMPLATE =
@"using UnityEngine;

namespace Ezg.Game.Audio
{
    /// <summary>
    /// Central catalog of this game's AudioClips. A SoundConfig.asset is created automatically by
    /// Create > Ezg > Audio > Project setup; fill in the clips in the Inspector, then reference it
    /// wherever you call <c>AudioService.Default.PlayMusic / PlaySound</c>.
    ///
    /// Generated template — add, rename, or remove fields to match your game's sounds.
    /// </summary>
    public class SoundConfig : ScriptableObject
    {
        [Header(""Music"")]
        public AudioClip[] MainMenuMusics;
        public AudioClip[] GameplayMusics;

        [Header(""UI"")]
        public AudioClip ButtonSelect;
        public AudioClip OpenPopup;
        public AudioClip ClosePopup;

        [Header(""Gameplay"")]
        public AudioClip RewardCollect;
        public AudioClip PurchaseSuccess;

        // TODO: [Developer] - add your own clip fields here.
    }
}
";

        #endregion
    }
}
