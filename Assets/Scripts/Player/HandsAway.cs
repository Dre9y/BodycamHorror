using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BodycamHorror.Player
{
    /// <summary>
    /// Takes the hands (and the gun in them) down out of the frame while the player is busy with something they do
    /// "off camera", like turning a valve wheel (<see cref="Request"/>, called every frame it goes on), and brings them
    /// back up after.
    /// Everything under the camera pivot except the camera's own rig (the gun holder, the arms) is swung down only
    /// for the drawing of each frame: it is put back right after, so aiming, shots and the arms' posing never see it.
    /// Added to the player by itself (<see cref="Ensure"/>).
    /// </summary>
    public class HandsAway : MonoBehaviour
    {
        [Tooltip("How far down the hands go (m).")]
        public float drop = 0.6f;
        [Tooltip("And back towards the body (m).")]
        public float pullBack = 0.12f;
        [Tooltip("Tipped down about the eye by this much (deg).")]
        public float tilt = 35f;
        [Tooltip("Seconds to get them out of the way / back up.")]
        public float awayTime = 0.3f, backTime = 0.45f;

        static HandsAway instance;
        static float requestedUntil = -1f;

        // Domain reload is off in this project: statics outlive Play Mode while Time.time starts again from 0,
        // so a request from the last session would hold the hands down for as long as that session ran.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { instance = null; requestedUntil = -1f; }

        BodycamPlayerController player;
        float weight;
        bool applied;
        readonly List<Transform> parts = new List<Transform>();
        readonly List<Vector3> savedPos = new List<Vector3>();
        readonly List<Quaternion> savedRot = new List<Quaternion>();

        /// <summary>Keep the hands away this frame (call every frame the thing goes on).</summary>
        public static void Request() => requestedUntil = Time.time + 0.1f;

        /// <summary>0 = hands where they are, 1 = all the way down.</summary>
        public static float Weight => instance ? instance.weight : 0f;

        public static void Ensure()
        {
            if (instance) return;
            var p = FindAnyObjectByType<BodycamPlayerController>();
            if (!p || !p.cameraPivot) return;
            instance = p.GetComponent<HandsAway>();
            if (!instance) instance = p.gameObject.AddComponent<HandsAway>();
        }

        void Awake()
        {
            instance = this;
            player = GetComponent<BodycamPlayerController>();
        }

        void OnEnable()
        {
            RenderPipelineManager.beginContextRendering += Apply;
            RenderPipelineManager.endContextRendering += Undo;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginContextRendering -= Apply;
            RenderPipelineManager.endContextRendering -= Undo;
            Undo(default, null);
        }

        void Update()
        {
            float target = Time.time <= requestedUntil ? 1f : 0f;
            float time = target > weight ? awayTime : backTime;
            weight = Mathf.MoveTowards(weight, target, Time.deltaTime / Mathf.Max(time, 0.01f));
        }

        void Apply(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (applied || weight <= 0.0001f || !player || !player.cameraPivot) return;
            float e = weight * weight * (3f - 2f * weight);
            Quaternion swing = Quaternion.Euler(tilt * e, 0f, 0f);
            Vector3 shift = new Vector3(0f, -drop, -pullBack) * e;

            parts.Clear(); savedPos.Clear(); savedRot.Clear();
            foreach (Transform part in player.cameraPivot)
            {
                if (part.GetComponentInChildren<Camera>(true)) continue;   // the view itself stays
                parts.Add(part);
                savedPos.Add(part.localPosition);
                savedRot.Add(part.localRotation);
                // swung down about the eye (the pivot's origin), then lowered
                part.localPosition = swing * part.localPosition + shift;
                part.localRotation = swing * part.localRotation;
            }
            applied = true;
        }

        void Undo(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (!applied) return;
            for (int i = 0; i < parts.Count; i++)
                if (parts[i]) parts[i].SetLocalPositionAndRotation(savedPos[i], savedRot[i]);
            applied = false;
        }
    }
}
