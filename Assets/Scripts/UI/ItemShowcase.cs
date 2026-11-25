using System.Collections;
using System.Collections.Generic;
using BodycamHorror.Environment;
using BodycamHorror.Weapons;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BodycamHorror.UI
{
    /// <summary>
    /// The "you found it" screen: picking a gun, the flashlight or a key up freezes the game, the picture behind goes
    /// soft and dark, and the item turns slowly in a studio light in the middle of the screen - no words, only the thing.
    /// Any key or button puts it away (and is not passed on to the game).
    ///
    /// The item is a copy of the pickup's meshes (taken as <see cref="KeyPickup.Taken"/> / <see cref="WeaponPickup.Taken"/>
    /// fire, before the pickup is gone), shown on a little stage far under the level by a camera of its own, drawn over
    /// the player's camera. The stage: a backdrop quad holding the blurred last frame, a key light from the upper left,
    /// a cold rim light from behind, a faint fill. The item turns to show its broad side (a gun in profile, a key with
    /// its tag), unless the pickup says otherwise (<see cref="KeyPickup.showcaseEuler"/>, <see cref="WeaponPickup.showcaseEuler"/>).
    /// Added by itself at load; <see cref="Show"/> works for anything with renderers.
    /// </summary>
    public class ItemShowcase : MonoBehaviour
    {
        public static ItemShowcase Instance { get; private set; }
        /// <summary>On screen, or just put away and the key that did it still held: the game takes no input.</summary>
        public static bool Blocking => Instance && (Instance.open || Instance.waitRelease);

        static readonly Vector3 StagePos = new Vector3(0f, -3000f, 0f);
        const float Fov = 26f, Distance = 1.6f;
        const float MinShowTime = 0.45f;   // so the press that took it, or a mash, does not put it straight away

        bool open, waitRelease, closing;
        float age, fade;
        Transform stage, pivot, item;
        Camera cam;
        Renderer backdrop;
        Material backdropMat;
        RenderTexture grab, blurred;
        float timeScaleBefore = 1f;
        Quaternion pose;
        Vector3 offset;    // where the item sits on the stage (a shining light sits to the left of its beam)
        Vector3 beamDir;   // the item's own spot light, in its frame (zero: it has none)
        float beamAngle = 50f;
        Color beamColor = Color.white;
        static Mesh beamMesh;
        static Material beamMat;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (Instance) return;
            var go = new GameObject("ItemShowcase");
            DontDestroyOnLoad(go);
            go.AddComponent<ItemShowcase>();
        }

        void Awake()
        {
            Instance = this;
            KeyPickup.Taken += OnKey;
            WeaponPickup.Taken += OnWeapon;
        }

        void OnDestroy()
        {
            KeyPickup.Taken -= OnKey;
            WeaponPickup.Taken -= OnWeapon;
            if (open) Restore();
            if (grab) grab.Release();
            if (blurred) blurred.Release();
            if (Instance == this) Instance = null;
        }

        // keys sit further off than guns: close up, a little key filled the screen
        void OnKey(KeyPickup key, GameObject player) => Show(key.gameObject, key.showcaseEuler, 0.7f);
        void OnWeapon(WeaponPickup pickup, GameObject player)
        {
            // the weapon light is shown as it will be carried: fitted under the pistol
            if (pickup.weapon == WeaponPickup.Kind.PistolLight)
            {
                var fitted = PistolWithLight(pickup, player);
                if (fitted)
                {
                    Show(fitted, pickup.showcaseEuler);
                    Destroy(fitted);
                    return;
                }
            }
            Show(pickup.gameObject, pickup.showcaseEuler);
        }

        /// <summary>A throwaway copy of the player's pistol (its own model, meshes only) with the pickup's light model where
        /// the weapon light rides (muzzle + flashlightOffset, as BodycamFlashlight fits it).
        /// Null if the player has no pistol.</summary>
        static GameObject PistolWithLight(WeaponPickup pickup, GameObject player)
        {
            var shooting = player ? player.GetComponentInChildren<PlayerShooting>(true) : null;
            var gun = shooting ? shooting.FindWeapon<Pistol>() : null;
            var lightModel = pickup.transform.Find("Model");
            if (!gun || !gun.muzzle || !lightModel) return null;

            // built inactive, far below the level, with every script taken off before anything wakes up
            var holder = new GameObject("PistolWithLight");
            holder.SetActive(false);
            holder.transform.position = StagePos + Vector3.down * 50f;
            var copy = Instantiate(gun.gameObject, holder.transform, false);
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = Quaternion.identity;
            copy.transform.localScale = gun.transform.lossyScale;
            copy.SetActive(true);
            foreach (var b in copy.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(b);
            foreach (var l in copy.GetComponentsInChildren<Light>(true)) DestroyImmediate(l);
            var flash = FindPath(copy.transform, gun.transform, gun.muzzle);
            if (flash) foreach (var r in flash.GetComponentsInChildren<Renderer>(true)) r.enabled = false;   // no muzzle flash
            // in the hands the pistol is part of the arms' mesh; the gun keeps its own model switched off: show that
            var model = copy.transform.Find("Model");
            if (model) model.gameObject.SetActive(true);

            // the light rides at the muzzle + offset in the gun's own space (BodycamFlashlight puts it there on the
            // gun anchor, which sits where the gun is)
            var fitted = Instantiate(lightModel.gameObject, copy.transform, false);
            fitted.name = "WeaponLight";
            fitted.transform.localPosition = gun.muzzle.localPosition + gun.flashlightOffset;
            fitted.transform.localRotation = Quaternion.identity;
            fitted.transform.localScale = Vector3.one;
            foreach (var l in fitted.GetComponentsInChildren<Light>(true))
                if (l.type != LightType.Spot) DestroyImmediate(l);   // its glow and bounce stay with the pickup
            holder.SetActive(true);
            return holder;
        }

        /// <summary>The object under copyRoot at the same place as target is under sourceRoot.</summary>
        static Transform FindPath(Transform copyRoot, Transform sourceRoot, Transform target)
        {
            if (!target) return null;
            if (target == sourceRoot) return copyRoot;
            var names = new List<string>();
            for (var t = target; t && t != sourceRoot; t = t.parent) names.Insert(0, t.name);
            return copyRoot.Find(string.Join("/", names));
        }

        /// <summary>Shows a copy of <paramref name="source"/>'s meshes. <paramref name="euler"/>: its pose to the camera
        /// (zero: turned to show its broad side).</summary>
        public void Show(GameObject source, Vector3 euler = default, float size = 1f)
        {
            if (!source || open) return;
            BuildStage();
            if (item) Destroy(item.gameObject);
            item = CopyMeshes(source);
            if (!item) return;
            Frame(item, euler, size);
            AddBeams(item);
            StartCoroutine(Open());
        }

        IEnumerator Open()
        {
            // the frame the player was looking at, blurred, is the backdrop
            open = true;
            closing = false;
            age = 0f;
            fade = 0f;
            // the hands and gun are left out of that frame: taking a gun swaps the hands' pose that very frame, and
            // frozen there, half-drawn, they showed through the blur as broken arms
            var hidden = new List<Renderer>();
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
                if (r.enabled && (r.renderingLayerMask & Player.ViewModelLighting.ViewModelLayer) != 0) { r.enabled = false; hidden.Add(r); }
            yield return new WaitForEndOfFrame();
            Grab();
            foreach (var r in hidden) if (r) r.enabled = true;
            timeScaleBefore = Time.timeScale > 0f ? Time.timeScale : 1f;
            Time.timeScale = 0f;
            stage.gameObject.SetActive(true);
            cam.enabled = true;
        }

        void Update()
        {
            if (waitRelease && !AnyHeld()) waitRelease = false;
            if (!open) return;
            float dt = Time.unscaledDeltaTime;
            age += dt;
            fade = Mathf.MoveTowards(fade, closing ? 0f : 1f, dt * (closing ? 5f : 3f));

            if (pivot)
            {
                // a slow turn to and fro, and in from a little further at the start
                float sway = Mathf.Sin(age * 0.55f) * 16f;
                float tilt = Mathf.Sin(age * 0.37f + 1f) * 4f;
                pivot.localRotation = Quaternion.Euler(tilt, sway, 0f) * pose;
                float approach = 1f - Mathf.Pow(1f - Mathf.Clamp01(age / 0.6f), 3f);
                pivot.localPosition = offset + new Vector3(0f, 0f, Mathf.Lerp(0.35f, 0f, approach));
            }
            if (backdropMat) backdropMat.SetColor("_BaseColor", Color.Lerp(new Color(1f, 1f, 1f), new Color(0.32f, 0.33f, 0.34f), fade));

            if (!closing && age > MinShowTime && AnyPressed()) closing = true;
            if (closing && fade <= 0f) Close();
        }

        void Close()
        {
            Restore();
            waitRelease = true;   // the key that put it away is not the game's
        }

        void Restore()
        {
            open = false;
            closing = false;
            Time.timeScale = timeScaleBefore;
            if (cam) cam.enabled = false;
            if (stage) stage.gameObject.SetActive(false);
            if (item) Destroy(item.gameObject);
        }

        static bool AnyPressed()
        {
            if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame) return true;
            var m = Mouse.current;
            if (m != null && (m.leftButton.wasPressedThisFrame || m.rightButton.wasPressedThisFrame || m.middleButton.wasPressedThisFrame)) return true;
            var g = Gamepad.current;
            if (g != null)
                foreach (var c in g.allControls)
                    if (c is UnityEngine.InputSystem.Controls.ButtonControl b && b.wasPressedThisFrame && !b.synthetic) return true;
            return false;
        }

        static bool AnyHeld()
        {
            if (Keyboard.current != null && Keyboard.current.anyKey.isPressed) return true;
            var m = Mouse.current;
            if (m != null && (m.leftButton.isPressed || m.rightButton.isPressed || m.middleButton.isPressed)) return true;
            var g = Gamepad.current;
            if (g != null)
                foreach (var c in g.allControls)
                    if (c is UnityEngine.InputSystem.Controls.ButtonControl b && b.isPressed && !b.synthetic) return true;
            return false;
        }

        // ------------------------------------------------------------------ the backdrop: the last frame, blurred

        void Grab()
        {
            int w = Screen.width, h = Screen.height;
            if (!grab || grab.width != w || grab.height != h)
            {
                if (grab) grab.Release();
                grab = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
            }
            ScreenCapture.CaptureScreenshotIntoRenderTexture(grab);

            // down a few halvings (each a box blur through bilinear filtering) and back up: soft enough, cheap
            var chain = new List<RenderTexture>();
            RenderTexture src = grab;
            int cw = w, ch = h;
            for (int i = 0; i < 5; i++)
            {
                cw = Mathf.Max(8, cw / 2); ch = Mathf.Max(8, ch / 2);
                var rt = RenderTexture.GetTemporary(cw, ch, 0, RenderTextureFormat.ARGB32);
                rt.filterMode = FilterMode.Bilinear;
                Graphics.Blit(src, rt);
                chain.Add(rt);
                src = rt;
            }
            for (int i = chain.Count - 2; i >= 1; i--) { Graphics.Blit(src, chain[i]); src = chain[i]; }
            if (!blurred || blurred.width != chain[1].width || blurred.height != chain[1].height)
            {
                if (blurred) blurred.Release();
                blurred = new RenderTexture(chain[1].width, chain[1].height, 0, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Bilinear };
            }
            Graphics.Blit(src, blurred);
            foreach (var rt in chain) RenderTexture.ReleaseTemporary(rt);

            // a screen capture comes in upside down on some platforms
            bool flip = SystemInfo.graphicsUVStartsAtTop;
            backdropMat.SetTexture("_BaseMap", blurred);
            backdropMat.SetTextureScale("_BaseMap", new Vector2(1f, flip ? -1f : 1f));
            backdropMat.SetTextureOffset("_BaseMap", new Vector2(0f, flip ? 1f : 0f));
            FitBackdrop();
        }

        void FitBackdrop()
        {
            float d = 6f;
            float hgt = 2f * d * Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad) * 1.02f;
            backdrop.transform.localPosition = new Vector3(0f, 0f, d);
            backdrop.transform.localScale = new Vector3(hgt * cam.aspect, hgt, 1f);
        }

        // ------------------------------------------------------------------ the stage

        void BuildStage()
        {
            if (stage) return;
            stage = new GameObject("ShowcaseStage").transform;
            stage.SetParent(transform, false);
            stage.position = StagePos;

            var camGo = new GameObject("ShowcaseCamera");
            camGo.transform.SetParent(stage, false);
            camGo.transform.localPosition = new Vector3(0f, 0f, -Distance);
            cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = Fov;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 10f;
            cam.depth = 50f;   // over the player's camera
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = 1;   // Default only: not the player's body, not the view model
            cam.enabled = false;
            var data = cam.GetUniversalAdditionalCameraData();
            // no post-processing at all: bloom turned the key light's highlight on a glossy slide into a halo
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            cam.allowMSAA = true;
            cam.allowHDR = false;
            data.renderShadows = false;
            // the renderer without the bodycam lens (Assets/Editor/ItemShowcaseSetup.cs), if the pipeline has it
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                var list = urp.rendererDataList;
                for (int i = 0; i < list.Length; i++)
                    if (list[i] && list[i].name == "Showcase_Renderer") { data.SetRenderer(i); break; }
            }

            // the backdrop: an unlit quad far behind the item, filling the view
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Backdrop";
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(camGo.transform, false);
            backdrop = quad.GetComponent<Renderer>();
            backdropMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            backdrop.sharedMaterial = backdropMat;
            backdrop.shadowCastingMode = ShadowCastingMode.Off;

            pivot = new GameObject("Pivot").transform;
            pivot.SetParent(stage, false);

            // studio light: a warm-white key from the upper left and in front, a cold rim from behind right, a faint fill
            StageLight("Key", new Vector3(-0.9f, 1.1f, -0.9f), new Color(1f, 0.95f, 0.88f), 26f, 40f);
            StageLight("Rim", new Vector3(1.0f, 0.6f, 1.1f), new Color(0.65f, 0.78f, 1f), 16f, 40f);
            StageLight("Fill", new Vector3(0.6f, -0.4f, -1.4f), new Color(0.8f, 0.85f, 0.9f), 2.5f, 60f);
            stage.gameObject.SetActive(false);
        }

        void StageLight(string name, Vector3 local, Color color, float intensity, float angle)
        {
            var go = new GameObject(name);
            go.transform.SetParent(stage, false);
            go.transform.localPosition = local;
            go.transform.rotation = Quaternion.LookRotation(-local, Vector3.up);
            var l = go.AddComponent<Light>();
            l.type = LightType.Spot;
            l.color = color;
            l.intensity = intensity;
            l.range = 5f;
            l.spotAngle = angle;
            l.innerSpotAngle = angle * 0.4f;
            l.shadows = LightShadows.None;
            go.AddComponent<NoBounce>();
        }

        /// <summary>The source's static and skinned meshes, as they look now, rebuilt under a fresh object (no scripts,
        /// colliders, lights or particles come along).</summary>
        Transform CopyMeshes(GameObject source)
        {
            var root = new GameObject("ShowcaseItem").transform;
            var renderers = new List<Renderer>();
            foreach (var r in source.GetComponentsInChildren<Renderer>())
                if (r.enabled && !(r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer)) renderers.Add(r);

            // the item's own frame: that of its biggest mesh (a pickup lies turned any which way; the model is not)
            Transform frame = source.transform;
            float biggest = 0f;
            foreach (var r in renderers)
            {
                float s = r.bounds.size.sqrMagnitude;
                if (s > biggest) { biggest = s; frame = r.transform; }
            }
            Matrix4x4 toLocal = frame.worldToLocalMatrix;
            int count = 0;
            foreach (var r in renderers)
            {
                Mesh mesh = null;
                if (r is SkinnedMeshRenderer skinned) { mesh = new Mesh(); skinned.BakeMesh(mesh, true); }
                else if (r.TryGetComponent(out MeshFilter f)) mesh = f.sharedMesh;
                if (!mesh) continue;
                var part = new GameObject(r.name, typeof(MeshFilter), typeof(MeshRenderer));
                part.transform.SetParent(root, false);
                Matrix4x4 m = toLocal * r.transform.localToWorldMatrix;
                part.transform.localPosition = m.GetColumn(3);
                part.transform.localRotation = m.rotation;
                part.transform.localScale = r is SkinnedMeshRenderer ? Vector3.one : m.lossyScale;   // a baked skin is already scaled
                part.GetComponent<MeshFilter>().sharedMesh = mesh;
                var pr = part.GetComponent<MeshRenderer>();
                pr.sharedMaterials = r.sharedMaterials;
                pr.shadowCastingMode = ShadowCastingMode.Off;
                pr.renderingLayerMask = 1;   // lit by the stage lights, whatever layers the view model used
                part.layer = 0;
                count++;
            }
            if (count == 0) { Destroy(root.gameObject); return null; }

            // a spot light on the item (a flashlight left on) comes along as a beam: a soft cone of lit air (the level's
            // volumetric haze showed a hard-edged ellipse on this stage). The glow that marks a pickup in the dark
            // (a point light) does not come
            beamDir = Vector3.zero;
            foreach (var l in source.GetComponentsInChildren<Light>())
            {
                if (!l.enabled || l.type != LightType.Spot) continue;
                var go = new GameObject("Beam");
                go.transform.SetParent(root, false);
                Matrix4x4 m = toLocal * l.transform.localToWorldMatrix;
                go.transform.localPosition = m.GetColumn(3);
                go.transform.localRotation = m.rotation;
                beamAngle = l.spotAngle;
                beamColor = l.color;
                if (beamDir == Vector3.zero) beamDir = (m.rotation * Vector3.forward).normalized;
            }
            root.localScale = frame.lossyScale;
            return root;
        }

        /// <summary>A beam's cone, hung on its "Beam" mark once the item has its size: <see cref="BeamLength"/> m long
        /// on the stage whatever the item's scale.</summary>
        void AddBeams(Transform it)
        {
            const float BeamLength = 1.1f;
            if (!beamMesh) beamMesh = ConeMesh(BeamLength, Mathf.Tan(Mathf.Min(beamAngle, 80f) * 0.5f * Mathf.Deg2Rad) * BeamLength * 0.8f);
            if (!beamMat)
            {
                var shader = Shader.Find("BodycamHorror/Showcase Beam");
                if (!shader) return;
                beamMat = new Material(shader);
            }
            beamMat.SetColor("_Color", beamColor);
            foreach (Transform mark in it)
            {
                if (mark.name != "Beam") continue;
                var cone = new GameObject("Cone", typeof(MeshFilter), typeof(MeshRenderer));
                cone.transform.SetParent(mark, false);
                cone.transform.localScale = Vector3.one / Mathf.Max(mark.lossyScale.x, 0.0001f);
                cone.GetComponent<MeshFilter>().sharedMesh = beamMesh;
                var r = cone.GetComponent<MeshRenderer>();
                r.sharedMaterial = beamMat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                // and the light itself, faint, so the lens end of the item catches its own spill
                var spill = mark.gameObject.AddComponent<Light>();
                spill.type = LightType.Point;
                spill.color = beamColor;
                spill.intensity = 0.6f;
                spill.range = 0.25f;
                spill.shadows = LightShadows.None;
                mark.gameObject.AddComponent<NoBounce>();
            }
        }

        /// <summary>An open cone along +Z from its tip at the origin: alpha bright near the tip (after a short fade-in
        /// right at the lens), dying out towards the end.</summary>
        static Mesh ConeMesh(float length, float radius)
        {
            const int Around = 40, Along = 16;
            var verts = new List<Vector3>(); var normals = new List<Vector3>(); var colors = new List<Color>(); var tris = new List<int>();
            for (int j = 0; j <= Along; j++)
            {
                float t = j / (float)Along;
                float a = Mathf.Pow(1f - t, 2.4f) * Mathf.SmoothStep(0f, 1f, t / 0.06f);
                for (int i = 0; i <= Around; i++)
                {
                    float ang = i / (float)Around * Mathf.PI * 2f;
                    var dir = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f);
                    verts.Add(dir * (radius * Mathf.Max(t, 0.02f)) + Vector3.forward * (length * t));
                    normals.Add((dir - Vector3.forward * (radius / length)).normalized);
                    colors.Add(new Color(1f, 1f, 1f, a));
                }
            }
            for (int j = 0; j < Along; j++)
                for (int i = 0; i < Around; i++)
                {
                    int a = j * (Around + 1) + i, b = a + Around + 1;
                    tris.Add(a); tris.Add(b); tris.Add(a + 1);
                    tris.Add(a + 1); tris.Add(b); tris.Add(b + 1);
                }
            var mesh = new Mesh { name = "ShowcaseBeamCone" };
            mesh.SetVertices(verts); mesh.SetNormals(normals); mesh.SetColors(colors); mesh.SetTriangles(tris, 0);
            mesh.bounds = new Bounds(Vector3.forward * length * 0.5f, new Vector3(radius * 2f, radius * 2f, length));
            return mesh;
        }

        /// <summary>Centres the item on the pivot, sizes it to the frame and turns it to its pose.</summary>
        void Frame(Transform it, Vector3 euler, float sizeScale = 1f)
        {
            it.SetParent(pivot, false);
            it.localPosition = Vector3.zero;
            it.localRotation = Quaternion.identity;

            // bounds in the item's own space (the parts sit right under it)
            Bounds b = default; bool any = false;
            foreach (Transform part in it)
            {
                if (!part.TryGetComponent(out MeshFilter pf)) continue;   // a beam, not a part
                Bounds mb = pf.sharedMesh.bounds;
                Matrix4x4 m = Matrix4x4.TRS(part.localPosition, part.localRotation, part.localScale);
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = m.MultiplyPoint3x4(c);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            Vector3 size = Vector3.Scale(b.size, it.localScale);

            // the pose: given, or the long side across the screen and the thin side to the camera, slightly turned
            Quaternion turn;
            if (euler != Vector3.zero) turn = Quaternion.Euler(euler);
            else if (beamDir != Vector3.zero)
            {
                // a light that is on: turned to shine off to the right and a little away, so its beam crosses the screen
                Vector3 hint = Mathf.Abs(Vector3.Dot(beamDir, Vector3.up)) < 0.8f ? Vector3.up : Vector3.forward;
                Vector3 upLocal = Vector3.ProjectOnPlane(hint, beamDir).normalized;
                turn = Quaternion.LookRotation(new Vector3(1f, -0.12f, 0.45f).normalized, Vector3.up)
                     * Quaternion.Inverse(Quaternion.LookRotation(beamDir, upLocal));
            }
            else
            {
                int longest = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
                int thinnest = size.x <= size.y && size.x <= size.z ? 0 : size.y <= size.z ? 1 : 2;
                if (thinnest == longest) thinnest = (longest + 2) % 3;
                int third = 3 - longest - thinnest;
                turn = Quaternion.Euler(-8f, 18f, 8f) * BroadSide(Axis(thinnest), Axis(third));   // a three-quarter look, like a product shot
            }
            pose = turn;

            // fit: the longest side to most of the frame width (or height, on a tall screen)
            float viewH = 2f * Distance * Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad);
            float viewW = viewH * (cam ? cam.aspect : 16f / 9f);
            // its width and height on screen, as posed (the box's corners turned)
            Vector3 lo = Vector3.positiveInfinity, hi = Vector3.negativeInfinity;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = Vector3.Scale(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)), it.localScale);
                c = pose * c;
                lo = Vector3.Min(lo, c); hi = Vector3.Max(hi, c);
            }
            Vector3 onScreen = hi - lo;
            float fit = Mathf.Min(viewW * 0.6f / Mathf.Max(onScreen.x, 0.001f), viewH * 0.5f / Mathf.Max(onScreen.y, 0.001f));
            // a light that shines is shown small, off to the left: the picture is its beam as much as the thing
            bool beam = beamDir != Vector3.zero;
            if (beam) fit = Mathf.Min(fit, viewH * 0.16f / Mathf.Max(onScreen.y, 0.001f));
            fit *= sizeScale;
            offset = beam ? new Vector3(-viewW * 0.22f, viewH * 0.04f, 0f) : Vector3.zero;
            it.localScale *= fit;
            it.localPosition = -(Vector3.Scale(b.center, it.localScale));   // the middle of the item on the pivot
            pivot.localRotation = pose;
            pivot.localPosition = offset + new Vector3(0f, 0f, 0.35f);
        }

        static Vector3 Axis(int i) => i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;

        /// <summary>The rotation turning item axis <paramref name="face"/> towards the camera and <paramref name="up"/> (the
        /// model's own positive axis: models are built upright) to screen up; the long side then lies across the screen.</summary>
        static Quaternion BroadSide(Vector3 face, Vector3 up) => Quaternion.Inverse(Quaternion.LookRotation(-face, up));
    }
}
