using UnityEngine;

namespace BodycamHorror.UI
{
    /// <summary>
    /// Frames per second in the top right corner, with the frame time under it. Averaged over a quarter
    /// of a second so it can be read; the number turns amber below 50 and red below 30. Switched on and
    /// off in the pause menu's settings (<see cref="GameSettings.ShowFps"/>). Installs itself.
    /// </summary>
    public class FpsCounter : MonoBehaviour
    {
        const float Window = 0.25f;   // seconds averaged per reading

        int frames;
        float elapsed, fps, ms;
        string text = "";
        GUIStyle style, shadow;
        float styleScale = -1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindAnyObjectByType<FpsCounter>()) return;
            var go = new GameObject("FpsCounter");
            DontDestroyOnLoad(go);
            go.AddComponent<FpsCounter>();
        }

        void Update()
        {
            // real time: the pause menu stops game time, the counter keeps counting
            frames++;
            elapsed += Time.unscaledDeltaTime;
            if (elapsed < Window) return;
            fps = frames / elapsed;
            ms = elapsed * 1000f / frames;
            text = Mathf.RoundToInt(fps) + " FPS\n" + ms.ToString("0.0") + " ms";
            frames = 0;
            elapsed = 0f;
        }

        void OnGUI()
        {
            if (!GameSettings.ShowFps || Event.current.type != EventType.Repaint || text.Length == 0) return;
            float k = Screen.height / 1080f;
            if (!Mathf.Approximately(k, styleScale) || style == null)
            {
                styleScale = k;
                style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = Mathf.Max(10, Mathf.RoundToInt(22f * k)), fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.UpperRight, wordWrap = false,
                };
                shadow = new GUIStyle(style);
                shadow.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
            }
            style.normal.textColor = fps >= 50f ? new Color(0.88f, 0.92f, 0.86f) : fps >= 30f ? new Color(1f, 0.75f, 0.3f) : new Color(1f, 0.35f, 0.3f);

            float margin = 16f * k, w = 200f * k, h = 70f * k;
            var r = new Rect(Screen.width - margin - w, margin, w, h);
            float o = Mathf.Max(1f, Mathf.Round(2f * k));
            GUI.Label(new Rect(r.x + o, r.y + o, r.width, r.height), text, shadow);
            GUI.Label(r, text, style);
        }
    }
}
