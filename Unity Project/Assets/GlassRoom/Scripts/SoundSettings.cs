using System.Collections.Generic;
using UnityEngine;

namespace GlassRoom
{
    /// <summary>The kinds of sound that can be switched off in Settings > Sounds.</summary>
    public enum SoundKind { Music, Footsteps, Scooters, Blood, Chat, Board, Room }

    /// <summary>
    /// Settings > Sounds: each kind of sound on or off (remembered). Every sound in the game is
    /// sorted into a kind by what makes it, and muted while its kind is off.
    /// </summary>
    public class SoundSettings : MonoBehaviour
    {
        public static readonly (SoundKind kind, string label)[] Kinds =
        {
            (SoundKind.Music, "Music"), (SoundKind.Footsteps, "Footsteps & jumps"), (SoundKind.Scooters, "Scooters"),
            (SoundKind.Blood, "Blood & body"), (SoundKind.Chat, "Chat"), (SoundKind.Board, "Board"), (SoundKind.Room, "Room & plants"),
        };

        static readonly Dictionary<AudioSource, SoundKind> s_Kinds = new Dictionary<AudioSource, SoundKind>();
        static SoundSettings s_Instance;
        float m_Next;

        public static bool Enabled(SoundKind kind) => PlayerPrefs.GetInt("Sound" + kind, 1) == 1;

        public static void SetEnabled(SoundKind kind, bool on)
        {
            PlayerPrefs.SetInt("Sound" + kind, on ? 1 : 0);
            PlayerPrefs.Save();
            if (s_Instance != null) s_Instance.m_Next = 0f;
        }

        /// <summary>Says what kind of sound a source plays (when it can't be told from what it's on).</summary>
        public static void Tag(AudioSource source, SoundKind kind) { if (source != null) s_Kinds[source] = kind; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Begin()
        {
            var go = new GameObject("Sound Settings");
            DontDestroyOnLoad(go);
            s_Instance = go.AddComponent<SoundSettings>();
        }

        void Update()
        {
            if (Time.unscaledTime < m_Next) return;
            m_Next = Time.unscaledTime + 0.4f;
            foreach (var source in FindObjectsByType<AudioSource>())
            {
                if (!s_Kinds.TryGetValue(source, out var kind)) s_Kinds[source] = kind = Classify(source);
                bool mute = !Enabled(kind);
                if (source.mute != mute) source.mute = mute;
            }
        }

        static SoundKind Classify(AudioSource source)
        {
            string n = source.gameObject.name;
            if (n.StartsWith("Shared Sound "))
                return n.EndsWith("Scooter") ? SoundKind.Scooters : n.EndsWith("Room") ? SoundKind.Room : SoundKind.Footsteps;
            if (source.GetComponentInParent<MusicSpeaker>() != null) return SoundKind.Music;
            if (source.GetComponentInParent<StandUpScooter>() != null) return SoundKind.Scooters;
            if (source.GetComponentInParent<ChatBox>() != null) return SoundKind.Chat;
            if (source.GetComponentInParent<MarkerBoard>() != null) return SoundKind.Board;
            if (source.GetComponentInParent<VictimBody>() != null) return SoundKind.Blood;
            if (source.GetComponentInParent<FirstPersonController>() != null || source.GetComponentInParent<NetworkPlayer>() != null
                || source.GetComponentInParent<PlayerAvatar>() != null) return SoundKind.Footsteps;
            return SoundKind.Room;
        }
    }
}
