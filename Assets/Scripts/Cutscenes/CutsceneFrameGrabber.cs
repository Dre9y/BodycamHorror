using System.IO;
using UnityEngine;

namespace BodycamHorror.Cutscenes
{
    /// <summary>
    /// Dev tool: plays the scene at a fixed 30 frames a second of game time (however slow the machine
    /// renders) and saves the cutscene camera's picture at the listed cutscene frames as PNGs, so a cutscene
    /// can be checked frame by frame. Add it next to an <see cref="ElevatorFinaleCutscene"/> and press Play.
    /// </summary>
    public class CutsceneFrameGrabber : MonoBehaviour
    {
        public ElevatorFinaleCutscene cutscene;
        [Tooltip("Cutscene frames to save.")]
        public int[] frames = { 60, 100, 140, 190, 200, 206, 212, 220, 230, 245 };
        [Tooltip("Folder the PNGs go to (absolute, or relative to the project).")]
        public string folder = "Captures/Cutscene";
        public Vector2Int size = new Vector2Int(640, 360);

        int next;

        void OnEnable()
        {
            Time.captureFramerate = 30;
            Directory.CreateDirectory(folder);
            next = 0;
        }

        void OnDisable() => Time.captureFramerate = 0;

        void LateUpdate()
        {
            if (!cutscene || !cutscene.viewCamera || next >= frames.Length) return;
            if (cutscene.Frame < frames[next]) return;
            int frame = frames[next++];
            var cam = cutscene.viewCamera.GetComponent<Camera>();
            var rt = RenderTexture.GetTemporary(size.x, size.y, 24);
            var old = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = old;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(Path.Combine(folder, $"frame_{frame:000}.png"), tex.EncodeToPNG());
            Destroy(tex);
        }
    }
}
