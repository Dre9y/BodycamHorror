using BodycamHorror.Environment;
using BodycamHorror.Weapons;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BodycamHorror.Player
{
    /// <summary>
    /// Lets the player use doors, turn valves and pick guns up: stand close, look at one, and the key to
    /// press shows by its handle / on the wheel / over the gun (E / D-pad down). Doors and pickups add this to the player themselves,
    /// so nothing has to be set up per scene. While something is in reach the same key does not toggle
    /// the flashlight (see BodycamFlashlight).
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public class PlayerInteractor : MonoBehaviour
    {
        public static PlayerInteractor Instance { get; private set; }
        /// <summary>A door or a pickup is in reach and looked at right now.</summary>
        public static bool HasTarget => Instance && Instance.isActiveAndEnabled && (Instance.Target || Instance.Pickup || Instance.Valve);

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
        [Tooltip("The padlock that shows by the key once a locked door has been tried.")]
        public Color lockedInk = new Color(0.92f, 0.12f, 0.1f, 1f);

        public Door Target { get; private set; }
        public WeaponPickup Pickup { get; private set; }
        public Valve Valve { get; private set; }

        Camera cam;
        InputAction useAction;
        string keyLabel = "E";
        Vector3 promptPoint;
        float promptAlpha;
        GUIStyle labelStyle;
        static Texture2D white, padlockIcon;
        Door triedLocked;      // the door being looked at was tried and would not open
        float lockedShown, lockedAge;

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
            if (UI.PauseMenu.IsOpen) return;
            if (!cam || !cam.isActiveAndEnabled)
            {
                cam = GetComponentInChildren<Camera>();
                if (!cam) cam = Camera.main;
            }
            Target = cam ? FindDoor(cam.transform.position, cam.transform.forward) : null;
            Pickup = cam && !Target ? FindPickup(cam.transform.position, cam.transform.forward) : null;
            Valve = cam && !Target && !Pickup ? FindValve(cam.transform.position, cam.transform.forward) : null;

            if (Target)
            {
                promptPoint = Target.PromptPoint(cam.transform.position) + Vector3.up * promptLift;
                if (useAction.WasPressedThisFrame() && !Target.Toggle(cam.transform.position) && Target.locked)
                {
                    triedLocked = Target;
                    lockedAge = 0f;   // pops in again (and shakes) on every try
                }
            }
            else if (Pickup)
            {
                promptPoint = Pickup.PromptPoint;
                if (useAction.WasPressedThisFrame() && Pickup.Take(gameObject)) Pickup = null;
            }
            else if (Valve)
            {
                promptPoint = Valve.PromptPoint;
                if (useAction.IsPressed()) Valve.Use(useAction.WasPressedThisFrame());
            }
            promptAlpha = Mathf.MoveTowards(promptAlpha, Target || Pickup || Valve ? 1f : 0f, Time.unscaledDeltaTime * 7f);

            // the lock badge stays while the player keeps looking at that door, until it is shot open
            if (triedLocked && (Target != triedLocked || !triedLocked.locked)) triedLocked = null;
            lockedAge += Time.unscaledDeltaTime;
            lockedShown = Mathf.MoveTowards(lockedShown, triedLocked ? 1f : 0f, Time.unscaledDeltaTime * (triedLocked ? 12f : 7f));
        }

        Valve FindValve(Vector3 eye, Vector3 forward)
        {
            Valve best = null;
            float bestAngle = pickupLookAngle;
            foreach (Valve valve in Valve.All)
            {
                if (!valve.CanUse) continue;
                Vector3 to = valve.PromptPoint - eye;
                float distance = to.magnitude;
                if (distance > reach || distance < 0.01f) continue;
                float a = Vector3.Angle(forward, to);
                if (a >= bestAngle) continue;
                // not through a wall
                if (Physics.Raycast(eye, to / distance, out RaycastHit block, distance - 0.05f, mask, QueryTriggerInteraction.Ignore)
                    && !block.collider.transform.IsChildOf(valve.transform)) continue;
                best = valve;
                bestAngle = a;
            }
            return best;
        }

        WeaponPickup FindPickup(Vector3 eye, Vector3 forward)
        {
            WeaponPickup best = null;
            float bestAngle = pickupLookAngle;
            foreach (WeaponPickup pickup in WeaponPickup.All)
            {
                Vector3 to = pickup.transform.position + Vector3.up * 0.05f - eye;
                float distance = to.magnitude;
                if (distance > (pickup.reach > 0f ? pickup.reach : pickupReach) || distance < 0.01f) continue;
                float a = Vector3.Angle(forward, to);
                if (a >= bestAngle || (pickup.lookAngle > 0f && a >= pickup.lookAngle)) continue;
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

            if (lockedShown > 0.01f) DrawLocked(box, line);
            GUI.color = Color.white;
        }

        /// <summary>A red padlock in a cap of its own beside the key: pops in and shakes when the door is tried.</summary>
        void DrawLocked(Rect key, float line)
        {
            float pop = 1f + 0.35f * Mathf.Exp(-lockedAge * 14f);
            float shake = Mathf.Sin(lockedAge * 70f) * Mathf.Exp(-lockedAge * 9f) * key.width * 0.09f;
            float size = key.width * 0.78f * pop;
            var badge = new Rect(Mathf.Round(key.xMax + key.width * 0.16f + shake), Mathf.Round(key.center.y - size * 0.5f), size, size);
            float a = promptAlpha * lockedShown;

            Color fill = promptFill, ink = lockedInk;
            fill.a *= a;
            ink.a *= a;
            GUI.color = fill;
            GUI.DrawTexture(badge, white);
            GUI.color = ink;
            GUI.DrawTexture(new Rect(badge.x, badge.y, badge.width, line), white);
            GUI.DrawTexture(new Rect(badge.x, badge.yMax - line, badge.width, line), white);
            GUI.DrawTexture(new Rect(badge.x, badge.y, line, badge.height), white);
            GUI.DrawTexture(new Rect(badge.xMax - line, badge.y, line, badge.height), white);

            if (!padlockIcon) padlockIcon = MakePadlockIcon(64);
            float inset = badge.width * 0.17f;
            GUI.DrawTexture(new Rect(badge.x + inset, badge.y + inset, badge.width - inset * 2f, badge.height - inset * 2f), padlockIcon);
        }

        /// <summary>A white padlock glyph (shackle, body, keyhole) drawn from distance fields, antialiased; tinted when drawn.</summary>
        static Texture2D MakePadlockIcon(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "PadlockIcon" };
            var pixels = new Color32[size * size];
            float px = 1f / size;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // 0..1, y up (texture rows run bottom to top)
                    var p = new Vector2((x + 0.5f) * px, (y + 0.5f) * px);
                    float body = RoundBox(p - new Vector2(0.5f, 0.31f), new Vector2(0.36f, 0.27f), 0.07f);
                    // shackle: the top half of a ring, its legs running down into the body
                    Vector2 q = p - new Vector2(0.5f, 0.62f);
                    float ringDistance = q.y > 0f ? q.magnitude : new Vector2(q.x, 0f).magnitude;
                    float shackle = Mathf.Abs(ringDistance - 0.2f) - 0.065f;
                    if (q.y <= 0f && p.y < 0.5f) shackle = 1f;
                    float shape = Mathf.Min(body, shackle);
                    // keyhole: a round hole over a tapering slot
                    float hole = Mathf.Min((p - new Vector2(0.5f, 0.35f)).magnitude - 0.065f,
                        RoundBox(p - new Vector2(0.5f, 0.24f), new Vector2(0.032f, 0.08f), 0.01f));
                    shape = Mathf.Max(shape, -hole);
                    float alpha = Mathf.Clamp01(0.5f - shape / px);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        static float RoundBox(Vector2 p, Vector2 half, float radius)
        {
            Vector2 d = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - half + Vector2.one * radius;
            return Vector2.Max(d, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(d.x, d.y), 0f) - radius;
        }
    }
}
