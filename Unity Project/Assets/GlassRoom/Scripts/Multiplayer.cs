using System.Collections.Generic;
using Fusion;
using UnityEngine;

namespace GlassRoom
{
    /// <summary>
    /// Everyone on the website shares one room (Photon Fusion, shared mode): once you press
    /// Enter you join it and appear to everyone else as a NetworkPlayer, and they appear to you.
    /// If the connection fails the game simply carries on just for you. Also answers who is
    /// using what: chairs, the scooter, and whose radio everyone follows.
    /// </summary>
    public class Multiplayer : MonoBehaviour
    {
        [Tooltip("The networked stand-in for each player (Prefabs/Network Player).")]
        public NetworkObject playerPrefab;
        public string sessionName = "glass-room";
        public int maxPlayers = 100;
        /// <summary>Bump whenever NetworkPlayer's shared data changes.</summary>
        public const int NetworkVersion = 5;

        public static Multiplayer Instance { get; private set; }
        /// <summary>Everyone in the room, you included.</summary>
        public static readonly List<NetworkPlayer> Players = new List<NetworkPlayer>();
        /// <summary>You (once connected).</summary>
        public static NetworkPlayer Local { get; internal set; }
        public static int PlayerCount => Players.Count;

        NetworkRunner m_Runner;
        bool m_Started;
        static Seat[] s_Seats;

        void Awake() => Instance = this;

        float m_RetryAt = -1f;
        bool m_Placed;

        /// <summary>
        /// True once the game can start: you've pressed Enter, joined the room (or found you
        /// can't), and everyone already there has shown up, so you know how things stand.
        /// Without a Multiplayer in the scene, as soon as the Enter screen is gone.
        /// </summary>
        public static bool RoomReady => Instance != null ? Instance.m_Ready : !WebPage.Covered;

        /// <summary>True if anyone else in the room has the radio going (you then hear theirs).</summary>
        public static bool SomeoneElseHasMusic
        {
            get
            {
                foreach (var p in Players)
                    if (p != null && !p.IsLocal && p.Object != null && p.Object.IsValid && p.MusicTrack >= 0) return true;
                return false;
            }
        }

        bool m_Ready;
        float m_EnteredAt = -1f, m_JoinedAt = -1f;
        // Joining never holds the game back longer than this; the joined room waits at most this
        // long for the people already there to show up.
        const float JoinTimeout = 10f, ArrivalTimeout = 4f;

        void Update()
        {
            // Join once Enter is pressed (in the editor, straight away).
            if (!m_Started)
            {
                if (!WebPage.Entered || playerPrefab == null) { if (playerPrefab == null && WebPage.Entered) MarkReady(); return; }
                m_Started = true;
                m_EnteredAt = Time.unscaledTime;
                m_LastInput = Time.unscaledTime;
                Connect();
                return;
            }
            if (!m_Ready)
            {
                bool joined = m_JoinedAt >= 0f;
                bool everyoneHere = joined && m_Runner != null && m_Runner.IsRunning && Local != null
                                    && Players.Count >= CountActivePlayers();
                if (everyoneHere || (joined && Time.unscaledTime - m_JoinedAt > ArrivalTimeout)
                    || Time.unscaledTime - m_EnteredAt > JoinTimeout)
                    MarkReady();
            }
            // No rejoining in the background: someone who's been away 5 minutes, or whose
            // connection dropped (a browser pauses a page that's out of sight, and the room lets
            // them go), is stopped and has to reload. That way nobody ends up playing on in a room
            // of their own that looks shared but isn't.
            if (m_Gone) return;
            if (AnyInput()) m_LastInput = Time.unscaledTime;
            if (m_Ready && Time.unscaledTime - m_LastInput > IdleLimit)
                Leave("You were away for 10 minutes, so you've left the room.");
            else if (m_JoinedAt >= 0f && m_Runner != null && !m_Runner.IsRunning && !m_Connecting)
                Leave("You've been disconnected.");
        }

        bool m_Connecting;
        bool m_Gone;
        float m_LastInput;
        const float IdleLimit = 600f;

