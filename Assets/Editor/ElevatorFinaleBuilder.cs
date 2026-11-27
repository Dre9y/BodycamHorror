using System.Collections.Generic;
using BodycamHorror.Cutscenes;
using BodycamHorror.Player;
using BodycamHorror.Weapons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace BodycamHorror.EditorTools
{
    /// <summary>
    /// Builds Assets/Scenes/ElevatorFinale.unity, the final cutscene, from the Blender export
    /// Assets/Cutscenes/ElevatorFinale/ElevatorFinale.fbx (made by Blender/tools/elevator_finale_export.py
    /// from Blender/Elevator_Finale.blend). Sets up the model import (generic, camera, one take), URP
    /// materials for the lift and landing, the game's own materials for the infected and the arms, the
    /// lights, the grade and the fade, and an <see cref="ElevatorFinaleCutscene"/> that plays it.
    /// Menu: BodycamHorror > Build Elevator Finale.
    /// </summary>
    public static class ElevatorFinaleBuilder
    {
        const string ScenePath = "Assets/Scenes/ElevatorFinale.unity";
        const string Dir = "Assets/Cutscenes/ElevatorFinale";
        const string ModelPath = Dir + "/ElevatorFinale.fbx";
        const string MaterialDir = Dir + "/Materials";
        const string Env = "Assets/Textures/Environment";
        const string ArmsPrefabPath = "Assets/Pistol/PistolArms.prefab";
        const string LevelScenePath = "Assets/Scenes/Level.unity";

        /// <summary>
        /// Copies the player camera's settings from Level onto the cutscene camera: Camera (FOV, clipping),
        /// URP camera data, LensLook (frame / vignette, fisheye, focus; the debug menu's saved values still
        /// override those at run time, in Level and here alike) and AutoExposure. Returns Level's grade.
        /// </summary>
        static VolumeProfile CopyLevelCamera(Camera cam, LensLook lens, AutoExposure exposure)
        {
            var level = EditorSceneManager.OpenScene(LevelScenePath, OpenSceneMode.Additive);
            VolumeProfile grade = null;
            try
            {
                Camera src = null;
                foreach (var go in level.GetRootGameObjects())
                {
                    foreach (var c in go.GetComponentsInChildren<Camera>(true))
                        if (!src && c.GetComponentInParent<BodycamPlayerController>(true)) src = c;
                    foreach (var v in go.GetComponentsInChildren<Volume>(true))
                        if (!grade && v.isGlobal) grade = v.sharedProfile;
                }
                if (!src) { Debug.LogWarning("ElevatorFinale: no player camera in " + LevelScenePath); return grade; }
                Vector3 pos = cam.transform.localPosition;
                Quaternion rot = cam.transform.localRotation;
                Paste(src, cam);
                cam.transform.SetLocalPositionAndRotation(pos, rot);
                if (src.TryGetComponent(out UniversalAdditionalCameraData srcData)) Paste(srcData, cam.GetUniversalAdditionalCameraData());
                if (src.TryGetComponent(out LensLook srcLens)) Paste(srcLens, lens);
                if (src.TryGetComponent(out AutoExposure srcExposure)) Paste(srcExposure, exposure);
            }
            finally
            {
                EditorSceneManager.CloseScene(level, true);
            }
            return grade;
        }

        static void Paste(Component from, Component to)
        {
            if (UnityEditorInternal.ComponentUtility.CopyComponent(from))
                UnityEditorInternal.ComponentUtility.PasteComponentValues(to);
        }

        [MenuItem("BodycamHorror/Build Elevator Finale")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder(MaterialDir)) AssetDatabase.CreateFolder(Dir, "Materials");
            ConfigureModel();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.012f, 0.012f, 0.014f);
            RenderSettings.skybox = null;

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var root = (GameObject)PrefabUtility.InstantiatePrefab(model);
            root.name = "ElevatorFinale";
            if (!root.TryGetComponent(out Animator animator)) animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = null;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.applyRootMotion = false;
            foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                skin.updateWhenOffscreen = true;
            // walls for the gunshot acoustics to feel out (static meshes only; they ride the cab with it)
            foreach (var f in root.GetComponentsInChildren<MeshFilter>(true))
                if (!f.name.StartsWith("Muzzle_Flash") && !f.name.StartsWith("Blood_Splat"))
                    f.gameObject.AddComponent<MeshCollider>().sharedMesh = f.sharedMesh;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                if (r.name.StartsWith("Muzzle_Flash") || r.name.StartsWith("Shaft_UpperLeak") || r.name.StartsWith("Plafond_Glass"))
                    r.shadowCastingMode = ShadowCastingMode.Off;

            // camera: the imported one, with everything else as on the player's camera in Level (lens frame,
            // fisheye, focus, exposure, FOV, URP settings) and Level's grade
            var cam = root.GetComponentInChildren<Camera>(true);
            cam.tag = "MainCamera";
            cam.gameObject.AddComponent<AudioListener>();
            var lens = cam.gameObject.AddComponent<LensLook>();
            var exposure = cam.gameObject.AddComponent<AutoExposure>();
            VolumeProfile grade = CopyLevelCamera(cam, lens, exposure);
            cam.farClipPlane = Mathf.Min(cam.farClipPlane, 60f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;

            // lights (renderer pivots sit at the cab origin, so place by bounds)
            // the infected stays in the dark: the plafond lights the cab and the hands but not it (own layer,
            // left out of the light's culling mask); it only gets a faint spill, the weak landing bulb and the shot
            int shade = EnsureLayer(ShadeLayer);
            var infectedRenderers = new List<Renderer>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                if (r.name.StartsWith("Infected_")) { r.gameObject.layer = shade; infectedRenderers.Add(r); }
            Light cabLight = PointLight("Cab_Light", Find(root, "Plafond_Glass"), new Color(0.85f, 1f, 0.88f), 1.6f, 3.5f, -0.08f);
            cabLight.cullingMask = ~(1 << shade);
            Light spill = PointLight("Cab_Spill_OnInfected", Find(root, "Plafond_Glass"), new Color(0.85f, 1f, 0.88f), 0.12f, 3.5f, -0.08f);
            spill.cullingMask = 1 << shade;
            spill.shadows = LightShadows.None;
            Light landingLight = PointLight("Landing_Light", Find(root, "Bulb_Glass"), new Color(1f, 0.72f, 0.42f), 0.6f, 5f, -0.06f);
            // the player's hands as in the game: the PistolArms prefab sits on the camera exactly as on the game's
            // weapon holder (its head height below the eye, its view offset), plays the pistol clips on its hidden
            // DMC5 rig, and its BareArms ("Arms") reach for those hands by IK (CutsceneArms, in place of the game's
            // AnimatedHands). The FBX's own baked arms and gun are hidden.
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                if (r.name.StartsWith("DMC5_") || r.name.StartsWith("xd_") || r.name.StartsWith("Muzzle_Flash")) r.enabled = false;
            var armsModel = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ArmsPrefabPath));
            armsModel.name = "PlayerArms";
            var hands = armsModel.GetComponent<AnimatedHands>();
            armsModel.transform.SetParent(cam.transform, false);
            armsModel.transform.SetLocalPositionAndRotation(Vector3.down * hands.headHeight + hands.viewOffset, Quaternion.identity);
            armsModel.transform.localScale = Vector3.one;

            // muzzle flash on the visible gun: a card and a light at the muzzle, riding the gun's Base bone
            if (hands.idleClip) hands.idleClip.SampleAnimation(armsModel, 0f);
            Transform gunBase = null;
            foreach (var t in armsModel.GetComponentsInChildren<Transform>(true))
                if (t.name == "Base" && !t.IsChildOf(hands.armsRoot)) gunBase = t;
            Vector3 muzzlePoint = cam.transform.TransformPoint(hands.GunPosition + hands.GunRotation * (hands.muzzleLocal * hands.gunScale));
            var flashSrc = Find(root, "Muzzle_Flash");
            var flash = new GameObject("Muzzle_FlashFX");
            flash.transform.SetParent(gunBase, false);
            Mesh flashMesh = flashSrc.GetComponent<MeshFilter>().sharedMesh;
            Vector3 flashNormal = flashMesh.normals.Length > 0 ? flashMesh.normals[0] : Vector3.forward;
            flash.transform.SetPositionAndRotation(muzzlePoint, Quaternion.FromToRotation(flashNormal, -cam.transform.forward));
            flash.transform.localScale = Vector3.one;
            flash.AddComponent<MeshFilter>().sharedMesh = flashMesh;
            var flashRenderer = flash.AddComponent<MeshRenderer>();
            flashRenderer.sharedMaterial = flashSrc.GetComponent<MeshRenderer>().sharedMaterial;
            flashRenderer.shadowCastingMode = ShadowCastingMode.Off;
            flashRenderer.enabled = false;
            var muzzle = new GameObject("Muzzle_Light").AddComponent<Light>();
            muzzle.transform.SetParent(flash.transform, false);
            muzzle.type = LightType.Point; muzzle.color = new Color(1f, 0.75f, 0.4f); muzzle.range = 6f; muzzle.intensity = 0f;
            muzzle.shadows = LightShadows.None;
            var armsAnimator = armsModel.GetComponent<Animator>();
            armsAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            armsAnimator.applyRootMotion = false;
            hands.enabled = false;
            foreach (var skin in armsModel.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                skin.updateWhenOffscreen = true;
                skin.shadowCastingMode = ShadowCastingMode.Off;
            }
            var armsIK = armsModel.AddComponent<CutsceneArms>();
            armsIK.source = hands;
            armsIK.holder = cam.transform;

            // Level's grade (the profile every scene of the game shares)
            var volumeGo = new GameObject("Grade");
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = grade ? grade : AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/GameLookProfile.asset");

            // black fade at the end
            var canvasGo = new GameObject("Fade", typeof(Canvas), typeof(CanvasGroup));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 100;
            var black = new GameObject("Black", typeof(RectTransform), typeof(Image));
            black.transform.SetParent(canvasGo.transform, false);
            var rt = (RectTransform)black.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            black.GetComponent<Image>().color = Color.black;
            var group = canvasGo.GetComponent<CanvasGroup>();
            group.alpha = 0f; group.blocksRaycasts = false; group.interactable = false;

            var cut = root.AddComponent<ElevatorFinaleCutscene>();
            cut.clip = ClipNamed(AssetDatabase.LoadAllAssetsAtPath(ModelPath), "Scene");
            cut.cabLight = cabLight;
            cut.landingLight = landingLight;
            cut.muzzleLight = muzzle;
            cut.muzzleFlash = flashRenderer;
            cut.landingBulb = Find(root, "Bulb_Glass").GetComponent<Renderer>();
            cut.fade = group;
            cut.arms = armsAnimator;
            cut.viewCamera = cam.transform;
            cut.sounds = Sounds();
            cut.speed = Speed;
            cut.fadeStart = 15.3f;   // cutscene seconds: the cab climbs a few floors with the doors shut, then black
            cut.infectedRoot = Find(root, "Infected_Root");
            cut.infectedRenderers = infectedRenderers.ToArray();
            cut.ragdollModel = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Zombie/Slasher/ZombieSlasher.fbx");
            cut.ragdollMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Zombie/Slasher/ZombieSlasher.mat");
            cut.cabIntensity = 1.6f;
            cut.landingIntensity = 0.6f;

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("Elevator finale built: " + ScenePath);
        }

        // ------------------------------------------------------------------ sound

        const string LiftAudio = "Assets/Audio/Lift/";
        const string ShadeLayer = "CutsceneShade";

        /// <summary>Index of a user layer with that name, added in the first free slot if missing.</summary>
        static int EnsureLayer(string name)
        {
            int found = LayerMask.NameToLayer(name);
            if (found >= 0) return found;
            var tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tags.FindProperty("layers");
            for (int i = 8; i < 32; i++)
            {
                var slot = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(slot.stringValue)) continue;
                slot.stringValue = name;
                tags.ApplyModifiedProperties();
                return i;
            }
            Debug.LogWarning("ElevatorFinale: no free layer for " + name);
            return 0;
        }
        /// <summary>Playback speed of the cutscene (ElevatorFinaleCutscene.speed). Sounds play at their own rate,
        /// so a cue that must hit a later frame starts that much earlier in its clip.</summary>
        const float Speed = 1.15f;

        /// <summary>Where in a clip to start at <paramref name="startFrame"/> so its moment <paramref name="hitTime"/>
        /// lands on <paramref name="hitFrame"/>.</summary>
        static float Lead(float hitTime, int startFrame, int hitFrame) => Mathf.Max(0f, hitTime - (hitFrame - startFrame) / 30f / Speed);

        /// <summary>
        /// Cues in cutscene frames (30 fps). Lift recordings in Assets/Audio/Lift (CC0, see SOURCES.txt):
        /// lift_door_close.mp3 rolls for ~1.5 s and thumps shut at 2.0 s; lift_ride_ukrainian.mp3 is
        /// button + doors (0-3.6 s) then the ride; lift_ride_soviet.mp3 hums evenly from ~100 s on.
        /// </summary>
        static List<ElevatorFinaleCutscene.SoundCue> Sounds()
        {
            AudioClip door = Audio(LiftAudio + "lift_door_close.mp3");
            AudioClip ride = Audio(LiftAudio + "lift_ride_ukrainian.mp3");
            AudioClip hum = Audio(LiftAudio + "lift_ride_soviet.mp3");
            AudioClip slam = Audio(LiftAudio + "door_bang_reverb.mp3");     // single slams at 0.2 s and 1.95 s
            AudioClip bang = Audio(LiftAudio + "door_bang_metal.mp3");      // hits at ~0.7, 1.45, 3.0, 4.45, 6.0 s
            AudioClip rattle = Audio(LiftAudio + "door_rattle.mp3");        // a security door shaken hard from 10 s
            AudioClip strain = Audio("Assets/Audio/Ambience/creak_metallic_groan.mp3");
            AudioClip bend = Audio("Assets/Audio/Ambience/creak_bending_metal.mp3");
            var cues = new List<ElevatorFinaleCutscene.SoundCue>
            {
                Cue("Cab buzz", Audio("Assets/Audio/Ambience/hum_fluorescent.mp3"), 0, 0f, 0f, 0.22f, loop: true),
                // doors start at 15 and bite on the arm at 47: line the clip's thump (2.0 s) up with the bite
                Cue("Doors closing", door, 15, Lead(2.0f, 15, 47), 0f, 0.9f),
                Cue("Groan", Audio("Assets/Audio/Monster/zombie_groan.wav"), 40, 0f, 2.6f, 0.8f),

                // the arm slams through and the leaves bang on it; the motor keeps shoving, the arm thrashes
                Cue("Slam: arm in", slam, 46, 0.2f, 1.6f, 1f),
                Cue("Bang", bang, 58, 0.65f, 0.6f, 0.75f),
                Cue("Doors bite", door, 72, 1.85f, 0.6f, 0.55f),
                Cue("Bang", bang, 84, 1.4f, 0.6f, 0.7f),
                Cue("Slam", slam, 96, 1.95f, 1.3f, 0.7f),
                Cue("Doors bite", door, 104, 1.85f, 0.6f, 0.5f),
                Cue("Bang", bang, 112, 2.95f, 0.6f, 0.8f),
                Cue("Doors bite", door, 119, 1.85f, 0.6f, 0.6f),

                // both hands on the leaves: rattling, steel straining, a bang on every jerk apart
                Cue("Rattle", rattle, 124, 10f, 2.6f, 0.75f, fadeIn: 0.1f),
                Cue("Strain", strain, 130, 0.25f, 2.2f, 0.8f),
                Cue("Bang: jerk", bang, 137, 4.4f, 0.7f, 0.85f),
                Cue("Groan", Audio("Assets/Audio/Monster/zombie_groan_2.wav"), 140, 0f, 1.2f, 0.8f),
                Cue("Slam: jerk", slam, 151, 0.2f, 1.2f, 0.85f),
                Cue("Bend", bend, 155, 0.2f, 2.2f, 0.8f),
                Cue("Rattle", rattle, 160, 13.2f, 1.4f, 0.6f),
                Cue("Bang: wide open", bang, 175, 5.95f, 0.8f, 1f),
                Cue("Snarl", Audio("Assets/Audio/Monster/zombie_groan_3.wav"), 178, 0f, 0.65f, 0.9f),   // cut off by the shot
                Cue("Shot", Audio("Assets/Audio/Weapons/pistol_shot.wav"), 197, 0f, 0f, 1f, gunshot: true),
                // motor again at 214, shut at 238: thump on the shut
                Cue("Doors shut", door, 214, Lead(2.0f, 214, 238), 0f, 1f),
                // jolt at 300, then up
                Cue("Lift ride", ride, 298, 3.6f, 0f, 0.85f),
                Cue("Lift hum", hum, 296, 100f, 0f, 0.5f, fadeIn: 1.5f),
            };
            return cues;
        }

        static ElevatorFinaleCutscene.SoundCue Cue(string name, AudioClip clip, int frame, float from, float length,
                                                    float volume, bool loop = false, float fadeIn = 0f, bool gunshot = false)
            => new ElevatorFinaleCutscene.SoundCue
            {
                name = name, clip = clip, frame = frame, from = from, length = length, volume = volume,
                loop = loop, fadeIn = fadeIn, gunshot = gunshot, fadeOut = length > 0f ? 0.15f : 0.05f,
            };

        /// <summary>Loads a clip, set up so a cue can start it from the middle (mono, decoded on load).</summary>
        static AudioClip Audio(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (!importer) { Debug.LogWarning("ElevatorFinale: no " + path); return null; }
            var s = importer.defaultSampleSettings;
            if (path.StartsWith(LiftAudio) && (s.loadType != AudioClipLoadType.DecompressOnLoad || !importer.forceToMono))
            {
                importer.forceToMono = true;
                s.loadType = AudioClipLoadType.DecompressOnLoad;
                importer.defaultSampleSettings = s;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        static AnimationClip ClipNamed(Object[] assets, string name)
        {
            foreach (var a in assets)
                if (a is AnimationClip c && c.name == name) return c;
            return null;
        }

        static Transform Find(GameObject root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            Debug.LogWarning("ElevatorFinale: no " + name);
            return root.transform;
        }

        static Light PointLight(string name, Transform at, Color color, float intensity, float range, float drop)
        {
            var light = new GameObject(name).AddComponent<Light>();
            var r = at.GetComponent<Renderer>();
            Vector3 p = r ? r.bounds.center : at.position;
            light.transform.SetParent(at, false);
            light.transform.position = p + Vector3.up * drop;
            light.type = LightType.Point;
            light.color = color; light.intensity = intensity; light.range = range;
            light.shadows = LightShadows.Soft;
            light.shadowNearPlane = 0.05f;
            return light;
        }

        // ------------------------------------------------------------------ model + materials

        static void ConfigureModel()
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.importCameras = true;
            importer.importLights = false;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importAnimation = true;
            importer.resampleCurves = false;
            importer.animationCompression = ModelImporterAnimationCompression.Off;   // small door / camera moves matter
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            var clips = importer.defaultClipAnimations;
            foreach (var c in clips) { c.loopTime = false; c.name = "Scene"; }
            importer.clipAnimations = clips;

            foreach (var pair in Materials())
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), pair.Key), pair.Value);
            importer.SaveAndReimport();
        }

        static Dictionary<string, Material> Materials()
        {
            string tex = Dir + "/Textures/";
            Texture2D metalN = Tex(Env + "/Metal021/Metal021_NormalGL.jpg");
            var m = new Dictionary<string, Material>
            {
                ["Lift_Laminate"] = Lit("Lift_Laminate", Color.white, Tex(tex + "Lift_Laminate.png"), null, 0f, 0.55f),
                ["Lift_Linoleum"] = Lit("Lift_Linoleum", Color.white, Tex(tex + "Lift_Linoleum.png"), null, 0f, 0.3f),
                ["Lift_DoorPaint"] = Lit("Lift_DoorPaint", Color.white, Tex(tex + "Lift_DoorPaint.png"), metalN, 0.1f, 0.5f),
                ["Lift_Aluminium"] = Lit("Lift_Aluminium", new Color(0.7f, 0.7f, 0.68f), Tex(Env + "/Metal021/Metal021_Color.jpg"), metalN, 1f, 0.65f),
                ["Lift_PanelPlate"] = Lit("Lift_PanelPlate", new Color(0.35f, 0.35f, 0.33f), Tex(Env + "/Metal021/Metal021_Color.jpg"), metalN, 1f, 0.6f),
                ["Lift_Rubber"] = Lit("Lift_Rubber", new Color(0.02f, 0.02f, 0.02f), null, null, 0f, 0.1f),
                ["Lift_Button"] = Lit("Lift_Button", new Color(0.03f, 0.03f, 0.03f), null, null, 0f, 0.7f),
                ["Lift_ButtonRed"] = Lit("Lift_ButtonRed", new Color(0.6f, 0.02f, 0.01f), null, null, 0f, 0.7f),
                ["Lift_Label"] = Lit("Lift_Label", new Color(0.85f, 0.85f, 0.8f), null, null, 0f, 0.5f),
                ["Lift_Plafond"] = Emissive("Lift_Plafond", new Color(0.85f, 1f, 0.9f), 2.5f),
                ["Lift_Indicator"] = Emissive("Lift_Indicator", new Color(1f, 0.25f, 0.02f), 3f),
                ["Landing_GreenPaint"] = Scanned("Landing_GreenPaint", "PaintedPlaster011", new Color(0.22f, 0.42f, 0.30f), 0.45f),
                ["Landing_Whitewash"] = Scanned("Landing_Whitewash", "Plaster007", new Color(0.75f, 0.73f, 0.66f), 0.1f),
                ["Landing_Tiles"] = Scanned("Landing_Tiles", "Tiles110", new Color(0.75f, 0.6f, 0.5f), 0.4f),
                ["Shaft_Concrete"] = Scanned("Shaft_Concrete", "PaintedPlaster015", new Color(0.25f, 0.25f, 0.25f), 0.05f),
                ["Shaft_Rust"] = Scanned("Shaft_Rust", "Rust002", Color.white, 0.2f),
                ["Stair_Dark"] = Lit("Stair_Dark", new Color(0.01f, 0.01f, 0.01f), null, null, 0f, 0f),
                ["Shaft_LightLeak"] = Emissive("Shaft_LightLeak", new Color(1f, 0.75f, 0.45f), 4f),
                ["Landing_Bulb"] = Emissive("Landing_Bulb", new Color(1f, 0.7f, 0.35f), 4f),
                ["MuzzleFlash"] = Additive("MuzzleFlash", Tex("Assets/Textures/FX/MuzzleFlash.png")),
                ["BloodSplat.002"] = Decal("BloodSplat", Tex("Assets/Textures/ChainRoom/NowYouSeeMe_Blood.png")),
                ["ZombieSlasher"] = AssetDatabase.LoadAssetAtPath<Material>("Assets/Zombie/Slasher/ZombieSlasher.mat"),
                ["DMC5_Soldier_Gear_Mat"] = AssetDatabase.LoadAssetAtPath<Material>("Assets/Pistol/Materials/Pistol_Gear.mat"),
                ["DMC5_Soldier_Hands_Mat"] = AssetDatabase.LoadAssetAtPath<Material>("Assets/Pistol/Materials/Pistol_Hands.mat"),
                ["DMC5_Soldier_Shirt_Mat"] = AssetDatabase.LoadAssetAtPath<Material>("Assets/Pistol/Materials/Pistol_Shirt.mat"),
                ["None.001"] = AssetDatabase.LoadAssetAtPath<Material>("Assets/Pistol/Materials/Pistol_Gun.mat"),
            };
            AssetDatabase.SaveAssets();
            return m;
        }

        static Texture2D Tex(string path) => AssetDatabase.LoadAssetAtPath<Texture2D>(path);

        static Material Asset(string name, string shader)
        {
            string path = MaterialDir + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat)
            {
                mat = new Material(Shader.Find(shader));
                AssetDatabase.CreateAsset(mat, path);
            }
            else mat.shader = Shader.Find(shader);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material Lit(string name, Color color, Texture2D albedo, Texture2D normal, float metallic, float smoothness)
        {
            var mat = Asset(name, "Universal Render Pipeline/Lit");
            mat.SetColor("_BaseColor", color);
            mat.SetTexture("_BaseMap", albedo);
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Smoothness", smoothness);
            if (normal)
            {
                mat.SetTexture("_BumpMap", normal);
                mat.EnableKeyword("_NORMALMAP");
            }
            return mat;
        }

        /// <summary>Same textures as the game's scanned sets, own material so the shared tints stay as they are.</summary>
        static Material Scanned(string name, string id, Color tint, float smoothness)
        {
            string f = $"{Env}/{id}/{id}";
            var mat = Lit(name, tint, Tex(f + "_Color.jpg"), Tex(f + "_NormalGL.jpg"), 0f, smoothness);
            var mask = Tex(f + "_Mask.png");
            if (mask)
            {
                mat.SetTexture("_MetallicGlossMap", mask);
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                mat.SetTexture("_OcclusionMap", mask);
                mat.EnableKeyword("_OCCLUSIONMAP");
            }
            return mat;
        }

        static Material Emissive(string name, Color color, float strength)
        {
            var mat = Lit(name, color, null, null, 0f, 0.7f);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            mat.SetColor("_EmissionColor", color * strength);
            return mat;
        }

        static Material Additive(string name, Texture2D tex)
        {
            var mat = Asset(name, "Universal Render Pipeline/Particles/Unlit");
            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", new Color(1f, 0.85f, 0.6f, 1f));
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 2f);   // additive
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.One);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_Cull", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
            return mat;
        }

        static Material Decal(string name, Texture2D tex)
        {
            var mat = Lit(name, new Color(0.45f, 0.02f, 0.02f), tex, null, 0f, 0.75f);
            mat.SetFloat("_AlphaClip", 1f);
            mat.SetFloat("_Cutoff", 0.35f);
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.SetFloat("_Cull", 0f);
            return mat;
        }
    }
}
