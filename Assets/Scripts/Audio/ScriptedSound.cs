using UnityEngine;

namespace BodycamHorror.Audio
{
    /// <summary>
    /// For sounds that belong to a staged moment rather than to the place: a scare's sting, a cue.
    /// They are played as made, past the listener's effects, so the room the player happens to stand
    /// in (and the body camera microphone) does not colour them. Everything else in the game goes
    /// through those effects and takes on the space it is heard in.
    /// </summary>
    public static class ScriptedSound
    {
        /// <summary>Plays a clip once at a point in the world, untouched by the room.</summary>
        public static AudioSource PlayAt(AudioClip clip, Vector3 position, float volume = 1f, float spatialBlend = 1f)
        {
            if (!clip) return null;
            var go = new GameObject("ScriptedSound");
            go.transform.position = position;
            var source = go.AddComponent<AudioSource>();
            Mark(source);
            source.spatialBlend = spatialBlend;
            source.clip = clip;
            source.volume = volume;
            source.Play();
            Object.Destroy(go, clip.length / Mathf.Max(Mathf.Abs(source.pitch), 0.01f) + 0.2f);
            return source;
        }

        /// <summary>Takes an existing source out of the room: use on a scripted event's own AudioSource.</summary>
        public static void Mark(AudioSource source)
        {
            if (!source) return;
            source.bypassListenerEffects = true;
            source.bypassReverbZones = true;
            source.bypassEffects = false;   // its own filters are part of how it was made
        }
    }
}