        // Any key, mouse movement or click, or touch counts as playing.
        static bool AnyInput()
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.anyKey.isPressed) return true;
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null && (mouse.delta.ReadValue().sqrMagnitude > 0f || mouse.leftButton.isPressed)) return true;
            var touch = UnityEngine.InputSystem.Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.isPressed) return true;
            return false;
        }

        void Leave(string message)
        {
            m_Gone = true;
            if (m_Runner != null && m_Runner.IsRunning) m_Runner.Shutdown();
            WebPage.ShowGone(message + " Reload to play again.");
        }

        void MarkReady()
        {
            if (m_Ready) return;
            m_Ready = true;
            WebPage.Reveal();
        }

        int CountActivePlayers()
        {
            int n = 0;
            foreach (var _ in m_Runner.ActivePlayers) n++;
            return n;
        }

        async void Connect()
        {
            m_Connecting = true;
            var go = new GameObject("Network Runner");
            m_Runner = go.AddComponent<NetworkRunner>();
            m_Runner.ProvideInput = false;
            StartGameResult result;
            try
            {
                result = await m_Runner.StartGame(new StartGameArgs
                {
                    GameMode = GameMode.Shared,
                    // Each version of what players share gets its own room, so an older copy of the
                    // website still open somewhere never ends up in a room with a newer one.
                    SessionName = sessionName + "-v" + NetworkVersion,
                    PlayerCount = maxPlayers,
                    SceneManager = go.AddComponent<NetworkSceneManagerDefault>(),
                });
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("Multiplayer: couldn't connect: " + e.Message);
                m_Connecting = false;
                MarkReady();
                return;
            }
            m_Connecting = false;
            if (!result.Ok)
            {
                Debug.LogWarning("Multiplayer: couldn't join: " + result.ShutdownReason);
                MarkReady();
                return;
            }
            var me = FindAnyObjectByType<FirstPersonController>();
            // Everyone starts at their own spot around the entrance, facing into the hall, so
            // nobody appears inside anyone else. (Only the first time: after a reconnect you
            // stay where you were.)
            if (me != null && !m_Placed)
            {
                m_Placed = true;
                int slot = Mathf.Max(0, m_Runner.LocalPlayer.PlayerId - 1) % 12;
                Vector3 start = me.transform.position;
                // Rows of three a couple of metres into the hall.
                Vector3 offset = slot == 0 ? Vector3.zero : new Vector3(((slot - 1) % 3 - 1) * 1.6f, 0f, 2.2f + (slot - 1) / 3 * 1.6f);
                me.Teleport(start + Quaternion.Euler(0f, me.Yaw, 0f) * offset, me.Yaw);
            }
            Vector3 at = me != null ? me.transform.position : Vector3.zero;
            m_Runner.Spawn(playerPrefab, at, Quaternion.identity, m_Runner.LocalPlayer);
            if (m_JoinedAt < 0f) m_JoinedAt = Time.unscaledTime;
        }

        /// <summary>For testing: logs where everyone is (unityGame.SendMessage("Multiplayer", "Dump")).</summary>
        public void Dump()
        {
            var me = FindAnyObjectByType<FirstPersonController>();
            Debug.Log($"Multiplayer dump: me at {(me != null ? me.transform.position.ToString("F2") : "-")} yaw {(me != null ? me.Yaw : 0f):F0}, running {(m_Runner != null && m_Runner.IsRunning)}, {Players.Count} players");
            foreach (var p in Players)
                if (p != null) Debug.Log("  " + p.Describe());
            var radio = FindAnyObjectByType<MusicSpeaker>();
            if (BloodTracks.Instance != null) Debug.Log($"  marks {BloodTracks.Instance.MarkCount}, from others {BloodTracks.Instance.RemoteMarks}");
            if (radio != null) Debug.Log($"  radio on {radio.IsOn} track {radio.CurrentTrack} at {radio.Position:F1}s");
            foreach (var sc in Scooters) Debug.Log($"  {sc.name} at {sc.transform.position:F2} yaw {sc.transform.eulerAngles.y:F0} remote {sc.RemoteControlled}");
        }

        /// <summary>For testing: walks for a while, e.g. SendMessage("Multiplayer", "TestWalk", "0 1 2 run").</summary>
        public void TestWalk(string args)
        {
            var a = args.Split(' ');
            FirstPersonController.TestMove = new Vector2(float.Parse(a[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture));
            FirstPersonController.TestRun = a.Length > 3 && a[3] == "run";
            CancelInvoke(nameof(StopTestWalk));
            Invoke(nameof(StopTestWalk), float.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture));
        }

        /// <summary>For testing: "sit", "stand", "ride", "off", "radio", "skip", or "tp x z yaw".</summary>
        public void TestAct(string what)
        {
            var me = FindAnyObjectByType<FirstPersonController>();
            var radio = FindAnyObjectByType<MusicSpeaker>();
            var a = what.Split(' ');
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var board = MarkerBoard.Instance;
            if (a[0] == "mode") InputModeManager.Mode = a.Length > 1 && a[1] == "mobile" ? ControlMode.Touchscreen : ControlMode.KeyboardMouse;
            else if (a[0] == "write" && board != null) board.Write(what.Substring(6), new Rect(60f, 280f, 520f, 160f), 1);
            else if (a[0] == "draw" && board != null)
            {
                var line = new List<Vector2>();
                for (int i = 0; i <= 60; i++) line.Add(new Vector2(600f + i * 6f, 200f + Mathf.Sin(i * 0.25f) * 90f));
                board.Draw(new List<List<Vector2>> { line }, 2, 1);
            }
            else if (a[0] == "erase" && board != null) board.EraseAll();
            else if (a[0] == "panel" && BoardPanel.Instance != null) BoardPanel.Instance.Open();
            else if (a[0] == "near" && me != null) Debug.Log("Multiplayer near: " + me.DebugNear());
            else if (a[0] == "radio" && radio != null) radio.TogglePlayPause();
            else if (a[0] == "skip" && radio != null) radio.Skip();
            else if (a[0] == "tp" && me != null) me.Teleport(new Vector3(float.Parse(a[1], inv), 0.05f, float.Parse(a[2], inv)), float.Parse(a[3], inv));
            else if (a[0] == "hit" && me != null) me.TakeHit(Quaternion.Euler(0f, float.Parse(a[1], inv), 0f) * Vector3.forward, float.Parse(a[2], inv));
            else if (me != null) me.TestAct(a[0]);
            Dump();
        }

        void StopTestWalk() => FirstPersonController.TestMove = Vector2.zero;

        void OnDestroy()
        {
            if (m_Runner != null && m_Runner.IsRunning) m_Runner.Shutdown();
            if (Instance == this) Instance = null;
        }

        // ---- Chairs ----

        /// <summary>A chair's number, the same for everyone (by where it stands).</summary>
        public static int SeatIndex(Seat seat) => seat == null ? -1 : System.Array.IndexOf(Seats, seat);

        static Seat[] Seats
        {
            get
            {
                if (s_Seats == null || s_Seats.Length == 0 || System.Array.IndexOf(s_Seats, null) >= 0)
                {
                    s_Seats = FindObjectsByType<Seat>(FindObjectsInactive.Exclude);
                    System.Array.Sort(s_Seats, (a, b) =>
                    {
                        Vector3 pa = a.transform.position, pb = b.transform.position;
                        int c = pa.x.CompareTo(pb.x);
                        return c != 0 ? c : pa.z.CompareTo(pb.z);
                    });
                }
                return s_Seats;
            }
        }

        public static bool SeatTakenByOther(Seat seat)
        {
            int index = SeatIndex(seat);
            if (index < 0) return false;
            foreach (var p in Players)
                if (p != null && !p.IsLocal && p.Seated && p.SeatIndex == index) return true;
            return false;
        }

        // ---- Scooters ----

        /// <summary>How many scooters the network keeps track of.</summary>
        public const int MaxScooters = 4;
        static StandUpScooter[] s_Scooters;

        /// <summary>The scooters, numbered the same for everyone (by where they start out).</summary>
        public static StandUpScooter[] Scooters
        {
            get
            {
                if (s_Scooters == null || System.Array.IndexOf(s_Scooters, null) >= 0)
                {
                    var found = FindObjectsByType<StandUpScooter>(FindObjectsInactive.Exclude);
                    System.Array.Sort(found, (a, b) => string.CompareOrdinal(a.name, b.name));
                    if (found.Length > MaxScooters) System.Array.Resize(ref found, MaxScooters);
                    s_Scooters = found;
                }
                return s_Scooters;
            }
        }

        public static int ScooterIndex(StandUpScooter scooter) => scooter == null ? -1 : System.Array.IndexOf(Scooters, scooter);
        public static StandUpScooter ScooterAt(int index) => index >= 0 && index < Scooters.Length ? Scooters[index] : null;

        public static bool ScooterRiddenByOther(StandUpScooter scooter)
        {
            int index = ScooterIndex(scooter);
            if (index < 0) return false;
            foreach (var p in Players)
                if (p != null && !p.IsLocal && p.Riding && p.RideIndex == index) return true;
            return false;
        }

        /// <summary>You've stepped on this scooter: from now on everyone follows yours.</summary>
        public static void ClaimScooter(StandUpScooter scooter)
        {
            int index = ScooterIndex(scooter);
            if (Local != null && index >= 0) Local.ClaimScooter(index, HighestScooterVersion(index) + 1);
        }

        public static int HighestScooterVersion(int index)
        {
            int v = 0;
            foreach (var p in Players) if (p != null && p.Object != null && p.Object.IsValid) v = Mathf.Max(v, p.ScooterVersions[index]);
            return v;
        }

        /// <summary>
        /// Whose copy of a scooter everyone follows: whoever stepped on it last (the earliest
        /// player to join, if nobody has yet).
        /// </summary>
        public static NetworkPlayer ScooterOwner(int index) => Best(p => p.ScooterVersions[index]);

        /// <summary>A chair by its number.</summary>
        public static Seat SeatAt(int index) => index >= 0 && index < Seats.Length ? Seats[index] : null;

        // ---- Radio ----

        public static int HighestMusicVersion()
        {
            int v = 0;
            foreach (var p in Players) if (p != null) v = Mathf.Max(v, p.MusicVersion);
            return v;
        }

        /// <summary>Whose radio everyone follows: whoever pressed it last (else the earliest to join).</summary>
        public static NetworkPlayer MusicReference() => Best(p => p.MusicVersion);

        /// <summary>The player who has been in the room longest (lowest number).</summary>
        public static NetworkPlayer Oldest() => Best(p => 0);

        static NetworkPlayer Best(System.Func<NetworkPlayer, int> version)
        {
            NetworkPlayer best = null;
            foreach (var p in Players)
            {
                if (p == null || p.Object == null || !p.Object.IsValid) continue;
                if (best == null) { best = p; continue; }
                int a = version(p), b = version(best);
                if (a > b || (a == b && p.PlayerId < best.PlayerId)) best = p;
            }
            return best;
        }
    }
}
