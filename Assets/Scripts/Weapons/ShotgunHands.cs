using BodycamHorror.Player;
using UnityEngine;
using UnityEngine.Rendering;

namespace BodycamHorror.Weapons
{
    /// <summary>
    /// The shotgun rig: gun and arms play the same clips. Procedural weapon poses from
    /// <see cref="PlayerShooting"/> move the gun and hands, and the arms reach for them by IK. The reload
    /// clip is scrubbed by the shell-by-shell logic: one pass of it is looped for every shell, the
    /// opening and closing parts are played once.
    /// </summary>
    [DefaultExecutionOrder(65)]   // after PlayerShooting (-100) and the free-hand pose (50), before the weapon light (70)
    public class ShotgunHands : MonoBehaviour
    {
        [Header("Rig parts")]
        [Tooltip("The rig that plays the clips.")]
        public Transform sourceRoot;
        public Animator sourceAnimator;
        public AnimationClip sourceIdleClip;

        [Header("Gun in the Idle clip (weapon-holder space, measured from the meshes)")]
        public Vector3 gunPosition;
        [Tooltip("Rotation of the gun: sight line along +Z, up along +Y.")]
        public Vector3 gunEuler;
        [Tooltip("Where the pellets leave, in gun space.")]
        public Vector3 muzzleLocal;
        [Tooltip("Height of the sight line above the weapon origin.")]
        public float sightHeight = 0.06f;

        [Header("Gun model (empty = the one the clips were made with)")]
        [Tooltip("A different gun to show in the hands. It rides the clips' bones: the body on Body, the part named below on Slide (the pump).")]
        public GameObject modelPrefab;
        [Tooltip("Where the model sits in gun space (sight line along +Z, up along +Y), at gun scale 1.")]
        public Vector3 modelPosition;
        public Vector3 modelEuler;
        public float modelScale = 1f;
        [Tooltip("Child of the model that slides with the pump.")]
        public string modelPumpPart = "Spas_Pump";

        [Header("Arms")]
        [Tooltip("Size of the arms. The whole rig grows with them, clips and all, so the hands stay on the gun; the gun's own size is armsScale x gunScale.")]
        [Range(1f, 1.6f)] public float armsScale = 1f;

        [Header("Grip (fitting the clips' hands to another gun)")]
        [Tooltip("Turns the right hand about its palm, in gun space (x pitch, y yaw, z roll): a pistol grip is held more upright than a stock's wrist.")]
        public Vector3 rightHandEuler;
        [Tooltip("Turns the left hand about its palm while it holds the fore-end, in gun space.")]
        public Vector3 leftHandEuler;
        [Tooltip("Left hand on the fore-end: how far the fingers wrap round it (0 = as the clips hold them).")]
        [Range(0f, 1f)] public float leftGripWeight;
        [Range(0f, 1f)] public float leftThumbCurl = 0.3f;
        [Range(0f, 1f)] public float leftFingerCurl = 0.7f;

        [Header("Left hand cradling the fore-end (needs the model)")]
        [Tooltip("Hold the fore-end like a cylinder: palm under it, thumb up the left side, fingers round the right. Off = the clips' grip turned by leftHandEuler.")]
        public bool leftCradle;
        [Tooltip("Middle of the palm, in the model's own space (on the fore-end part, so it rides the pump stroke).")]
        public Vector3 leftCradlePoint;
        [Tooltip("Fingers point this far round from straight ahead towards the right side (degrees).")]
        [Range(0f, 90f)] public float leftCradleFingerAngle = 45f;
        [Tooltip("Rolls the hand so the palm faces a little to the right side as well as up (degrees).")]
        [Range(-45f, 45f)] public float leftCradleRoll = 15f;

        [Header("Reload")]
        [Tooltip("Moves the shell and the loading hand while reloading, in gun space at gun scale 1: from where the clips' gun takes its shells to this gun's loading port.")]
        public Vector3 reloadShellOffset;
        [Tooltip("Size of the shell in the hand against the clips' own (which grow with the gun).")]
        [Range(0.5f, 2f)] public float shellSize = 1f;
        [Tooltip("The shell rides the loading hand, pinched between thumb and fingers, instead of following the clip on its own.")]
        public bool shellInHand;
        [Tooltip("Where the shell's middle sits against the hand: 0 at the thumb tip, 1 at the middle joint of the index finger.")]
        [Range(0f, 1f)] public float shellPinch = 0.5f;
        [Tooltip("Moves the shell out of the pinch, in hand space (m): it should stick out past the thumb so it reads as held.")]
        public Vector3 shellPinchOffset;
        [Tooltip("Turns the shell in the hand (degrees, hand space).")]
        public Vector3 shellPinchEuler;
        [Tooltip("Loading port: middle of the opening under the receiver, in the model's own space.")]
        public Vector3 loadingPort;
        [Tooltip("Height (model space) of the magazine tube's axis the shell goes up into and slides forward along.")]
        public float loadingTubeHeight;
        [Tooltip("How far (model units) the shell slides forward into the tube before it is gone.")]
        public float loadingSlide = 0.05f;
        [Tooltip("Nose-up tilt of the shell as it goes into the port (degrees).")]
        public float loadingTilt = 12f;
        [Tooltip("Share of each pass at which the hand starts lining the shell up under the port, and at which it starts pushing it in. "
                 + "It is in by shellInsertShare.")]
        public Vector2 loadingPhases = new Vector2(0.6f, 0.76f);
        [Tooltip("Model-space height the loading hand's palm may not rise above under the receiver (keeps the fingers out of the gun). Off below -1.")]
        public float loadingPalmCeiling = -10f;
        [Tooltip("Model-space length range of the receiver that ceiling applies under (x from, x to).")]
        public Vector2 loadingPortSpan = new Vector2(-0.26f, 0.02f);

