using BodycamHorror.Environment;
using BodycamHorror.Weapons;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BodycamHorror.Player
{
    /// <summary>
    /// Lets the player use doors and pick guns up: stand close, look at one, and the key to press shows
    /// by its handle / over the gun (F / D-pad down). Doors and pickups add this to the player themselves,
    /// so nothing has to be set up per scene. While something is in reach the same key does not toggle
    /// the flashlight (see BodycamFlashlight).
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public class PlayerInteractor : MonoBehaviour
    {
        public static PlayerInteractor Instance { get; private set; }
        /// <summary>A door or a pickup is in reach and looked at right now.</summary>
        public static bool HasTarget => Instance && Instance.isActiveAndEnabled && (Instance.Target || Instance.Pickup);

        [Tooltip("How far the player can reach (m, from the camera).")]
        public float reach = 2.2f;
        [Tooltip("Looking this close to a handle counts even when the view ray misses the door itself (deg).")]
        public float lookAngle = 22f;
        public LayerMask mask = ~(1 << 2);   // not Ignore Raycast (the player)
        [Tooltip("Reach for things lying about (m, from the camera: the floor is a long way down).")]
        public float pickupReach = 2.6f;
        [Tooltip("A pickup counts when looked at this closely (deg).")]
        public float pickupLookAngle = 30f;

        [Header("Prompt")]
        [Tooltip("Key cap size in pixels at 1080p.")]
        public float promptSize = 40f;
        [Tooltip("The prompt sits this far above the handle (m).")]
        public float promptLift = 0.14f;
        public Color promptFill = new Color(0.04f, 0.045f, 0.05f, 0.78f);
        public Color promptInk = new Color(0.92f, 0.94f, 0.9f, 1f);

        public Door Target { get; private set; }
        public WeaponPickup Pickup { get; private set; }

        Camera cam;
        InputAction useAction;
        string keyLabel = "E";
        Vector3 promptPoint;
        float promptAlpha;
        GUIStyle labelStyle;
        static Texture2D white;

        /// <summary>Puts an interactor on the player if the scene has none yet.</summary>
        public static void Ensure()
        {
            if (Instance) return;
            var player = FindAnyObjectByType<BodycamPlayerController>();
            if (player && !player.GetComponent<PlayerInteractor>()) player.gameObject.AddComponent<PlayerInteractor>();
        }

        void Awake()
        {
            Instance = this;
            useAction = new InputAction("Interact", InputActionType.Button, "<Keyboard>/e");
            useAction.AddBinding("<Gamepad>/dpad/down");
            // the key as printed on a QWERTY board, whatever layout is active (a Russian layout would show "А")
            string shown = InputControlPath.ToHumanReadableString(useAction.bindings[0].effectivePath,
                InputControlPath.HumanReadableStringOptions.OmitDevice);
            if (!string.IsNullOrEmpty(shown)) keyLabel = shown.ToUpperInvariant();
        }

        void OnEnable() => useAction.Enable();
        void OnDisable() => useAction.Disable();

        void OnDestroy()
        {
            useAction?.Dispose();
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (!cam || !cam.isActiveAndEnabled)
            {
                cam = GetComponentInChildren<Camera>();
                if (!cam) cam = Camera.main;
            }
            Target = cam ? FindDoor(cam.transform.position, cam.transform.forward) : null;
            Pickup = cam && !Target ? FindPickup(cam.transform.position, cam.transform.forward) : null;

            if (Target)
            {
                promptPoint = Target.PromptPoint(cam.transform.position) + Vector3.up * promptLift;
                if (useAction.WasPressedThisFrame()) Target.Toggle(cam.transform.position);
            }
            else if (Pickup)
            {
                promptPoint = Pickup.PromptPoint;
                if (useAction.WasPressedThisFrame() && Pickup.Take(gameObject)) Pickup = null;
            }
            promptAlpha = Mathf.MoveTowards(promptAlpha, Target || Pickup ? 1f : 0f, Time.unscaledDeltaTime * 7f);
        }

        WeaponPickup FindPickup(Vector3 eye, Vector3 forward)
        {
            WeaponPickup best = null;
            float bestAngle = pickupLookAngle;
            foreach (WeaponPickup pickup in WeaponPickup.All)
            {
                Vector3 to = pickup.transform.position + Vector3.up * 0.05f - eye;
                float distance = to.magnitude;
                if (distance > pickupReach || distance < 0.01f) continue;
                float a = Vector3.Angle(forward, to);
                if (a >= bestAngle) continue;
                // not through a wall or a crate
                if (Physics.Raycast(eye, to / distance, out RaycastHit block, distance - 0.1f, mask, QueryTriggerInteraction.Ignore)
                    && !block.collider.transform.IsChildOf(pickup.transform)) continue;
                best = pickup;
                bestAngle = a;
            }
            return best;
        }

        Door FindDoor(Vector3 eye, Vector3 forward)
        {
            // looking straight at it
            if (Physics.Raycast(eye, forward, out RaycastHit hit, reach, mask, QueryTriggerInteraction.Ignore))
            {
                Door door = Door.Of(hit.collider);
                if (door) return door;
            }
            // or near enough at its handle: an open door is seen edge-on and is easy to miss
            Door best = null;
            float bestAngle = lookAngle;
            foreach (Door door in Door.All)
            {
                Vector3 to = door.PromptPoint(eye) - eye;
                float distance = to.magnitude;
                if (distance > reach || distance < 0.01f) continue;
                float a = Vector3.Angle(forward, to);
                if (a >= bestAngle) continue;
                if (Physics.Raycast(eye, to / distance, out RaycastHit wall, distance - 0.05f, mask, QueryTriggerInteraction.Ignore)
                    && Door.Of(wall.collider) != door) continue;
                best = door;
                bestAngle = a;
            }
            return best;
        }

        void OnGUI()
        {
            if (promptAlpha <= 0.01f || !cam || Event.current.type != EventType.Repaint) return;
            Vector3 screen = cam.WorldToScreenPoint(promptPoint);
            if (screen.z <= 0.05f) return;

            if (!white) { white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply(); }
            float size = promptSize * Screen.height / 1080f;
            float line = Mathf.Max(1f, Mathf.Round(size * 0.055f));
            var box = new Rect(Mathf.Round(screen.x - size * 0.5f), Mathf.Round(Screen.height - screen.y - size * 0.5f), size, size);

            Color fill = promptFill, ink = promptInk;
            fill.a *= promptAlpha;
            ink.a *= promptAlpha;
            GUI.color = fill;
            GUI.DrawTexture(box, white);
            GUI.color = ink;
            GUI.DrawTexture(new Rect(box.x, box.y, box.width, line), white);
            GUI.DrawTexture(new Rect(box.x, box.yMax - line, box.width, line), white);
            GUI.DrawTexture(new Rect(box.x, box.y, line, box.height), white);
            GUI.DrawTexture(new Rect(box.xMax - line, box.y, line, box.height), white);

            if (labelStyle == null) labelStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            labelStyle.fontSize = Mathf.RoundToInt(size * (keyLabel.Length > 1 ? 0.34f : 0.58f));
            labelStyle.normal.textColor = Color.white;
            GUI.Label(box, keyLabel, labelStyle);
            GUI.color = Color.white;
        }
    }
}
