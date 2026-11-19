using System.Collections.Generic;
using UnityEngine;

namespace BodycamHorror.Player
{
    /// <summary>
    /// The keys the player carries (by id, see KeyPickup and Door.keyId). Taking a key, or using one up on a door,
    /// shows a short line at the bottom of the screen ("Ключ: кв. 3"). Added to the player on first use.
    /// </summary>
    public class KeyRing : MonoBehaviour
    {
        [Tooltip("How long a message stays (s), fade included.")]
        public float messageTime = 3f;
        [Tooltip("Text size in pixels at 1080p.")]
        public float textSize = 26f;
        public Color fill = new Color(0.04f, 0.045f, 0.05f, 0.72f);
        public Color ink = new Color(0.92f, 0.94f, 0.9f, 1f);

        readonly Dictionary<string, string> keys = new Dictionary<string, string>();
        string message;
        float messageAge = float.MaxValue;
        GUIStyle style;
        static Texture2D white;

        /// <summary>The ring on this player, added if it has none.</summary>
        public static KeyRing Of(GameObject player)
        {
            if (!player) return null;
            var ring = player.GetComponentInParent<KeyRing>();
            if (!ring)
            {
                var controller = player.GetComponentInParent<BodycamPlayerController>();
                ring = (controller ? controller.gameObject : player).AddComponent<KeyRing>();
            }
            return ring;
        }

        public bool Has(string id) => !string.IsNullOrEmpty(id) && keys.ContainsKey(id);

        /// <summary>Puts a key on the ring. <paramref name="label"/> is what the player is told it is.</summary>
        public void Add(string id, string label)
        {
            if (string.IsNullOrEmpty(id)) return;
            keys[id] = label;
            Say("Ключ: " + label);
        }

        /// <summary>Takes a key off the ring (it stayed in a lock).</summary>
        public void UseUp(string id)
        {
            if (keys.TryGetValue(id, out string label)) keys.Remove(id);
        }

        public void Say(string text)
        {
            message = text;
            messageAge = 0f;
        }

        void Update() => messageAge += Time.unscaledDeltaTime;

        void OnGUI()
        {
            if (string.IsNullOrEmpty(message) || messageAge > messageTime || Event.current.type != EventType.Repaint) return;
            float a = Mathf.Clamp01(messageAge / 0.15f) * Mathf.Clamp01((messageTime - messageAge) / 0.6f);
            if (a <= 0.01f) return;

            if (!white) { white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply(); }
            float scale = Screen.height / 1080f;
            if (style == null) style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            style.fontSize = Mathf.RoundToInt(textSize * scale);
            style.normal.textColor = new Color(ink.r, ink.g, ink.b, ink.a * a);

            Vector2 size = style.CalcSize(new GUIContent(message));
            float padX = 22f * scale, padY = 10f * scale;
            var box = new Rect(Mathf.Round((Screen.width - size.x) * 0.5f - padX), Mathf.Round(Screen.height * 0.82f - padY),
                               size.x + padX * 2f, size.y + padY * 2f);
            GUI.color = new Color(fill.r, fill.g, fill.b, fill.a * a);
            GUI.DrawTexture(box, white);
            GUI.color = Color.white;
            GUI.Label(box, message, style);
        }
    }
}
