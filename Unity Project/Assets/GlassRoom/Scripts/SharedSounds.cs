using UnityEngine;

namespace GlassRoom
{
    /// <summary>The sounds a player makes that everyone else nearby hears too.</summary>
    public enum SharedSound : byte
    {
        /// <summary>Pushing off for a jump (index: footstep clip).</summary>
        Jump,
        /// <summary>Landing from a jump or fall (index: footstep clip).</summary>
        Land,
        /// <summary>Leaves rustling while moving through a plant (index: rustle clip).</summary>
        Rustle,
        /// <summary>Sitting down in a chair (index: chair number).</summary>
        Sit,
        /// <summary>Stepping onto a scooter (index: scooter number).</summary>
        ScooterOn,
        /// <summary>Stepping off a scooter (index: scooter number).</summary>
        ScooterOff,
    }

    /// <summary>
    /// Passes the sounds you make on to the other players (NetworkPlayer sends them), and plays
    /// theirs from where they happened, quieter with distance.
    /// </summary>
    public static class SharedSounds
    {
        /// <summary>Raised when you make one of these sounds: what, which clip/thing, where, how loud.</summary>
        public static event System.Action<SharedSound, int, Vector3, float> Played;

        public static void Raise(SharedSound sound, int index, Vector3 at, float volume) => Played?.Invoke(sound, index, at, volume);

        /// <summary>Another player's sound, heard from where it happened.</summary>
        public static void PlayRemote(SharedSound sound, int index, Vector3 at, float volume)
        {
            var clip = ClipFor(sound, index, out float pitch);
            if (clip == null) return;
            var go = new GameObject("Shared Sound");
            go.transform.position = at;
            var source = go.AddComponent<AudioSource>();
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = 2f;
            source.maxDistance = 30f;
            source.dopplerLevel = 0f;
            source.pitch = pitch * Random.Range(0.95f, 1.05f);
            source.PlayOneShot(clip, Mathf.Clamp01(volume));
            Object.Destroy(go, clip.length / Mathf.Max(0.5f, source.pitch) + 0.2f);
        }

        static AudioClip ClipFor(SharedSound sound, int index, out float pitch)
        {
            pitch = 1f;
            switch (sound)
            {
                case SharedSound.Jump:
                case SharedSound.Land:
                {
                    var me = Object.FindAnyObjectByType<FirstPersonController>();
                    if (me == null) return null;
                    pitch = me.footstepPitch * (sound == SharedSound.Land ? 0.9f : 1f);
                    return Pick(me.footstepClips, index);
                }
                case SharedSound.Rustle:
                {
                    var plants = Object.FindAnyObjectByType<PlantRustle>();
                    return plants != null ? Pick(plants.clips, index) : null;
                }
                case SharedSound.Sit:
                {
                    var seat = Multiplayer.SeatAt(index);
                    return seat != null ? seat.sitClip : null;
                }
                case SharedSound.ScooterOn:
                case SharedSound.ScooterOff:
                {
                    var scooter = Multiplayer.ScooterAt(index);
                    if (scooter == null) return null;
                    return sound == SharedSound.ScooterOn ? scooter.stepOnClip : scooter.stepOffClip;
                }
            }
            return null;
        }

        static AudioClip Pick(AudioClip[] clips, int index)
        {
            if (clips == null || clips.Length == 0) return null;
            return clips[Mathf.Abs(index) % clips.Length];
        }
    }
}
