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

        void Update()
        {
            // Join once the website's Enter screen is gone (in the editor, straight away).
            if (!m_Started)
            {
                if (WebPage.Covered || playerPrefab == null) return;
                m_Started = true;
                Connect();
                return;
            }
            // Dropped (a browser pauses a page that's in the background, and the connection
            // times out): rejoin a few seconds after coming back.
            if (m_Runner != null && !m_Runner.IsRunning && m_RetryAt < 0f && !m_Connecting)
                m_RetryAt = Time.unscaledTime + 3f;
            if (m_RetryAt >= 0f && Time.unscaledTime >= m_RetryAt)
            {
                m_RetryAt = -1f;
                if (m_Runner != null) Destroy(m_Runner.gameObject);
                m_Runner = null;
                Connect();
            }
        }

        bool m_Connecting;

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
                    SessionName = sessionName,
                    PlayerCount = maxPlayers,
                    SceneManager = go.AddComponent<NetworkSceneManagerDefault>(),
                });
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("Multiplayer: couldn't connect: " + e.Message);
                m_Connecting = false;
                m_RetryAt = Time.unscaledTime + 10f;
                return;
            }
            m_Connecting = false;
            if (!result.Ok)
            {
                Debug.LogWarning("Multiplayer: couldn't join: " + result.ShutdownReason);
                m_RetryAt = Time.unscaledTime + 10f;
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
            if (a[0] == "radio" && radio != null) radio.TogglePlayPause();
            else if (a[0] == "skip" && radio != null) radio.Skip();
            else if (a[0] == "tp" && me != null) me.Teleport(new Vector3(float.Parse(a[1], inv), me.transform.position.y, float.Parse(a[2], inv)), float.Parse(a[3], inv));
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
