using System.Collections.Generic;
using BodycamHorror.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BodycamHorror.EditorTools
{
    /// <summary>
    /// Builds Assets/Scenes/MainMenu.unity: a black camera, the menu (<see cref="MainMenu"/>) and its music,
    /// and puts it first in the build, with the Level right after it (the scene Start Game goes to).
    /// Menu: BodycamHorror > Build Main Menu Scene. Rebuilding writes over the scene.
    /// </summary>
    public static class MainMenuSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/MainMenu.unity";
        const string LevelPath = "Assets/Scenes/Level.unity";
        const string MusicPath = "Assets/Audio/Music/menu_psycho_crime_loop.wav";

        [MenuItem("BodycamHorror/Build Main Menu Scene")]
        public static void Build()
        {
            // a long music loop: streamed and compressed, not decoded into memory whole
            if (AssetImporter.GetAtPath(MusicPath) is AudioImporter importer)
            {
                var settings = importer.defaultSampleSettings;
                settings.loadType = AudioClipLoadType.Streaming;
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                settings.quality = 0.7f;
                importer.defaultSampleSettings = settings;
                importer.loadInBackground = true;
                importer.SaveAndReimport();
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = 0;   // nothing to draw: the menu is all IMGUI over black
            camGo.AddComponent<AudioListener>();

            var menuGo = new GameObject("MainMenu");
            var source = menuGo.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0f;
            var menu = menuGo.AddComponent<MainMenu>();
            menu.music = AssetDatabase.LoadAssetAtPath<AudioClip>(MusicPath);
            menu.firstScene = System.IO.Path.GetFileNameWithoutExtension(LevelPath);
            if (!menu.music) Debug.LogWarning("Main menu: no music at " + MusicPath);

            // no fog, no ambient light to speak of: plain black
            RenderSettings.fog = false;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;
            RenderSettings.skybox = null;

            EditorSceneManager.SaveScene(scene, ScenePath);
            SetBuildOrder();
            AssetDatabase.SaveAssets();
            Debug.Log("Main menu scene built: " + ScenePath);
        }

        /// <summary>The menu first, the level second, everything else as it was after them.</summary>
        static void SetBuildOrder()
        {
            var list = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(ScenePath, true),
                new EditorBuildSettingsScene(LevelPath, true),
            };
            foreach (var s in EditorBuildSettings.scenes)
                if (s.path != ScenePath && s.path != LevelPath) list.Add(s);
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
