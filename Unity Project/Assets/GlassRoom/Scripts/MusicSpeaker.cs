using UnityEngine;

namespace GlassRoom
{
    /// <summary>
    /// A speaker with a playlist. It always starts with the first track, then plays the others in
    /// random order (never the same one twice in a row), moving on when a track ends. It can be
    /// paused and resumed, or skipped to the next track. Its light shows green while playing and
    /// red while paused.
    /// </summary>
    public class MusicSpeaker : MonoBehaviour
    {
        [Tooltip("Always played first when the game starts.")]
        public AudioClip firstTrack;
        [Tooltip("Played in random order after the first track.")]
        public AudioClip[] playlist;
        [Range(0f, 1f)] public float volume = 0.8f;
        public bool playOnStart = true;
        [Tooltip("Within this distance the music plays at full volume, in metres.")]
        public float fullVolumeDistance = 7f;
        [Tooltip("Beyond this distance the music stops getting quieter, in metres.")]
        public float maxDistance = 90f;

        [Header("Status light")]
        public Renderer statusLight;
        public Light statusGlow;
        public Material onMaterial;
        public Material offMaterial;
        public Color onColor = new Color(0.25f, 1f, 0.4f);
        public Color offColor = new Color(1f, 0.15f, 0.1f);

        AudioSource m_Source;
        bool m_Paused = true;
        // Seconds of the current track that have played. A track only counts as finished once
        // about its whole length has played: in a web browser the music streams, and a brief
        // hiccup (for example while walking) can make it look stopped for a moment. Treating that
        // as "finished" used to load a new song every frame, which stopped the music and lagged.
        float m_Played;

        /// <summary>True while music is playing (not paused).</summary>
        public bool IsOn => !m_Paused;

        void Awake()
        {
            m_Source = gameObject.AddComponent<AudioSource>();
            m_Source.loop = false;
            m_Source.playOnAwake = false;
            m_Source.volume = volume;
            // Heard from where the speaker stands: full volume nearby, fading gently across the hall.
            m_Source.spatialBlend = 1f;
            m_Source.rolloffMode = AudioRolloffMode.Logarithmic;
            m_Source.minDistance = fullVolumeDistance;
            m_Source.maxDistance = maxDistance;
        }

        void Start()
        {
            m_Source.clip = firstTrack != null ? firstTrack : PickNext();
            if (playOnStart) Resume();
            else ShowState();
        }

        void Update()
        {
            if (m_Paused || m_Source.clip == null) return;
            if (m_Source.isPlaying)
            {
                m_Played += Time.unscaledDeltaTime;
                return;
            }
            // When a track finishes on its own, move on to another one.
            if (m_Played >= m_Source.clip.length - 1f)
                PlayClip(PickNext());
        }

        public void TogglePlayPause()
        {
            if (m_Paused) Resume();
            else Pause();
        }

        /// <summary>Skips to a random different track, and starts playing if paused.</summary>
        public void Skip() => PlayClip(PickNext());

        void Resume()
        {
            if (m_Source.clip == null) m_Source.clip = PickNext();
            if (m_Source.clip == null) return;
            if (m_Source.time > 0f) m_Source.UnPause();
            else m_Source.Play();
            m_Paused = false;
            ShowState();
        }

        void Pause()
        {
            m_Source.Pause();
            m_Paused = true;
            ShowState();
        }

        void PlayClip(AudioClip clip)
        {
            if (clip == null) return;
            m_Source.clip = clip;
            m_Source.time = 0f;
            m_Played = 0f;
            m_Source.Play();
            m_Paused = false;
            ShowState();
        }

        AudioClip PickNext()
        {
            var options = new System.Collections.Generic.List<AudioClip>();
            if (playlist != null)
                foreach (var clip in playlist)
                    if (clip != null && clip != m_Source.clip) options.Add(clip);
            if (options.Count == 0) return m_Source.clip != null ? m_Source.clip : firstTrack;
            return options[Random.Range(0, options.Count)];
        }

        void ShowState()
        {
            if (statusLight != null) statusLight.sharedMaterial = IsOn ? onMaterial : offMaterial;
            if (statusGlow != null) statusGlow.color = IsOn ? onColor : offColor;
        }
    }
}
