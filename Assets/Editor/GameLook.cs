using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using BodycamHorror.Player;

namespace BodycamHorror.EditorTools
{
    /// <summary>
    /// The look every scene of the game starts from, worked out in the dark room: black stays black
    /// (no lifted, tinted shadows), washed-out colour a shade to the cold side, a little grain, a
    /// smeared lens in the bloom; black fog, next to no ambient fill, no sky and no reflections; and a
    /// camera that barely opens up in the dark, so darkness is not turned into grey.
    /// What a scene shows, it shows by its own lamps and the flashlight.
    /// Menu: BodycamHorror > Apply Game Look To Open Scene.
    /// </summary>
    public static class GameLook
    {
        public const string ProfilePath = "Assets/Settings/GameLookProfile.asset";
        const string TextureDir = "Assets/Textures/Environment";

        /// <summary>Most the camera may brighten a dark scene, in stops (AutoExposure.maxStops).</summary>
        public const float MaxStops = 0.5f;

        [MenuItem("BodycamHorror/Apply Game Look To Open Scene")]
        public static void ApplyToOpenScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            Apply();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("Game look applied to " + scene.path);
        }

        /// <summary>Grade, air and exposure for the scene that is open (not saved here).</summary>
        public static void Apply()
        {
            VolumeProfile profile = BuildProfile();

            Volume volume = null;
            foreach (Volume v in Object.FindObjectsByType<Volume>(FindObjectsInactive.Include))
                if (v.isGlobal && (!volume || v.priority < volume.priority)) volume = v;   // the scene's own, not a script's
            if (!volume)
            {
                volume = new GameObject("Global Volume").AddComponent<Volume>();
                volume.isGlobal = true;
            }
            volume.sharedProfile = profile;
            EditorUtility.SetDirty(volume);

            ApplyAtmosphere();

            foreach (AutoExposure exposure in Object.FindObjectsByType<AutoExposure>(FindObjectsInactive.Include))
            {
                exposure.maxStops = MaxStops;
                EditorUtility.SetDirty(exposure);
            }
        }

        /// <summary>No sky, black fog, a trace of neutral fill: just enough to tell a wall from the void.</summary>
        public static void ApplyAtmosphere()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = Color.black;
            RenderSettings.fogDensity = 0.036f;
            RenderSettings.skybox = null;
            RenderSettings.sun = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.004f, 0.004f, 0.004f);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = null;
            RenderSettings.reflectionIntensity = 0f;
        }

        /// <summary>The shared grade, rebuilt in place so scenes pointing at it keep their reference.</summary>
        public static VolumeProfile BuildProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (!profile)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }
            for (int i = profile.components.Count - 1; i >= 0; i--)
            {
                var old = profile.components[i];
                profile.components.RemoveAt(i);
                if (old) Object.DestroyImmediate(old, true);
            }

            Add<Tonemapping>(profile).mode.Override(TonemappingMode.ACES);

            var white = Add<WhiteBalance>(profile);
            white.temperature.Override(-4f);
            white.tint.Override(0f);

            var adjust = Add<ColorAdjustments>(profile);
            adjust.postExposure.Override(0.25f);
            adjust.contrast.Override(10f);
            adjust.saturation.Override(-26f);
            adjust.colorFilter.Override(new Color(0.985f, 0.99f, 1f));

            var lgg = Add<LiftGammaGain>(profile);
            lgg.lift.Override(new Vector4(1f, 1f, 1f, 0f));       // not lifted, not crushed: black is black, the dark greys stay
            lgg.gamma.Override(new Vector4(1f, 1f, 1f, 0f));
            lgg.gain.Override(new Vector4(1f, 1f, 0.98f, 0f));

            var split = Add<SplitToning>(profile);
            split.shadows.Override(new Color(0.5f, 0.5f, 0.5f));  // neutral
            split.highlights.Override(new Color(0.53f, 0.52f, 0.48f));   // whites a shade off clean
            split.balance.Override(-10f);

            var bloom = Add<Bloom>(profile);
            bloom.threshold.Override(0.85f);
            bloom.intensity.Override(0.9f);
            bloom.scatter.Override(0.78f);
            bloom.highQualityFiltering.Override(true);
            bloom.dirtTexture.Override(EnvironmentTextures.LensDirt(TextureDir));
            bloom.dirtIntensity.Override(2.5f);

            var grain = Add<FilmGrain>(profile);
            grain.type.Override(FilmGrainLookup.Medium1);
            grain.intensity.Override(0.07f);   // in the black, grain is the only thing left to see
            grain.response.Override(0.7f);

            var vignette = Add<Vignette>(profile);   // LensLook replaces it with the lens frame at runtime
            vignette.color.Override(Color.black);
            vignette.intensity.Override(0.38f);
            vignette.smoothness.Override(0.45f);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        static T Add<T>(VolumeProfile profile) where T : VolumeComponent
        {
            T component = profile.Add<T>(false);
            component.name = typeof(T).Name;
            component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }
    }
}