        [Header("Trigger finger")]
        [Tooltip("Front face of the trigger, in the model's own space: the pad of the index finger is curled onto it.")]
        public Vector3 rightTriggerPoint;
        [Tooltip("0 = the clips' finger, 1 = curled onto the trigger.")]
        [Range(0f, 1f)] public float rightTriggerWeight;

        [Header("Sights")]
        [Tooltip("How far the line from the rear sight notch to the front sight dips below the barrel (degrees): down the sights the gun is raised by it.")]
        public float sightPitch;

        [Header("Placement")]
        [Tooltip("Size of the gun against the arms, grown around the right palm so the grip stays where it is.")]
        public float gunScale = 1.25f;
        [Tooltip("Where the gun is carried relative to the clips' pose: low and to the right, butt in the shoulder.")]
        public Vector3 readyOffset = new Vector3(0.0676f, -0.1253f, -0.144f);
        [Tooltip("Distance of the gun from the eye when aiming.")]
        public float aimDistance = 0.25f;
        [Tooltip("How far below the aim point the gun is held when aiming, so the target stays in view.")]
        public float aimDrop = 0.08f;
        [Tooltip("How far right of the aim point the gun is held when aiming.")]
        public float aimSide = 0.03f;
        [Tooltip("Carried low on the run (weapon-holder space).")]
        public Vector3 sprintPosition = new Vector3(0.14f, -0.27f, 0.09f);
        public Vector3 sprintEuler = new Vector3(20f, -14f, 10f);
        [Tooltip("Put away: low and out of frame, without tipping the butt up into the lens.")]
        public Vector3 holsterPosition = new Vector3(0.2f, -0.6f, 0.1f);
        public Vector3 holsterEuler = new Vector3(18f, -12f, 12f);
        [Tooltip("Lifted towards the middle of the view while loading, so the hand and the shells are in frame.")]
        public Vector3 reloadPosition = new Vector3(0.08f, -0.17f, 0.26f);
        public Vector3 reloadEuler = new Vector3(-8f, -10f, 18f);
        [Tooltip("Moves the left hand on the pump, in gun space.")]
        public Vector3 leftHandOffset = new Vector3(0f, 0.016f, 0f);
        [Tooltip("Moves the arms when aiming (weapon-holder space), so the left forearm comes to the fore-end from the side and not from under it.")]
        public Vector3 aimArmsOffset = new Vector3(-0.1f, 0.16f, 0.03f);
        [Tooltip("Climbing: how far out to the side the left elbow points, against how far down (m from the shoulder).")]
        public float climbElbowOut = 1f;
        public float climbElbowDown = 0.15f;
        [Tooltip("Climbing: moves the left shoulder on top of the shared climbing shift (weapon-holder space).")]
        public Vector3 climbLeftShoulder = new Vector3(-0.2f, 0f, 0f);
        [Tooltip("Shoulders in weapon-holder space.")]
        public Vector3 leftShoulder = new Vector3(-0.13f, -0.3f, 0.05f);
        public Vector3 rightShoulder = new Vector3(0.24f, -0.3f, -0.18f);

        [Header("Reload clip timeline (s)")]
        [Tooltip("End of the opening: the support hand is about to leave the pump.")]
        public float enterEnd = 0.07f;
        [Tooltip("End of the first shell's pass.")]
        public float firstShellEnd = 1.38f;
        [Tooltip("Length of one pass: hand to the belt, shell in, hand back.")]
        public float shellCycle = 1.31f;
        [Tooltip("Where the closing part starts.")]
        public float exitStart = 5.31f;
        [Tooltip("From the closing part's start until the pump is racked.")]
        public float exitToRack = 0.62f;
        [Tooltip("Clip length.")]
        public float reloadLength = 6.82f;
        [Tooltip("Reload playback speed against the clip.")]
        public float reloadRate = 1.3f;
        [Tooltip("Share of a pass at which the shell goes in.")]
        [Range(0f, 1f)] public float shellInsertShare = 0.9f;

        [Header("Firing")]
        [Tooltip("Time from the shot until the pump is finished and it can fire again.")]
        public float shotRecovery = 0.75f;
        [Tooltip("Time from the shot until the pump starts.")]
        public float pumpDelay = 0.33f;
        public float pumpTime = 0.3f;

        [Header("Moving")]
        [Tooltip("Above this speed the walk clip plays.")]
        public float walkSpeed = 0.6f;

        [Header("IK")]
        [Tooltip("How far the whole rig may move when a hand target is out of range (m).")]
        public float maxReachShift = 0.55f;

