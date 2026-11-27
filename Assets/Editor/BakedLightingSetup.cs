using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BodycamHorror.EditorTools
{
    /// <summary>
    /// Gives the open scene real bounced light instead of the hand-placed fakes.
    ///
    /// Lamps alone only light what they hit, so the scene used to be held up by shadowless fill, glow and
    /// wall-wash lights (and <c>BounceLight</c> at runtime) that shine through walls and flatten everything.
    /// This turns fixed lamps into Mixed lights (direct light and shadows stay realtime, so flicker and
    /// swinging still work; the bounce is baked), bakes that bounce into Adaptive Probe Volumes (no lightmap
    /// UVs needed, so the generated meshes work as they are), adds a box-projected reflection probe per lit
    /// room and deletes the fake fill lights.
    ///
    /// Lamps a script switches off or drives (scares, pickups, the player's lights) stay Realtime: baked
    /// bounce would linger after they go dark. Safe to run again after the scene builders rebuild a level.
    /// </summary>
    public static class BakedLightingSetup
    {
        const string LightingFolder = "Assets/Settings/Lighting";

        /// <summary>
        /// Indirect multiplier on every baked lamp. The lamps were tuned with the fake fill lights adding most of
        /// the room light, so physically correct bounce alone left rooms darker than designed.
        /// Raise for brighter rooms, lower for a darker game.
        /// </summary>
        const float IndirectBoost = 1.0f;   // 1.5 read too bright for a horror game (user, 2026-11-27)

        /// <summary>
        /// The textures are much darker than real rooms (floors ~0.11 linear albedo, walls 0.09-0.23, furniture 0.03;
        /// real plaster is 0.3-0.6), so light barely bounces. The bake treats surfaces this much lighter; the textures
        /// on screen are unchanged.
        /// </summary>
        const float AlbedoBoost = 2.0f;

        [MenuItem("BodycamHorror/Lighting/Set Up Baked GI In Open Scene")]
        public static void SetUpOpenScene()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path)) { Debug.LogError("[BakedLighting] Save the scene first."); return; }

            EnableProbeVolumesInPipelines();
            var report = new List<string>();
            report.Add(SetUpLights(out var mixedLights));
            report.Add(SetUpStaticRenderers());
            report.Add(SetUpProbeVolume(scene.path));
            report.Add(SetUpReflectionProbes(mixedLights));
            SetUpLightingSettings(scene.name);

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[BakedLighting] " + string.Join("\n", report) + "\nNow bake: BodycamHorror/Lighting/Bake Open Scene (or Window > Rendering > Lighting > Generate Lighting).");
        }

        [MenuItem("BodycamHorror/Lighting/Bake Open Scene")]
        public static void Bake()
        {
            Lightmapping.BakeAsync();
        }

        // ---------------------------------------------------------------- pipeline

        static void EnableProbeVolumesInPipelines()
        {
            var assets = new HashSet<UniversalRenderPipelineAsset>();
            for (int i = 0; i < QualitySettings.count; i++)
                if (QualitySettings.GetRenderPipelineAssetAt(i) is UniversalRenderPipelineAsset a) assets.Add(a);
            if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset d) assets.Add(d);

            foreach (var asset in assets)
            {
                var so = new SerializedObject(asset);
                so.FindProperty("m_LightProbeSystem").intValue = 1;          // Adaptive Probe Volumes
                so.FindProperty("m_ProbeVolumeSHBands").intValue = 1;        // L1: enough for soft bounce, cheap
                so.FindProperty("m_ColorGradingMode").intValue = 1;          // HDR grading: ACES works on the full range
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
            }
        }

        // ---------------------------------------------------------------- lights

        static bool IsFakeFill(Light l)
        {
            string n = l.name;
            if (n == "WallWash") return true;                                             // wall lamps' fake spill
            if (n.StartsWith("Fill_") && l.GetComponent<Environment.NoBounce>()) return true; // room fill lights
            // the upward "glow" spot hung under each pendant lamp to light the ceiling
            if (n == "Glow" && l.type == LightType.Spot && l.transform.parent && l.transform.parent.name == "Pivot") return true;
            return false;
        }

        /// <summary>Lamps that a script switches off, dims to black, moves around or creates: their light must not be baked.</summary>
        static bool MustStayRealtime(Light l, HashSet<Light> referenced, List<Transform> switchedGroups)
        {
            if (referenced.Contains(l)) return true;   // a scare / pickup / weapon holds it and will switch it
            // whole groups switched on/off by an event (emergency power and the like)
            if (switchedGroups.Any(g => l.transform.IsChildOf(g))) return true;
            foreach (var mb in l.GetComponentsInParent<MonoBehaviour>(true))
            {
                if (!mb) continue;
                string t = mb.GetType().Name;
                if (t.Contains("Pickup") || t.Contains("Player") || t.Contains("Weapon") || t == "BurstingBulb" || t == "Firearm"
                    || t == "Pistol" || t == "Shotgun" || t == "Manthing" || t.Contains("Scare"))
                    return true;
                if (mb is Environment.LightFlicker f && f.kind == Environment.LightFlicker.Kind.Screen) return true; // TV: colour jumps
            }
            return false;
        }

        /// <summary>
        /// Lights any script holds directly, and lamps a script holds from outside (a Transform or GameObject that
        /// is not part of the script's own object): scares and events switch those off as a whole
        /// (PeekScare.alsoOff / burstLamp, LightFearEvent.emergencyLights). A script pointing into its own
        /// hierarchy (SwingingLamp, LightHaze) does not count.
        /// </summary>
        static HashSet<Light> LightsReferencedByScripts(out List<Transform> switchedGroups)
        {
            var set = new HashSet<Light>();
            switchedGroups = new List<Transform>();
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
            {
                if (!mb) continue;
                var so = new SerializedObject(mb);
                var it = so.GetIterator();
                while (it.NextVisible(true))
                {
                    if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                    var value = it.objectReferenceValue;
                    if (value is Light l) { set.Add(l); continue; }
                    Transform t = value is GameObject go ? go.transform : value as Transform;
                    if (!t || !t.gameObject.scene.IsValid()) continue;
                    if (t.IsChildOf(mb.transform) || mb.transform.IsChildOf(t)) continue;   // its own parts
                    if (t.GetComponentInChildren<Light>(true)) switchedGroups.Add(t);
                }
            }
            return set;
        }

        static string SetUpLights(out List<Light> mixed)
        {
            mixed = new List<Light>();
            int removed = 0, realtime = 0;
            var referenced = LightsReferencedByScripts(out var switchedGroups);

            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
            {
                if (l.type == LightType.Directional || l.type == LightType.Rectangle || l.type == LightType.Disc) continue;
                if (IsFakeFill(l) && !referenced.Contains(l))
                {
                    Undo.DestroyObjectImmediate(l.gameObject);
                    removed++;
                    continue;
                }

                if (MustStayRealtime(l, referenced, switchedGroups))
                {
                    if (l.lightmapBakeType != LightmapBakeType.Realtime)   // an earlier run may have baked it
                    {
                        Undo.RecordObject(l, "Baked lighting");
                        l.lightmapBakeType = LightmapBakeType.Realtime;
                        EditorUtility.SetDirty(l);
                    }
                    realtime++;
                    continue;
                }

                Undo.RecordObject(l, "Baked lighting");
                l.lightmapBakeType = LightmapBakeType.Mixed;
                l.bounceIntensity = IndirectBoost;
                // Spots get shadows: realtime shadows stop light leaking through walls, and the bake only
                // occludes the bounce for lights that cast shadows. Point lights stay shadowless: six shadow
                // maps each cost ~900 extra draw calls in the basement (measured), for bare bulbs in open rooms.
                if (l.type == LightType.Spot && l.shadows == LightShadows.None) l.shadows = LightShadows.Soft;
                if (l.type == LightType.Point) l.shadows = LightShadows.None;
                // the bake uses the bulb size for soft baked shadows
                l.shapeRadius = l.type == LightType.Point ? 0.04f : 0.06f;
                var data = l.GetComponent<UniversalAdditionalLightData>();
                if (data)
                {
                    // points cost six shadow maps: keep them small (the property setter is play-mode only)
                    var so = new SerializedObject(data);
                    var tier = so.FindProperty("m_AdditionalLightsShadowResolutionTier");
                    if (tier != null)
                    {
                        tier.intValue = l.type == LightType.Point
                            ? UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierLow
                            : UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierMedium;
                        so.ApplyModifiedProperties();
                    }
                }
                EditorUtility.SetDirty(l);
                mixed.Add(l);
            }
            return $"Lights: {mixed.Count} Mixed, {realtime} kept Realtime (scripted), {removed} fake fill/glow lights removed.";
        }

        // ---------------------------------------------------------------- geometry

        static bool Moves(Transform t)
        {
            if (t.GetComponentInParent<Rigidbody>(true) || t.GetComponentInParent<Animator>(true)) return true;
            foreach (var mb in t.GetComponentsInParent<MonoBehaviour>(true))
            {
                if (!mb) continue;
                string n = mb.GetType().Name;
                if (n.Contains("Door") || n.Contains("Swing") || n.Contains("Pickup") || n.Contains("Player") || n.Contains("Valve")
                    || n.Contains("Scare") || n.Contains("Enemy") || n == "Manthing" || n == "BurstingBulb")
                    return true;
            }
            return false;
        }

        static string SetUpStaticRenderers()
        {
            int contributors = 0;
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include))
            {
                var go = r.gameObject;
                var flags = GameObjectUtility.GetStaticEditorFlags(go);
                if ((flags & StaticEditorFlags.ContributeGI) == 0)
                {
                    // already static for batching/occlusion: it does not move, so let it take part in the bake
                    if (flags == 0 || Moves(go.transform)) continue;
                    GameObjectUtility.SetStaticEditorFlags(go, flags | StaticEditorFlags.ContributeGI);
                }
                Undo.RecordObject(r, "Baked lighting");
                // Lit from the probe volume: no lightmap UVs needed on the generated meshes.
                r.receiveGI = ReceiveGI.LightProbes;
                r.lightProbeUsage = LightProbeUsage.BlendProbes;
                contributors++;
            }
            return $"Static geometry: {contributors} renderers bake into the probes.";
        }

        // ---------------------------------------------------------------- probe volume

        static string SetUpProbeVolume(string scenePath)
        {
            var root = GameObject.Find("Adaptive Probe Volume");
            if (!root) root = new GameObject("Adaptive Probe Volume");
            var pv = root.GetComponent<ProbeVolume>() ?? root.AddComponent<ProbeVolume>();
            pv.mode = ProbeVolume.Mode.Scene;   // cover every static renderer in this scene
            pv.fillEmptySpaces = false;

            Directory.CreateDirectory(LightingFolder);
            string sceneName = Path.GetFileNameWithoutExtension(scenePath);
            string setPath = $"{LightingFolder}/{sceneName}_BakingSet.asset";
            var set = AssetDatabase.LoadAssetAtPath<ProbeVolumeBakingSet>(setPath);
            if (!set)
            {
                set = ScriptableObject.CreateInstance<ProbeVolumeBakingSet>();
                AssetDatabase.CreateAsset(set, setPath);
            }
            // Rooms are 2.5-4 m across: probes every 0.5 m near walls, sparser in open space.
            set.minDistanceBetweenProbes = 0.5f;
            set.simplificationLevels = 3;
            set.TryAddScene(AssetDatabase.AssetPathToGUID(scenePath));
            set.SetSceneBaking(AssetDatabase.AssetPathToGUID(scenePath), true);
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            return $"Probe volume: {setPath} (0.5 m spacing).";
        }

        // ---------------------------------------------------------------- reflections

        static string SetUpReflectionProbes(List<Light> lamps)
        {
            var parent = GameObject.Find("Reflection Probes");
            if (parent)
                for (int i = parent.transform.childCount - 1; i >= 0; i--)
                    Undo.DestroyObjectImmediate(parent.transform.GetChild(i).gameObject);
            else parent = new GameObject("Reflection Probes");

            int mask = ~((1 << 2) | LayerMaskOf("UI"));
            var centres = new List<Vector3>();
            foreach (var lamp in lamps)
            {
                Vector3 p = lamp.transform.position;
                if (!Physics.Raycast(p + Vector3.down * 0.2f, Vector3.down, out var floor, 8f, mask, QueryTriggerInteraction.Ignore)) continue;
                Vector3 c = floor.point + Vector3.up * 1.3f;   // eye height: where the reflections are seen from

                // Room box from rays in the six directions (rooms are boxes in this level).
                float Dist(Vector3 dir, float max) =>
                    Physics.Raycast(c, dir, out var h, max, mask, QueryTriggerInteraction.Ignore) ? h.distance : max;
                float xp = Dist(Vector3.right, 8f), xn = Dist(Vector3.left, 8f);
                float zp = Dist(Vector3.forward, 8f), zn = Dist(Vector3.back, 8f);
                float yp = Dist(Vector3.up, 5f), yn = 1.3f;
                var min = c - new Vector3(xn, yn, zn);
                var max = c + new Vector3(xp, yp, zp);
                var centre = (min + max) * 0.5f;
                if (centres.Any(o => (o - centre).sqrMagnitude < 1.5f * 1.5f)) continue;
                centres.Add(centre);

                var go = new GameObject("Probe_" + lamp.transform.root.name + "_" + (lamp.transform.parent && lamp.transform.parent.name == "Pivot" ? lamp.transform.parent.parent.name : lamp.transform.parent ? lamp.transform.parent.name : lamp.name));
                go.transform.SetParent(parent.transform, false);
                go.transform.position = c;
                var probe = go.AddComponent<ReflectionProbe>();
                probe.mode = ReflectionProbeMode.Baked;
                probe.boxProjection = true;
                probe.size = max - min + Vector3.one * 0.1f;
                probe.center = centre - c;
                probe.resolution = 128;
                probe.hdr = true;
                probe.blendDistance = 0.5f;
                probe.importance = 1;
                Undo.RegisterCreatedObjectUndo(go, "Baked lighting");
            }
            return $"Reflection probes: {centres.Count} box-projected room probes.";
        }

        static int LayerMaskOf(string layer)
        {
            int i = LayerMask.NameToLayer(layer);
            return i < 0 ? 0 : 1 << i;
        }

        // ---------------------------------------------------------------- lighting settings

        static void SetUpLightingSettings(string sceneName)
        {
            string path = $"{LightingFolder}/{sceneName}_Lighting.lighting";
            var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(path);
            if (!settings)
            {
                settings = new LightingSettings { name = sceneName + "_Lighting" };
                AssetDatabase.CreateAsset(settings, path);
            }
            settings.bakedGI = true;
            settings.realtimeGI = false;
            settings.mixedBakeMode = MixedLightingMode.IndirectOnly;   // direct light and shadows stay realtime
            settings.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
            settings.maxBounces = 3;
            settings.albedoBoost = AlbedoBoost;
            settings.directSampleCount = 32;
            settings.indirectSampleCount = 256;
            settings.environmentSampleCount = 64;
            settings.lightmapResolution = 10f;   // only probe-lit geometry here, so lightmaps stay unused
            EditorUtility.SetDirty(settings);
            Lightmapping.lightingSettings = settings;

            // Reflections were switched off (intensity 0): metal, glass and wet floors looked like plastic.
            RenderSettings.reflectionIntensity = 1f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            AssetDatabase.SaveAssets();
        }
    }
}
