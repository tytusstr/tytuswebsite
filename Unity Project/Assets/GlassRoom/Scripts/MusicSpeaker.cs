using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace GlassRoom
{
    /// <summary>
    /// A speaker with a playlist. It always starts with the first track, then plays the others in
    /// random order (never the same one twice in a row), moving on when a track ends. It can be
    /// paused and resumed, or skipped to the next track. Its light shows green while playing and
    /// red while paused.
    ///
    /// In a web browser the songs stream from the website's files (see <see cref="webTracks"/>)
    /// through the browser's own audio player, and get quieter with distance. Unity would
    /// otherwise unpack each whole song into memory and run it through 3D sound every frame you
    /// move, which made phones lag and drop the music while walking.
    /// </summary>
    public class MusicSpeaker : MonoBehaviour
    {
        [Tooltip("Always played first when the game starts.")]
        public AudioClip firstTrack;
        [Tooltip("Played in random order after the first track.")]
        public AudioClip[] playlist;
        [Tooltip("Website build only: song file names in StreamingAssets/Music, first track first. Filled in by Tools > Glass Room > Build Website.")]
        public string[] webTracks;
        [Range(0f, 1f)] public float volume = 0.8f;
        [Tooltip("Played when a button is pressed: play, pause or skip.")]
        public AudioClip clickClip;
        [Range(0f, 1f)] public float clickVolume = 0.6f;
        public bool playOnStart = true;
        [Tooltip("Within this distance the music plays at full volume, in metres.")]
        public float fullVolumeDistance = 4.9f;
        [Tooltip("Beyond this distance the music stops getting quieter, in metres.")]
        public float maxDistance = 63f;

        [Header("Status light")]
        public Renderer statusLight;
        public Light statusGlow;
        public Material onMaterial;
        public Material offMaterial;
        public Color onColor = new Color(0.25f, 1f, 0.4f);
        public Color offColor = new Color(1f, 0.15f, 0.1f);

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void WebMusic_Play(string url);
        [DllImport("__Internal")] static extern void WebMusic_Pause();
        [DllImport("__Internal")] static extern void WebMusic_Resume();
        [DllImport("__Internal")] static extern void WebMusic_SetVolume(float volume);
        [DllImport("__Internal")] static extern int WebMusic_Ended();
        [DllImport("__Internal")] static extern void WebMusic_Seek(float seconds);
        [DllImport("__Internal")] static extern float WebMusic_Position();
        static bool UseWeb(MusicSpeaker s) => s.webTracks != null && s.webTracks.Length > 0;
#else
        static void WebMusic_Play(string url) { }
        static void WebMusic_Pause() { }
        static void WebMusic_Resume() { }
        static void WebMusic_SetVolume(float volume) { }
        static int WebMusic_Ended() => 0;
        static void WebMusic_Seek(float seconds) { }
        static float WebMusic_Position() => 0f;
        static bool UseWeb(MusicSpeaker s) => false;
#endif

        AudioSource m_Source;
        AudioSource m_Click;
        // A press waiting for its click to finish before the music starts.
        Coroutine m_Pending;
        bool m_Paused = true;
        bool m_Web;
        // Index of the current track: 0 is the first track, the rest are the playlist.
        int m_Current = -1;
        // Seconds of the current track that have played. A track only counts as finished once
        // about its whole length has played, so a brief hiccup never skips to another song.
        float m_Played;
        float m_AppliedWebVolume = -1f;
        float m_LastPress = -1f;

        /// <summary>True while music is playing (not paused).</summary>
        public bool IsOn => !m_Paused;

        // ---- Shared with other players (see NetworkPlayer) ----

        /// <summary>Raised when someone here presses play/pause or skip.</summary>
        public event System.Action UserChanged;
        /// <summary>The song playing (or paused): 0 is the first track.</summary>
        public int CurrentTrack => m_Current;
        /// <summary>How far into the current song it is, in seconds.</summary>
        public float Position
        {
            get
            {
                if (m_Current < 0) return 0f;
                if (m_Web) return WebMusic_Position();
                return m_Source != null ? m_Source.time : 0f;
            }
        }

        /// <summary>
        /// Matches another player's radio: same song, same moment, playing or paused. Quietly
        /// (no click), and only jumps if it's noticeably off, so it never stutters.
        /// </summary>
        public void ApplyShared(bool on, int track, float position)
        {
            if (!HasTrack(track)) return;
            if (m_Pending != null) { StopCoroutine(m_Pending); m_Pending = null; }
            if (track != m_Current) { PlayTrack(track, position); m_LastSeek = Time.unscaledTime; }
            // Jumps only when clearly out of step, and not again while a jump is still settling.
            else if (Mathf.Abs(Position - position) > 2f && Time.unscaledTime - m_LastSeek > 3f)
            {
                Seek(position);
                m_LastSeek = Time.unscaledTime;
            }
            if (on && m_Paused) Resume();
            else if (!on && !m_Paused) Pause();
        }

        float m_LastSeek = -10f;

        void Seek(float position)
        {
            if (m_Web) WebMusic_Seek(position);
            else if (m_Source != null && m_Source.clip != null) m_Source.time = Mathf.Clamp(position, 0f, m_Source.clip.length - 0.05f);
            m_Played = position;
        }

        int TrackCount => m_Web ? webTracks.Length : 1 + (playlist != null ? playlist.Length : 0);

        void Awake()
        {
            m_Web = UseWeb(this);
            m_Click = gameObject.AddComponent<AudioSource>();
            m_Click.playOnAwake = false;
            m_Click.loop = false;
            m_Click.spatialBlend = 0f;
            if (m_Web) return;
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

        // Nothing plays until you've pressed Enter and the game knows whether anyone else is in
        // the room. Then: if someone else already has the radio going you hear theirs (see
        // NetworkPlayer); otherwise it starts the first song from the beginning.
        bool m_Started;

        void Start() => ShowState();

        void Update()
        {
            if (!m_Started)
            {
                if (m_Current >= 0) m_Started = true; // already following someone else's radio
                else if (Multiplayer.RoomReady)
                {
                    m_Started = true;
                    if (playOnStart && !Multiplayer.SomeoneElseHasMusic) PlayTrack(HasTrack(0) ? 0 : PickNext());
                }
                return;
            }
            if (m_Paused || m_Current < 0 || m_Pending != null) return;
            if (m_Web)
            {
                UpdateWebVolume();
                if (WebMusic_Ended() != 0) PlayTrack(NextAfter(m_Current));
                return;
            }
            if (m_Source.isPlaying)
            {
                m_Played += Time.unscaledDeltaTime;
                return;
            }
            // When a track finishes on its own, move on to another one.
            var clip = m_Source.clip;
            if (clip == null || m_Played >= clip.length - 1f)
                PlayTrack(NextAfter(m_Current));
        }

        // Same fall-off as the computer version: full volume within fullVolumeDistance, then
        // quieter in proportion to distance. Only sent to the browser when it changes noticeably.
        void UpdateWebVolume()
        {
            var cam = Camera.main;
            float distance = cam != null ? Vector3.Distance(cam.transform.position, transform.position) : 0f;
            distance = Mathf.Clamp(distance, fullVolumeDistance, maxDistance);
            float target = volume * fullVolumeDistance / distance;
            if (Mathf.Abs(target - m_AppliedWebVolume) < 0.01f) return;
            m_AppliedWebVolume = target;
            WebMusic_SetVolume(target);
        }

        // Play: click, then the music. Pause: the music stops, then click.
        public void TogglePlayPause()
        {
            if (TooSoon()) return;
            if (m_Paused) AfterClick(Resume);
            else
            {
                Pause();
                Click();
            }
            UserChanged?.Invoke();
        }

        /// <summary>Skips to a random different track. Does nothing while paused.</summary>
        public void Skip()
        {
            if (m_Paused || TooSoon()) return;
            // The music stops, a click, then the next song.
            StopForSkip();
            int next = PickNext();
            AfterClick(() => { PlayTrack(next); UserChanged?.Invoke(); });
        }

        // A single tap can arrive twice on some phones; presses closer together than this are
        // treated as one, so the music never flips between playing and paused.
        bool TooSoon()
        {
            if (Time.unscaledTime - m_LastPress < 0.3f) return true;
            m_LastPress = Time.unscaledTime;
            return false;
        }

        void Click()
        {
            if (clickClip != null) m_Click.PlayOneShot(clickClip, clickVolume);
        }

        // Clicks now, and runs the action once the click has finished. The speaker shows its
        // new state straight away.
        void AfterClick(System.Action action)
        {
            if (m_Pending != null) StopCoroutine(m_Pending);
            Click();
            m_Paused = false;
            ShowState();
            m_Pending = StartCoroutine(RunAfter(clickClip != null ? clickClip.length : 0f, action));
        }

        System.Collections.IEnumerator RunAfter(float seconds, System.Action action)
        {
            yield return new WaitForSecondsRealtime(seconds);
            m_Pending = null;
            m_Paused = true; // the action decides
            action();
        }

        // Silences the current song before a skip (it stays "on").
        void StopForSkip()
        {
            if (m_Web) WebMusic_Pause();
            else m_Source.Stop();
        }

        void Resume()
        {
            if (m_Current < 0) { PlayTrack(HasTrack(0) ? 0 : PickNext()); return; }
            if (m_Web) WebMusic_Resume();
            else if (m_Source.time > 0f) m_Source.UnPause();
            else m_Source.Play();
            m_Paused = false;
            ShowState();
        }

        void Pause()
        {
            if (m_Pending != null) { StopCoroutine(m_Pending); m_Pending = null; }
            if (m_Web) WebMusic_Pause();
            else m_Source.Pause();
            m_Paused = true;
            ShowState();
        }

        void PlayTrack(int index) => PlayTrack(index, 0f);

        void PlayTrack(int index, float startAt)
        {
            if (!HasTrack(index)) return;
            m_Current = index;
            m_Played = startAt;
            if (m_Web)
            {
                string url = Application.streamingAssetsPath + "/Music/" + System.Uri.EscapeDataString(webTracks[index]);
                WebMusic_Play(url);
                if (startAt > 0.5f) WebMusic_Seek(startAt);
                m_AppliedWebVolume = -1f;
                UpdateWebVolume();
            }
            else
            {
                m_Source.clip = ClipAt(index);
                m_Source.time = m_Source.clip != null ? Mathf.Clamp(startAt, 0f, m_Source.clip.length - 0.05f) : 0f;
                m_Source.Play();
            }
            m_Paused = false;
            ShowState();
        }

        AudioClip ClipAt(int index) => index == 0 ? firstTrack : playlist[index - 1];

        bool HasTrack(int index)
        {
            if (index < 0 || index >= TrackCount) return false;
            return m_Web ? !string.IsNullOrEmpty(webTracks[index]) : ClipAt(index) != null;
        }

        // When a song ends by itself, the next one follows from the one that finished, the same
        // way for everyone, so players listening together stay on the same song.
        int NextAfter(int current)
        {
            var options = new System.Collections.Generic.List<int>();
            for (int i = 1; i < TrackCount; i++)
                if (i != current && HasTrack(i)) options.Add(i);
            if (options.Count == 0) return HasTrack(current) ? current : 0;
            return options[(int)((uint)(current * 2654435761u + 97u) % (uint)options.Count)];
        }

        // A random track other than the first one and the one playing now.
        int PickNext()
        {
            var options = new System.Collections.Generic.List<int>();
            for (int i = 1; i < TrackCount; i++)
                if (i != m_Current && HasTrack(i)) options.Add(i);
            if (options.Count == 0) return HasTrack(m_Current) ? m_Current : 0;
            return options[Random.Range(0, options.Count)];
        }

        void ShowState()
        {
            if (statusLight != null) statusLight.sharedMaterial = IsOn ? onMaterial : offMaterial;
            if (statusGlow != null) statusGlow.color = IsOn ? onColor : offColor;
        }
    }
}