        /// <summary>Where the clips hold the gun; the procedural poses are measured against this.
        /// With bigger arms the clips are bigger too, and hold it further out.</summary>
        public Vector3 GunPosition => bound ? clipGunPosition : gunPosition;
        /// <summary>Where the gun sits at the ready.</summary>
        public Vector3 ReadyPosition => gunPosition + readyOffset;
        /// <summary>Where the gun sits when aiming.</summary>
        public Vector3 AimPosition => new Vector3(aimSide, -SightHeight - aimDrop, aimDistance);
        // the gun grows around the right palm, so its points move away from the palm, not from the gun origin
        // gun-space numbers are measured at arms scale 1: the rig, and so the clips' gun, is armsScale bigger
        public float SightHeight => gripLocal.y + (sightHeight * armsScale - gripLocal.y) * gunScale;
        public Vector3 MuzzleLocal => gripLocal + (muzzleLocal * armsScale - gripLocal) * gunScale;
        public Quaternion GunRotation => Quaternion.Euler(gunEuler);
        public float IdleCant => Mathf.DeltaAngle(0f, gunEuler.z);
        public Transform GunAnchor { get; private set; }
        public float EnterTime => enterEnd / reloadRate;
        public float ShellTime => shellCycle / reloadRate;
        public float ExitTime => exitToRack / reloadRate;
        public float ExitExtraTime => (reloadLength - exitStart - exitToRack) / reloadRate;

        Firearm gun;
        PlayerShooting shooting;
        BodycamPlayerController player;
        Transform holder, freeLeftHand, gunBody, shell, pump, modelRoot, modelPump;
        Quaternion shellRelRot = Quaternion.identity;   // the shell against the loading hand, from the reload clip
        Vector3 shellCenterLocal;                       // the shell mesh's middle in the shell bone's space
        Vector3 shellAxisLocal = Vector3.up;            // the shell's long axis in its bone's space, towards the nose
        float shellHalfLocal = 0.03f;
        Vector3 pinchInHand;                            // thumb-and-index pinch point against the loading hand, hand rotation space
        Vector3 thumbInHand;                            // thumb tip against the loading hand, hand rotation space
        Vector3 loadCenter, loadDir;                    // the shell on its way into the port this frame
        Vector3 bodyRestPos;                            // the gun body in the Idle clip's first frame, weapon-holder space
        Quaternion bodyRestRot = Quaternion.identity;
        RiggedArms otherArms;
        PlaceholderArms pose;
        ArmRig rig;
        Vector3 rootBase, bodyScale, shellScale, pumpPalm, gripLocal, clipGunPosition;
        readonly Quaternion[] fingerScratch = new Quaternion[ArmRig.FingerCount * ArmRig.JointsPerFinger];
        readonly float[] curl = new float[ArmRig.FingerCount];
        bool bound;
        Quaternion pumpHand;
        float pumpHold = 1f, shotUntil;
        SkinnedMeshRenderer shellRenderer;
        float leftFree, leftOpen;
        bool walking, wasReloading, ready;

        /// <summary>Hooks the rig up to its weapon. Called once by the weapon that spawned it.</summary>
        public void Bind(Firearm weapon, BodycamPlayerController owner, PlayerShooting shooter)
        {
            gun = weapon;
            player = owner;
            shooting = shooter;
            holder = transform.parent;

            sourceAnimator.applyRootMotion = false;
            sourceAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            foreach (var t in GetComponentsInChildren<Transform>(true)) t.gameObject.layer = weapon.gameObject.layer;
            foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                smr.updateWhenOffscreen = true;
                smr.shadowCastingMode = ShadowCastingMode.Off;
            }
            ViewModelLighting.MarkViewModel(gameObject);

            // bigger arms: the whole rig grows about its own origin, so the clips' gun is held further out
            Vector3 rigOrigin = holder.InverseTransformPoint(sourceRoot.position);
            sourceRoot.localScale *= armsScale;
            clipGunPosition = rigOrigin + (gunPosition - rigOrigin) * armsScale;
            bound = true;

            if (sourceIdleClip) sourceIdleClip.SampleAnimation(sourceRoot.gameObject, 0f);
            rig = new ArmRig(sourceRoot);
            rootBase = sourceRoot.localPosition;

            gunBody = SourceBone("Body");
            bodyRestPos = holder.InverseTransformPoint(gunBody.position);
            bodyRestRot = Quaternion.Inverse(holder.rotation) * gunBody.rotation;
            shell = SourceBone("12ge_low");
            bodyScale = gunBody.localScale;
            shellScale = shell.localScale;

            // the grip on the fore-end, taken from the rest pose
            pump = SourceBone("Slide");
            ArmRig.Arm support = rig.Left;
            Quaternion toPump = Quaternion.Inverse(pump.rotation);
            pumpHand = toPump * support.hand.rotation;
            pumpPalm = toPump * (support.hand.position + support.hand.rotation * support.palmCenter - pump.position);

            foreach (var smr in sourceRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr.name == "12ge_low") shellRenderer = smr;

            // the body bone carries the file's x100 scale: keep the mount point itself at scale 1
            GunAnchor = new GameObject("GunAnchor").transform;
            GunAnchor.SetParent(gunBody, false);
            GunAnchor.localScale = Vector3.one / gunBody.lossyScale.x;
            GunAnchor.SetPositionAndRotation(holder.TransformPoint(GunPosition), holder.rotation * GunRotation);
            gripLocal = GunAnchor.InverseTransformPoint(rig.Right.hand.position + rig.Right.hand.rotation * rig.Right.palmCenter);
            if (modelPrefab) FitModel(weapon);
            if (shellInHand) MeasureShellInHand();

            otherArms = owner ? owner.GetComponentInChildren<RiggedArms>(true) : null;
            pose = owner ? owner.GetComponentInChildren<PlaceholderArms>(true) : null;

            weapon.Fired += OnFired;
            ready = true;
            OnEnable();
        }

        /// <summary>Puts the other gun into the hands in place of the clips' own: placed in gun space while
        /// the rig is in its rest pose, then hung on the clips' bones so every clip moves it, the pump
        /// stroke included, and it grows with the gun.</summary>
        void FitModel(Firearm weapon)
        {
            var model = Instantiate(modelPrefab, GunAnchor, false);
            model.name = modelPrefab.name;
            model.transform.localPosition = modelPosition * armsScale;
            model.transform.localRotation = Quaternion.Euler(modelEuler);
            model.transform.localScale = Vector3.one * (modelScale * armsScale);   // the anchor is at world scale 1 here

            Transform pumpPart = string.IsNullOrEmpty(modelPumpPart) ? null : model.transform.Find(modelPumpPart);
            if (pumpPart && pump) pumpPart.SetParent(pump, true);
            modelRoot = model.transform;
            modelPump = pumpPart;
            model.transform.SetParent(gunBody, true);   // not the anchor: that one is held at scale 1 and would not grow

            foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = weapon.gameObject.layer;
            if (pumpPart) foreach (var t in pumpPart.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = weapon.gameObject.layer;
            foreach (var r in model.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = ShadowCastingMode.Off;
            if (pumpPart) foreach (var r in pumpPart.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = ShadowCastingMode.Off;
            ViewModelLighting.MarkViewModel(model);
            if (pumpPart) ViewModelLighting.MarkViewModel(pumpPart.gameObject);

            // the clips' own gun goes; its shell stays for the reload
            foreach (var smr in sourceRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr.name == "main") smr.enabled = false;
        }

        /// <summary>How the reload clip turns the shell against the loading hand while carrying it, and where the
        /// middle of the shell mesh is: the shell is then hung on the hand itself, so it stays in the fingers.</summary>
        void MeasureShellInHand()
        {
            AnimationClip reload = null;
            foreach (var c in sourceAnimator.runtimeAnimatorController.animationClips)
                if (c.name.Contains("eload")) reload = c;
            if (!reload || !shell || !shellRenderer) return;
            // part-way through the first pass: the hand has the shell and is bringing it up
            reload.SampleAnimation(sourceRoot.gameObject, enterEnd + (firstShellEnd - enterEnd) * 0.75f);
            Transform hand = rig.Left.hand;
            shellRelRot = Quaternion.Inverse(hand.rotation) * shell.rotation;
            shellCenterLocal = shell.InverseTransformPoint(shellRenderer.bounds.center);

            // its long axis and length, from the mesh in the shell bone's own space (the bind pose puts each vertex
            // there exactly); the axis points to the nose, the end away from the thumb
            Mesh mesh = shellRenderer.sharedMesh;
            int boneIndex = System.Array.IndexOf(shellRenderer.bones, shell);
            if (!mesh || boneIndex < 0) return;
            Matrix4x4 toBone = mesh.bindposes[boneIndex];
            Vector3[] vertices = mesh.vertices;
            BoneWeight[] weights = mesh.boneWeights;
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            for (int i = 0; i < vertices.Length; i++)
            {
                if (weights.Length == vertices.Length && weights[i].boneIndex0 != boneIndex) continue;
                Vector3 p = toBone.MultiplyPoint3x4(vertices[i]);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            if (min.x > max.x) return;
            Vector3 size = max - min;
            int k = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
            shellCenterLocal = (min + max) * 0.5f;
            shellHalfLocal = size[k] * 0.5f;
            shellAxisLocal = Vector3.zero;
            shellAxisLocal[k] = 1f;
            Vector3 thumb = rig.Left.fingerEnds[0].position;
            if (Vector3.Distance(shell.TransformPoint(shellCenterLocal + shellAxisLocal * shellHalfLocal), thumb)
                < Vector3.Distance(shell.TransformPoint(shellCenterLocal - shellAxisLocal * shellHalfLocal), thumb))
                shellAxisLocal = -shellAxisLocal;
            pinchInHand = Quaternion.Inverse(hand.rotation) * (Vector3.Lerp(thumb, rig.Left.fingers[1 * ArmRig.JointsPerFinger + 1].position, shellPinch) - hand.position);
            thumbInHand = Quaternion.Inverse(hand.rotation) * (thumb - hand.position);

            if (sourceIdleClip) sourceIdleClip.SampleAnimation(sourceRoot.gameObject, 0f);
        }

        /// <summary>Half the shell's length in the world, at its size this frame.</summary>
        float ShellHalfLength() => shell.TransformVector(shellAxisLocal * shellHalfLocal).magnitude;

        /// <summary>Where the shell is on its way in, <paramref name="push"/> 0..1: lined up just under the loading
        /// port nose first, then up through the port to the magazine tube, then forward along it.</summary>
        void LoadingShellPose(float push, out Vector3 center, out Vector3 dir)
        {
            float tilt = loadingTilt * Mathf.Deg2Rad * (1f - Mathf.Clamp01(push * 2f - 1f));   // levels out as it slides along the tube
            dir = modelRoot.TransformDirection(new Vector3(Mathf.Cos(tilt), Mathf.Sin(tilt), 0f)).normalized;
            float half = ShellHalfLength() / Mathf.Max(modelRoot.lossyScale.x, 1e-4f);   // model units
            var below = new Vector3(loadingPort.x, loadingPort.y - half * 0.3f - 0.006f, loadingPort.z);
            var inPort = new Vector3(loadingPort.x, loadingTubeHeight, loadingPort.z);
            var inTube = inPort + new Vector3(loadingSlide, 0f, 0f);
            Vector3 at = push < 0.5f ? Vector3.Lerp(below, inPort, Mathf.SmoothStep(0f, 1f, push * 2f))
                                     : Vector3.Lerp(inPort, inTube, Mathf.SmoothStep(0f, 1f, push * 2f - 1f));
            center = modelRoot.TransformPoint(at);
        }

        /// <summary>Curls the index finger just so far that the pad of its last joint lies on the trigger's face.</summary>
        void CurlOntoTrigger(ArmRig.Arm a)
        {
            const int index = 1, first = index * ArmRig.JointsPerFinger;
            Vector3 target = modelRoot.TransformPoint(rightTriggerPoint);
            for (int j = 0; j < ArmRig.JointsPerFinger; j++) fingerScratch[j] = a.fingers[first + j].localRotation;

            float PadDistance(float c)
            {
                ArmRig.CurlFinger(a, index, c);
                Vector3 pad = Vector3.Lerp(a.fingers[first + ArmRig.JointsPerFinger - 1].position, a.fingerEnds[index].position, 0.3f);   // the pad: just past the last joint
                return (pad - target).sqrMagnitude;
            }
            float best = 0f, bestD = float.MaxValue;
            for (float c = 0f; c <= 1.001f; c += 0.05f) { float d = PadDistance(c); if (d < bestD) { bestD = d; best = c; } }
            float lo = Mathf.Max(0f, best - 0.05f), hi = Mathf.Min(1f, best + 0.05f);
            for (float c = lo; c <= hi + 0.0001f; c += 0.01f) { float d = PadDistance(c); if (d < bestD) { bestD = d; best = c; } }

            ArmRig.CurlFinger(a, index, best);
            for (int j = 0; j < ArmRig.JointsPerFinger; j++)
                a.fingers[first + j].localRotation = Quaternion.Slerp(fingerScratch[j], a.fingers[first + j].localRotation, rightTriggerWeight);
            TriggerPadError = Mathf.Sqrt(bestD);
        }

        /// <summary>How far the index finger's pad ended up from the trigger last frame (m): for checking the fit.</summary>
        public float TriggerPadError { get; private set; }

        Transform SourceBone(string boneName)
        {
            foreach (var t in sourceRoot.GetComponentsInChildren<Transform>(true))
                if (t.name == boneName && !t.GetComponent<SkinnedMeshRenderer>()) return t;
            Debug.LogWarning($"[ShotgunHands] Bone {boneName} not found.", this);
            return null;
        }

        void OnDestroy()
        {
            if (gun) gun.Fired -= OnFired;
        }

        void OnEnable()
        {
            if (!ready) return;
            walking = wasReloading = false;
            leftFree = 0f;
            pumpHold = 1f;
            sourceAnimator.Play("Idle", 0, 0f);
            if (otherArms) otherArms.RequestHidden(true);
        }

        void OnDisable()
        {
            if (ready && otherArms) otherArms.RequestHidden(false);
        }

        void OnFired(bool hit)
        {
            if (!isActiveAndEnabled) return;
            sourceAnimator.CrossFadeInFixedTime("Shot", 0.03f, 0, 0f);
            walking = false;
            shotUntil = Time.time + shotRecovery;
        }

        // ------------------------------------------------------------------ per frame

        void LateUpdate()
        {
            if (!ready || !holder) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            UpdateClips();
            sourceRoot.localPosition = rootBase;
            gunBody.localScale = bodyScale;   // the bones under it are read at the size the clips were made for

            Quaternion dq = gun.transform.localRotation * Quaternion.Inverse(GunRotation);
            Vector3 dp = gun.transform.localPosition - dq * GunPosition;
            Quaternion worldTurn = holder.rotation * dq * Quaternion.Inverse(holder.rotation);
            Vector3 Move(Vector3 p) => holder.TransformPoint(dp + dq * holder.InverseTransformPoint(p));

            ArmRig.Arm left = rig.Left, right = rig.Right;
            Vector3 grip = Move(right.hand.position + right.hand.rotation * right.palmCenter);
            Vector3 Grow(Vector3 p) => grip + (p - grip) * gunScale;
            // the clips' gun frame, moved: the grip turns and the reload offset are given in it
            Quaternion gunFrame = worldTurn * GunAnchor.rotation;
            Quaternion TurnIn(Vector3 euler) => gunFrame * Quaternion.Euler(euler) * Quaternion.Inverse(gunFrame);

            // outside the reload the palm rides the fore-end: the walk clips sway the hand on their own.
            // At the end of a reload it is back there in time for the racking stroke
            bool onPump = !shooting.IsReloading || (shooting.ReloadStage == 3 && shooting.ReloadStageTime >= ExitTime * 0.8f);
            pumpHold = Mathf.MoveTowards(pumpHold, onPump ? 1f : 0f, dt * 8f);
            // while loading, the shell and the hand that carries it go to this gun's loading port
            Vector3 reloadShift = gunFrame * (reloadShellOffset * armsScale) * (1f - pumpHold);

            // everything worked out in world space first: the rig may shift under it for reach
            Vector3 bodyPos = Grow(Move(gunBody.position)), shellPos = Grow(Move(shell.position) + reloadShift);
            Quaternion bodyRot = worldTurn * gunBody.rotation, shellRot = worldTurn * shell.rotation;

            Vector3 rightPos = Move(right.hand.position), leftPos = Move(left.hand.position);
            Quaternion rightRot = worldTurn * right.hand.rotation, leftRot = worldTurn * left.hand.rotation;
            if (rightHandEuler != Vector3.zero)
            {
                // turned about the palm, which is what the gun hangs on: the gun does not move
                Vector3 palm = rightPos + rightRot * right.palmCenter;
                rightRot = TurnIn(rightHandEuler) * rightRot;
                rightPos = palm - rightRot * right.palmCenter;
            }
            // the support hand stays on the bigger gun: its palm moves with the point it was holding
            Vector3 leftPalm = Move(left.hand.position + left.hand.rotation * left.palmCenter);
            leftPos += Grow(leftPalm) - leftPalm + reloadShift * gunScale;

            // down the sights the gun must lie exactly on the line the shots and the sight picture are worked out
            // on. The idle and walk clips keep the gun breathing in the hands, which walks the front sight out of
            // the notch: take that out, hands and all, as the gun comes up
            float steadyW = shooting.AimWeight * (shooting.IsReloading ? 0f : 1f);
            if (steadyW > 0.001f)
            {
                Vector3 restPos = Grow(Move(holder.TransformPoint(bodyRestPos)));
                Quaternion restRot = worldTurn * holder.rotation * bodyRestRot;
                Quaternion turn = Quaternion.Slerp(Quaternion.identity, restRot * Quaternion.Inverse(bodyRot), steadyW);
                Vector3 shiftTo = Vector3.Lerp(bodyPos, restPos, steadyW);
                Vector3 Steady(Vector3 p) => shiftTo + turn * (p - bodyPos);
                rightPos = Steady(rightPos); rightRot = turn * rightRot;
                leftPos = Steady(leftPos); leftRot = turn * leftRot;
                shellPos = Steady(shellPos); shellRot = turn * shellRot;
                bodyPos = shiftTo; bodyRot = turn * bodyRot;
            }

            if (pumpHold > 0.001f)
            {
                Quaternion pumpRot = worldTurn * pump.rotation;
                Quaternion heldRot = pumpRot * pumpHand;
                Vector3 heldPalm = Grow(Move(pump.position)) + pumpRot * (pumpPalm * gunScale);
                heldRot = TurnIn(leftHandEuler) * heldRot;
                Vector3 heldPos = heldPalm - heldRot * left.palmCenter;
                leftPos = Vector3.Lerp(leftPos, heldPos, pumpHold);
                leftRot = Quaternion.Slerp(leftRot, heldRot, pumpHold);
            }
            leftPos += gun.transform.TransformDirection(leftHandOffset);

            float freeTarget = shooting.IsReloading ? 0f : 1f - shooting.TwoHandWeight;
            leftFree = Mathf.MoveTowards(leftFree, freeTarget, dt * 8f);
            if (!freeLeftHand && pose) freeLeftHand = pose.transform.Find("LeftArm");   // built in the pose brain's Awake
            if (leftFree > 0.001f && freeLeftHand)
            {
                ArmRig.FreeTarget(left, freeLeftHand, out Vector3 freePos, out Quaternion freeRot);
                leftPos = Vector3.Lerp(leftPos, freePos, leftFree);
                leftRot = Quaternion.Slerp(leftRot, freeRot, leftFree);
            }

            left.upper.position = holder.TransformPoint(leftShoulder + shooting.ClimbLeftShoulderShift + climbLeftShoulder * shooting.ClimbWeight);
            right.upper.position = holder.TransformPoint(rightShoulder);
            sourceRoot.position += holder.TransformVector(aimArmsOffset * shooting.AimWeight + shooting.ClimbArmsShift);
            Vector3 reachDir = ArmRig.ReachDirection(holder);
            float shift = Mathf.Max(ArmRig.ReachShift(left, leftPos, reachDir), ArmRig.ReachShift(right, rightPos, reachDir));
            sourceRoot.position += reachDir * Mathf.Min(shift, maxReachShift);
            // forward and down is not always enough for the fore-end: the last centimetres go straight at it,
            // or the stretched arm would stop short and the palm would slide on the gun as it bobs
            sourceRoot.position -= ArmRig.ReachCorrection(left, leftPos) * (1f - leftFree);

            // a pose the right arm cannot get to: the gun stays in the hand and comes along
            Vector3 gunFix = ArmRig.ReachCorrection(right, rightPos);
            bodyPos += gunFix; shellPos += gunFix; rightPos += gunFix;
            leftPos += gunFix * (1f - leftFree);

            gunBody.SetPositionAndRotation(bodyPos, bodyRot);
            shell.SetPositionAndRotation(shellPos, shellRot);
            gunBody.localScale = bodyScale * gunScale;
            shell.localScale = shellScale * (gunScale * shellSize);
            GunAnchor.localScale = Vector3.one / gunBody.lossyScale.x;

            // the gun is where it will be this frame now: fit the left hand to the model itself
            float cradleW = leftCradle && modelPump ? pumpHold * (1f - leftFree) : 0f;
            if (cradleW > 0.001f)
            {
                // a cylinder grip: palm under the fore-end, fingers forward and round to the right, back of the hand down
                Vector3 fwd = modelPump.TransformDirection(Vector3.right).normalized;
                Vector3 up = modelPump.TransformDirection(Vector3.up).normalized;
                Vector3 fingers = Quaternion.AngleAxis(leftCradleFingerAngle, up) * fwd;
                Vector3 back = Quaternion.AngleAxis(leftCradleRoll, fingers) * -up;
                Quaternion cradleRot = Quaternion.LookRotation(fingers, back) * left.handFrame;
                Vector3 cradlePos = modelPump.TransformPoint(leftCradlePoint) - cradleRot * left.palmCenter;
                leftPos = Vector3.Lerp(leftPos, cradlePos, cradleW);
                leftRot = Quaternion.Slerp(leftRot, cradleRot, cradleW);
            }

            // loading: the shell goes the way it would into this gun. Late in each pass the hand lines it up under the
            // loading port, then the thumb pushes it up through the port and forward into the magazine tube
            bool loading = shellInHand && modelRoot && shooting.ReloadStage == 2 && loadingTubeHeight != 0f;
            float lineUp = 0f, push = 0f;
            if (loading)
            {
                float pass = Mathf.Min(shooting.ReloadStageTime * reloadRate, shellCycle) / shellCycle;
                lineUp = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(loadingPhases.x, loadingPhases.y, pass));
                push = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(loadingPhases.y, shellInsertShare, pass));
                LoadingShellPose(push, out loadCenter, out loadDir);

                // the thumb is behind the shell's base; it can follow it up to the mouth of the port, not into the gun
                Vector3 baseModel = modelRoot.InverseTransformPoint(loadCenter - loadDir * ShellHalfLength());
                baseModel.y = Mathf.Min(baseModel.y, loadingPort.y - 0.002f);
                Vector3 thumbTo = modelRoot.TransformPoint(baseModel);
                Vector3 pinchNow = leftPos + leftRot * thumbInHand;   // the thumb tip, last frame's shape of the hand
                leftPos += (thumbTo - pinchNow) * lineUp * (1f - leftFree);
            }

            // loading: the palm stays under the receiver, so the fingers and the shell go up to the port, not into the gun
            float loadW = modelRoot && loadingPalmCeiling > -1f ? (1f - pumpHold) * (1f - leftFree) * (1f - lineUp) : 0f;
            if (loadW > 0.001f)
            {
                Vector3 palmModel = modelRoot.InverseTransformPoint(leftPos + leftRot * left.palmCenter);
                if (palmModel.x > loadingPortSpan.x && palmModel.x < loadingPortSpan.y && palmModel.y > loadingPalmCeiling)
                    leftPos -= modelRoot.TransformVector(0f, palmModel.y - loadingPalmCeiling, 0f) * loadW;
            }

            // climbing: the left elbow flares out to the side, not down under the frame
            Vector3 sideHint = left.upper.position + holder.right * (left.side * climbElbowOut) - holder.up * climbElbowDown;
            ArmRig.Solve(left, leftPos, leftRot, Vector3.Lerp(ArmRig.ElbowHint(left, holder), sideHint, shooting.ClimbWeight));
            ArmRig.Solve(right, rightPos, rightRot, ArmRig.ElbowHint(right, holder));

            // on the fore-end the fingers wrap round it: the clips curl them round a slimmer one
            float gripW = leftGripWeight * pumpHold * (1f - leftFree);
            if (gripW > 0.001f)
            {
                for (int i = 0; i < left.fingers.Length; i++) fingerScratch[i] = left.fingers[i].localRotation;
                curl[0] = leftThumbCurl;
                for (int f = 1; f < ArmRig.FingerCount; f++) curl[f] = leftFingerCurl;
                ArmRig.SetCurl(left, curl);
                for (int i = 0; i < left.fingers.Length; i++)
                    left.fingers[i].localRotation = Quaternion.Slerp(fingerScratch[i], left.fingers[i].localRotation, gripW);
            }

            // loading: the shell is in the fingers of the hand that carries it, pinched between thumb and index
            if (shellInHand && shooting.ReloadStage == 2)
            {
                Transform hand = left.hand;
                shell.rotation = hand.rotation * shellRelRot * Quaternion.Euler(shellPinchEuler);
                Vector3 pinch = Vector3.Lerp(left.fingerEnds[0].position, left.fingers[1 * ArmRig.JointsPerFinger + 1].position, shellPinch);
                shell.position += pinch + hand.rotation * shellPinchOffset - shell.TransformPoint(shellCenterLocal);
                pinchInHand = Quaternion.Inverse(hand.rotation) * (pinch - hand.position);   // for next frame's hand target
                thumbInHand = Quaternion.Inverse(hand.rotation) * (left.fingerEnds[0].position - hand.position);

                // lined up under the port and pushed in: the shell is on its loading path, the hand's thumb behind it
                if (loading && lineUp > 0.001f)
                {
                    Vector3 heldCenter = shell.TransformPoint(shellCenterLocal);
                    Quaternion onPath = Quaternion.FromToRotation(shell.rotation * shellAxisLocal, loadDir) * shell.rotation;
                    shell.rotation = Quaternion.Slerp(shell.rotation, onPath, lineUp);
                    shell.position += Vector3.Lerp(heldCenter, loadCenter, lineUp) - shell.TransformPoint(shellCenterLocal);
                }
            }

            // the index finger on the trigger: curled just so far that its pad lies on the trigger's face
            if (rightTriggerWeight > 0.001f && modelRoot) CurlOntoTrigger(right);

            bool parkour = player && !(player.State == MoveState.Grounded || player.State == MoveState.Airborne || player.State == MoveState.Sliding);
            float openTarget = leftFree * (parkour || (player && player.IsBraceReady) ? 1f : 0f);
            leftOpen = Mathf.MoveTowards(leftOpen, openTarget, dt * 8f);
            if (leftOpen > 0.001f) ArmRig.BlendOpen(left, leftOpen);
        }

        /// <summary>How the gun is carried through parkour moves: low and across the body, never tipped
        /// so far that the long butt swings up into the lens or the right arm has to reach for it.</summary>
        public void GetCarryPose(MoveState state, float progress, Vector3 readyPosition, Quaternion readyRotation,
                                 out Vector3 position, out Quaternion rotation)
        {
            Vector3 p = new Vector3(0.24f, -0.36f, 0.18f), e = new Vector3(25f, -15f, 25f);
            switch (state)
            {
                case MoveState.Mantling: p = new Vector3(0.2f, -0.4f, 0.16f); e = new Vector3(35f, -30f, 35f); break;
                case MoveState.Vaulting: p = new Vector3(0.26f, -0.36f, 0.16f); e = new Vector3(30f, -20f, 40f); break;
                case MoveState.BraceWall: p = new Vector3(0.22f, -0.34f, 0.16f); e = new Vector3(20f, -20f, 35f); break;
                case MoveState.HandLanding: p = new Vector3(0.22f, -0.42f, 0.14f); e = new Vector3(35f, -15f, 25f); break;
                case MoveState.Rolling: p = new Vector3(0.14f, -0.38f, 0.12f); e = new Vector3(30f, -35f, 50f); break;
                case MoveState.HardLanding: p = new Vector3(0.26f, -0.42f, 0.16f); e = new Vector3(35f, -20f, 30f); break;
            }
            position = p;
            rotation = Quaternion.Euler(e);
            if (state != MoveState.Bumped) return;
            // braced against the wall, coming back to ready as the stun wears off
            position = Vector3.Lerp(readyPosition, new Vector3(0.2f, -0.32f, 0.16f), 1f - progress);
            rotation = Quaternion.Slerp(readyRotation, Quaternion.Euler(15f, -15f, 20f), 1f - progress);
        }

        void UpdateClips()
        {
            int stage = shooting.ReloadStage;
            bool reloading = stage != 0;
            if (reloading != wasReloading)
            {
                wasReloading = reloading;
                walking = false;
                sourceAnimator.CrossFadeInFixedTime(reloading ? "Reload" : "Idle", reloading ? 0.08f : 0.16f, 0, 0f);
            }
            // the clip keeps the shell around all the time; in the hand it goes once it is pushed into the port
            bool pushedIn = shellInHand && stage == 2 && Mathf.Min(shooting.ReloadStageTime * reloadRate, shellCycle) >= shellCycle * shellInsertShare;
            if (shellRenderer) shellRenderer.enabled = stage == 2 && !pushedIn;
            if (reloading)
            {
                sourceAnimator.SetFloat("ReloadTime", ReloadClipTime(stage) / reloadLength);
                return;
            }

            // the shot and its pump: nothing may cut in, and the Animator only reports the state after the blend
            if (Time.time < shotUntil) return;
            var state = sourceAnimator.GetCurrentAnimatorStateInfo(0);
            if (state.IsName("Shot") && state.normalizedTime < 1f) return;
            bool moving = player && player.State == MoveState.Grounded && player.Speed > walkSpeed;
            if (moving == walking) return;
            walking = moving;
            sourceAnimator.CrossFadeInFixedTime(moving ? "Walk" : "Idle", 0.15f, 0, 0f);
        }

        float ReloadClipTime(int stage)
        {
            float t = shooting.ReloadStageTime * reloadRate;
            switch (stage)
            {
                case 1: return Mathf.Min(t, enterEnd);
                case 2: return (shooting.ShellsLoaded == 0 ? enterEnd : firstShellEnd) + Mathf.Min(t, shellCycle);
                default: return Mathf.Min(exitStart + t, reloadLength);
            }
        }
    }
}
