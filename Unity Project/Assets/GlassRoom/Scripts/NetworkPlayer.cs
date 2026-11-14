using System.Collections;
using Fusion;
using UnityEngine;

namespace GlassRoom
{
    /// <summary>
    /// One person in the shared room. Yours shares where you are and what you're doing (walking,
    /// running, jumping, sitting in which chair, riding which scooter), where your head is
    /// looking, which person you look like, the scooters as you last left them, what the radio
    /// is playing, and the sounds you make. Everyone else's shows up here as a real person
    /// (PlayerAvatar) moving smoothly, with their footsteps and other sounds, their bloody prints
    /// and tracks, and their squelches. Someone joining later is sent the prints already made.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class NetworkPlayer : NetworkBehaviour
    {
        [Networked] public int AvatarIndex { get; set; }
        /// <summary>The name typed on the Enter screen, shown above their head.</summary>
        [Networked] public NetworkString<_32> PlayerName { get; set; }
        /// <summary>Movement relative to their body (x sideways, y forward), for sidesteps and walking backwards.</summary>
        [Networked] public Vector2 Motion { get; set; }
        [Networked] public NetworkBool Airborne { get; set; }
        /// <summary>Knocked over by a scooter: how many times so far, whether they're down now, and which way they fell.</summary>
        [Networked] public int Knocks { get; set; }
        [Networked] public NetworkBool IsDown { get; set; }
        [Networked] public NetworkBool FellBackwards { get; set; }
        [Networked] public float Speed { get; set; }
        [Networked] public NetworkBool Seated { get; set; }
        [Networked] public int SeatIndex { get; set; }
        [Networked] public NetworkBool Riding { get; set; }
        [Networked] public int RideIndex { get; set; }
        [Networked] public float HeadYaw { get; set; }
        [Networked] public float HeadPitch { get; set; }

        // The scooters as this player last left them (everyone follows the newest of each).
        [Networked, Capacity(Multiplayer.MaxScooters)] public NetworkArray<int> ScooterVersions => default;
        [Networked, Capacity(Multiplayer.MaxScooters)] public NetworkArray<Vector3> ScooterPositions => default;
        [Networked, Capacity(Multiplayer.MaxScooters)] public NetworkArray<float> ScooterYaws => default;
        [Networked, Capacity(Multiplayer.MaxScooters)] public NetworkArray<float> ScooterSpeeds => default;

        // The radio as this player hears it (everyone follows whoever pressed it last).
        [Networked] public int MusicVersion { get; set; }
        [Networked] public int MusicTrack { get; set; }
        [Networked] public NetworkBool MusicOn { get; set; }
        [Networked] public float MusicPosition { get; set; }

        public bool IsLocal => Object != null && Object.HasStateAuthority;
        public int PlayerId => Object != null ? Object.StateAuthority.PlayerId : int.MaxValue;

        FirstPersonController m_Me;
        float m_NextNameCheck;
        CapsuleCollider m_Body;
        LocalPlayerBody m_MyBody;
        MusicSpeaker m_Radio;
        PlayerAvatar m_Avatar;
        int m_AvatarShown = -1;
        AudioSource m_Steps;
        float m_StepDistance;
        Vector3 m_LastPosition;
        bool m_LeftFoot;

        float m_SpawnedAt;

        public override void Spawned()
        {
            m_SpawnedAt = Time.unscaledTime;
            Multiplayer.Players.Add(this);
            m_Me = FindAnyObjectByType<FirstPersonController>();
            m_Radio = FindAnyObjectByType<MusicSpeaker>();
            m_LastPosition = transform.position;
            if (HasStateAuthority)
            {
                Multiplayer.Local = this;
                PlayerName = WebPage.PlayerName;
                m_MyBody = m_Me != null ? m_Me.GetComponent<LocalPlayerBody>() : null;
                SeatIndex = -1;
                RideIndex = -1;
                MusicTrack = -1;
                if (m_Radio != null) m_Radio.UserChanged += OnRadioPressed;
                BloodTracks.MarkPlaced += OnMarkPlaced;
                BloodTracks.Squelched += OnSquelched;
                SharedSounds.Played += OnSound;
                ChatBox.Sent += OnChat;
                MarkerBoard.TextRequested += OnBoardText;
                MarkerBoard.StrokeRequested += OnBoardStroke;
                MarkerBoard.EraseRequested += OnBoardErase;
                VictimBody.PoseShared += OnBodyPose;
                VictimBody.StainShared += OnBodyStain;
                VictimBody.SoundShared += OnBodySound;
            }
            else
            {
                Debug.Log($"Multiplayer: player {PlayerId} is here ({Multiplayer.PlayerCount} in the room)");
                // Their footsteps, heard from where they are.
                m_Steps = gameObject.AddComponent<AudioSource>();
                m_Steps.playOnAwake = false;
                m_Steps.spatialBlend = 1f;
                m_Steps.minDistance = 1.5f;
                m_Steps.maxDistance = 30f;
                m_Steps.rolloffMode = AudioRolloffMode.Logarithmic;
                // Someone arriving while you're here: a notice in the chat, once their name is in.
                if (Multiplayer.RoomReady) StartCoroutine(AnnounceJoin());
                // Someone new: if you've been here longest, pass on the prints already made.
                var me = Multiplayer.Local;
                if (me != null && me != this && PlayerId > me.PlayerId && me == Multiplayer.Oldest())
                    me.StartCoroutine(me.SendMarksTo(Object.StateAuthority));
            }
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            Multiplayer.Players.Remove(this);
            if (Multiplayer.Local == this) Multiplayer.Local = null;
            if (m_Radio != null) m_Radio.UserChanged -= OnRadioPressed;
            BloodTracks.MarkPlaced -= OnMarkPlaced;
            BloodTracks.Squelched -= OnSquelched;
            SharedSounds.Played -= OnSound;
            ChatBox.Sent -= OnChat;
            MarkerBoard.TextRequested -= OnBoardText;
            MarkerBoard.StrokeRequested -= OnBoardStroke;
            MarkerBoard.EraseRequested -= OnBoardErase;
            VictimBody.PoseShared -= OnBodyPose;
            VictimBody.StainShared -= OnBodyStain;
            VictimBody.SoundShared -= OnBodySound;
            if (m_Avatar != null) Destroy(m_Avatar.gameObject);
        }

        /// <summary>Their person in the room (null until shown).</summary>
        public PlayerAvatar Avatar => m_Avatar;

        public string Describe() =>
            $"player {PlayerId} '{PlayerName}' local {IsLocal} at {transform.position:F2} yaw {transform.eulerAngles.y:F0} head {HeadYaw:F0}/{HeadPitch:F0} speed {Speed:F1} seated {Seated} ({SeatIndex}) riding {Riding} ({RideIndex}) avatar {(m_Avatar != null ? m_Avatar.transform.position.ToString("F2") + " yaw " + m_Avatar.transform.eulerAngles.y.ToString("F0") : "none")}";

        // ---- You: share your state every network tick ----

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority || m_Me == null) return;
            AvatarIndex = m_MyBody != null ? m_MyBody.AvatarIndex : 0;
            PlayerAvatar.Placement(m_Me.transform.position, m_Me.BodyYaw, m_Me.CurrentSeat, m_Me.CurrentRide, out var feet, out var yaw);
            transform.SetPositionAndRotation(feet, Quaternion.Euler(0f, yaw, 0f));
            Speed = m_Me.GroundSpeed;
            Motion = m_Me.LocalMotion;
            Airborne = m_Me.Airborne;
            Knocks = m_Me.KnockCount;
            IsDown = m_Me.Lying;
            FellBackwards = m_Me.DownBackwards;
            Seated = m_Me.CurrentSeat != null;
            SeatIndex = Seated ? Multiplayer.SeatIndex(m_Me.CurrentSeat) : -1;
            Riding = m_Me.CurrentRide != null;
            RideIndex = Riding ? Multiplayer.ScooterIndex(m_Me.CurrentRide) : -1;
            HeadYaw = m_Me.HeadYaw;
            // Your name, if you change it in the settings (checked about once a second).
            if (Time.unscaledTime >= m_NextNameCheck)
            {
                m_NextNameCheck = Time.unscaledTime + 1f;
                string name = WebPage.PlayerName;
                if (PlayerName.ToString() != name) PlayerName = name;
            }
            HeadPitch = m_Me.HeadPitch;

            // Every scooter as you see it, shared. Everyone follows the copy of whoever stepped on it
            // last; everyone else keeps a copy of where it is (one version lower), so if that
            // person leaves, the next one takes over from the same spot and nothing jumps back.
            var scooters = Multiplayer.Scooters;
            for (int i = 0; i < scooters.Length; i++)
            {
                var scooter = scooters[i];
                if (scooter == null) continue;
                var owner = Multiplayer.ScooterOwner(i);
                if (owner == this)
                {
                    if (scooter.RemoteControlled && m_Me.CurrentRide != scooter) scooter.TakeBackControl();
                }
                else if (owner != null)
                {
                    int follow = Mathf.Max(0, owner.ScooterVersions[i] - 1);
                    if (ScooterVersions[i] < follow) ScooterVersions.Set(i, follow);
                }
                ScooterPositions.Set(i, scooter.transform.position);
                ScooterYaws.Set(i, scooter.transform.eulerAngles.y);
                ScooterSpeeds.Set(i, scooter.Speed);
            }
            SyncRadio();
        }

        public void ClaimScooter(int index, int version) => ScooterVersions.Set(index, version);

        // Follow whoever pressed the radio last (or the first person in the room), quietly and
        // only when ours is noticeably different; then share what ours is doing.
        void SyncRadio()
        {
            if (m_Radio == null) return;
            var reference = Multiplayer.MusicReference();
            if (reference != null && reference != this && reference.MusicTrack >= 0)
            {
                bool differs = reference.MusicTrack != m_Radio.CurrentTrack || (bool)reference.MusicOn != m_Radio.IsOn
                               || Mathf.Abs(reference.MusicPosition - m_Radio.Position) > 2.5f;
                if (differs || reference.MusicVersion > MusicVersion)
                {
                    // A moment later than they reported, for the time the message took.
                    m_Radio.ApplyShared(reference.MusicOn, reference.MusicTrack, reference.MusicPosition + (reference.MusicOn ? 0.15f : 0f));
                    MusicVersion = reference.MusicVersion;
                }
            }
            MusicTrack = m_Radio.CurrentTrack;
            MusicOn = m_Radio.IsOn;
            MusicPosition = m_Radio.Position;
        }

        void OnRadioPressed()
        {
            if (Object != null && Object.IsValid && HasStateAuthority) MusicVersion = Multiplayer.HighestMusicVersion() + 1;
        }

        // Your prints, tracks, squelches and other sounds, sent to everyone else.
        // ---- The body on the floor: where it is, blood it splashes on walls, its thuds ----

        void OnBodyPose(string packed) { if (Mine) RPC_BodyPose(packed); }
        void OnBodyStain(Vector3 at, Vector3 normal, float size) { if (Mine) RPC_BodyStain(at, normal, size); }
        void OnBodySound(int kind, Vector3 at, float loudness) { if (Mine) RPC_BodySound((byte)kind, at, loudness); }

        [Rpc(RpcSources.StateAuthority, RpcTargets.Proxies)]
        void RPC_BodyPose(string packed) { if (VictimBody.Instance != null) VictimBody.Instance.ShowRemote(packed, false); }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        void RPC_BodyPoseFor([RpcTarget] PlayerRef player, string packed) { if (VictimBody.Instance != null) VictimBody.Instance.ShowRemote(packed, true); }

        [Rpc(RpcSources.StateAuthority, RpcTargets.Proxies)]
        void RPC_BodyStain(Vector3 at, Vector3 normal, float size) { if (VictimBody.Instance != null) VictimBody.Instance.WallStain(at, normal, size, false); }

        [Rpc(RpcSources.StateAuthority, RpcTargets.Proxies)]
        void RPC_BodySound(byte kind, Vector3 at, float loudness) { if (VictimBody.Instance != null) VictimBody.Instance.Sound(kind, at, loudness, false); }

        void OnMarkPlaced(bool tyre, Vector3 at, float yaw, Vector3 size, float strength, bool mirror)
        {
            if (Object != null && Object.IsValid && HasStateAuthority) RPC_Mark(tyre, at, yaw, size, strength, mirror);
        }

        void OnSquelched(Vector3 at, float loudness)
        {
            if (Object != null && Object.IsValid && HasStateAuthority) RPC_Squelch(at, loudness);
        }

        void OnSound(SharedSound sound, int index, Vector3 at, float volume)
        {
            if (Object != null && Object.IsValid && HasStateAuthority) RPC_Sound((byte)sound, index, at, volume);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.Proxies)]
        void RPC_Mark(NetworkBool tyre, Vector3 at, float yaw, Vector3 size, float strength, NetworkBool mirror)
        {
            if (BloodTracks.Instance != null) BloodTracks.Instance.PlaceRemote(tyre, at, yaw, size, strength, mirror);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.Proxies)]
        void RPC_Squelch(Vector3 at, float loudness)
        {
            if (BloodTracks.Instance != null) BloodTracks.Instance.SquelchAt(at, loudness);
        }

        // ---- The marker board: what you put on it, sent to everyone (you included) ----

        bool Mine => Object != null && Object.IsValid && HasStateAuthority;
        void OnBoardText(string s, Rect box, int color, string key) { if (Mine) RPC_BoardText(s, new Vector4(box.x, box.y, box.width, box.height), (byte)color, key); }
        void OnBoardStroke(int id, int color, int size, string packed, bool last, string key) { if (Mine) RPC_BoardStroke(id, (byte)color, (byte)size, packed, last, key); }
        void OnBoardErase() { if (Mine) RPC_BoardErase(); }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        void RPC_BoardText(string s, Vector4 box, byte color, string key)
        {
            if (MarkerBoard.Instance != null) MarkerBoard.Instance.AddText(PlayerId, s, new Rect(box.x, box.y, box.z, box.w), color, true, key);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        void RPC_BoardStroke(int id, byte color, byte size, string packed, NetworkBool last, string key)
        {
            if (MarkerBoard.Instance != null) MarkerBoard.Instance.AddStroke(PlayerId, id, color, size, packed, last, true, key);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        void RPC_BoardErase()
        {
            if (MarkerBoard.Instance != null) MarkerBoard.Instance.Erase(PlayerId, true);
        }

        // Someone new: what's already on the board, shown straight away.
        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        void RPC_BoardTextFor([RpcTarget] PlayerRef player, string s, Vector4 box, byte color)
        {
            if (MarkerBoard.Instance != null) MarkerBoard.Instance.AddText(-3, s, new Rect(box.x, box.y, box.z, box.w), color, false);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        void RPC_BoardStrokeFor([RpcTarget] PlayerRef player, int id, byte color, byte size, string packed, NetworkBool last)
        {
            if (MarkerBoard.Instance != null) MarkerBoard.Instance.AddStroke(-3, id, color, size, packed, last, false);
        }

        IEnumerator SendBoardTo(PlayerRef player)
        {
            var board = MarkerBoard.Instance;
            if (board == null) yield break;
            var texts = new System.Collections.Generic.List<(string, Rect, int)>();
            var lines = new System.Collections.Generic.List<(int, int, System.Collections.Generic.List<Vector2>)>();
            board.Export((s, r, c) => texts.Add((s, r, c)), (c, z, p) => lines.Add((c, z, new System.Collections.Generic.List<Vector2>(p))));
            int sent = 0;
            foreach (var t in texts)
            {
                if (Object == null || !Object.IsValid) yield break;
                RPC_BoardTextFor(player, t.Item1, new Vector4(t.Item2.x, t.Item2.y, t.Item2.width, t.Item2.height), (byte)t.Item3);
                if (++sent % 8 == 0) yield return null;
            }
            int id = 100000;
            foreach (var line in lines)
            {
                id++;
                var points = line.Item3;
                for (int from = 0; from < points.Count; from += 40)
                {
                    if (Object == null || !Object.IsValid) yield break;
                    int n = Mathf.Min(40, points.Count - from);
                    RPC_BoardStrokeFor(player, id, (byte)line.Item1, (byte)line.Item2, StrokeGraphic.Pack(points, from, n), from + n >= points.Count);
                    if (++sent % 8 == 0) yield return null;
                }
            }
        }

        IEnumerator AnnounceJoin()
        {
            float until = Time.unscaledTime + 1.5f;
            while (Time.unscaledTime < until && PlayerName.Length == 0) yield return null;
            if (ChatBox.Instance != null) ChatBox.Instance.Joined(PlayerName.ToString());
            m_ShownName = PlayerName.ToString();
        }

        // The name everyone last saw for this player; a change after joining is announced.
        string m_ShownName;

        void CheckRename()
        {
            if (m_ShownName == null || Object == null || !Object.IsValid) return;
            string now = PlayerName.ToString();
            if (now == m_ShownName) return;
            if (ChatBox.Instance != null) ChatBox.Instance.Renamed(m_ShownName, now);
            m_ShownName = now;
        }

        // Someone new: what's been said so far.
        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        void RPC_ChatFor([RpcTarget] PlayerRef player, string name, string message, NetworkBool system, string oldName, float age)
        {
            if (ChatBox.Instance != null) ChatBox.Instance.AddOld(name, message, system, oldName, age);
        }

        IEnumerator SendChatTo(PlayerRef player)
        {
            if (ChatBox.Instance == null) yield break;
            var messages = new System.Collections.Generic.List<ChatBox.Message>(ChatBox.Instance.History);
            int sent = 0;
            foreach (var m in messages)
            {
                if (Object == null || !Object.IsValid) yield break;
                RPC_ChatFor(player, m.name ?? "", m.text, m.system, m.oldName ?? "", ChatBox.Age(m));
                if (++sent % 10 == 0) yield return null;
            }
        }

        // ---- Scooter hits ----

        /// <summary>You ran into this player on your scooter (sent to them only).</summary>
        public void Hit(NetworkPlayer target, Vector3 dir, float speed)
        {
            if (Mine && target != null && target.Object != null) RPC_Hit(target.Object.StateAuthority, dir, speed);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        void RPC_Hit([RpcTarget] PlayerRef player, Vector3 dir, float speed)
        {
            var me = FindAnyObjectByType<FirstPersonController>();
            if (me != null) me.TakeHit(dir, speed);
        }

        // Chat: your message to everyone (you included, so it shows in your own chat too).
        void OnChat(string message)
        {
            if (Object != null && Object.IsValid && HasStateAuthority) RPC_Chat(message);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        void RPC_Chat(string message)
        {
            if (ChatBox.Instance != null) ChatBox.Instance.Add(PlayerName.ToString(), message);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.Proxies)]
        void RPC_Sound(byte sound, int index, Vector3 at, float volume) => SharedSounds.PlayRemote((SharedSound)sound, index, at, volume);

        // ---- Someone new: the prints and tracks already on the floor ----

        IEnumerator SendMarksTo(PlayerRef player)
        {
            // Give them a moment to finish joining, then send a few each frame.
            yield return new WaitForSeconds(2f);
            if (BloodTracks.Instance == null) yield break;
            var marks = BloodTracks.Instance.AllMarks();
            int sent = 0;
            foreach (var m in marks)
            {
                if (Object == null || !Object.IsValid) yield break;
                RPC_MarkFor(player, m.tyre, m.at, m.yaw, m.size, m.strength, m.mirror);
                if (++sent % 12 == 0) yield return null;
            }
            // (The board they load themselves, from where it's kept online.)
            // Where the body lies now, if it's been moved.
            var victim = VictimBody.Instance;
            if (victim != null && victim.EverMoved && Object != null && Object.IsValid) RPC_BodyPoseFor(player, victim.Pack());
            yield return SendChatTo(player);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        void RPC_MarkFor([RpcTarget] PlayerRef player, NetworkBool tyre, Vector3 at, float yaw, Vector3 size, float strength, NetworkBool mirror)
        {
            if (BloodTracks.Instance != null) BloodTracks.Instance.PlaceRemote(tyre, at, yaw, size, strength, mirror);
        }

        // ---- Everyone else: show them ----

        public override void Render()
        {
            // Names: known a moment after arriving; changes after that go in the chat.
            if (m_ShownName == null && Time.unscaledTime - m_SpawnedAt > 2.5f) m_ShownName = PlayerName.ToString();
            CheckRename();
            if (HasStateAuthority) return;

            // Scooters follow them if they rode them last.
            var scooters = Multiplayer.Scooters;
            for (int i = 0; i < scooters.Length; i++)
            {
                var scooter = scooters[i];
                if (scooter == null || Multiplayer.ScooterOwner(i) != this) continue;
                if (m_Me != null && m_Me.CurrentRide == scooter) continue;
                scooter.FollowRemote(ScooterPositions[i], ScooterYaws[i], ScooterSpeeds[i], Riding && RideIndex == i);
            }

            if (m_AvatarShown != AvatarIndex || m_Avatar == null)
            {
                if (m_Avatar != null) Destroy(m_Avatar.gameObject);
                var library = PlayerAvatarLibrary.Instance;
                if (library == null) return;
                m_Avatar = library.Spawn(AvatarIndex, null);
                if (m_Avatar == null) return;
                m_Avatar.name = "Player " + PlayerId;
                // You bump into other people rather than walking through them.
                var body = m_Avatar.gameObject.AddComponent<CapsuleCollider>();
                body.center = new Vector3(0f, 0.85f, 0f);
                body.height = 1.7f;
                body.radius = 0.18f;
                m_Body = body;
                m_AvatarShown = AvatarIndex;
                // Looking where they're looking from the first frame.
                m_Avatar.Look(HeadYaw, HeadPitch, true);
            }

            // On a scooter they stand exactly on the deck wherever it's shown, holding its
            // handlebar; in a chair, exactly in the seat.
            var ride = Riding ? Multiplayer.ScooterAt(RideIndex) : null;
            var seat = Seated ? Multiplayer.SeatAt(SeatIndex) : null;
            Vector3 feet = transform.position;
            float yaw = transform.eulerAngles.y;
            if (ride != null || seat != null) PlayerAvatar.Placement(feet, yaw, seat, ride, out feet, out yaw);
            m_Avatar.Move(Motion);
            m_Avatar.Knock(IsDown, FellBackwards, Knocks);
            m_Avatar.Pose(feet, yaw, Speed, Seated, Time.deltaTime, PlayerAvatar.LeanBack(seat));
            // While you ride, people don't block you (your scooter pushes them on their side);
            // walking, you bump into them.
            if (m_Body != null) m_Body.enabled = m_Me == null || m_Me.CurrentRide == null;
            m_Avatar.Look(HeadYaw, HeadPitch);
            m_Avatar.Hold(ride);
            var board = MarkerBoard.Instance;
            if (board != null && board.ActiveAuthor == PlayerId) m_Avatar.UseBoard(board.PenPoint, board.Facing, board.ActiveTool, board.PenColor);
            else m_Avatar.UseBoard(Vector3.zero, Vector3.zero, MarkerBoard.Tool.None, Color.black);
            Footsteps();
        }

        // Their footsteps: one every stride while they walk or run.
        void Footsteps()
        {
            Vector3 p = transform.position;
            float moved = new Vector2(p.x - m_LastPosition.x, p.z - m_LastPosition.z).magnitude;
            m_LastPosition = p;
            if (Seated || Riding || Airborne || Speed < 0.4f || moved > 2f) { m_StepDistance = 0f; return; }
            m_StepDistance += moved;
            float stride = m_Me != null ? Mathf.Lerp(m_Me.walkStride, m_Me.runStride, Mathf.InverseLerp(m_Me.walkSpeed, m_Me.runSpeed, Speed)) : 0.9f;
            if (m_StepDistance < stride) return;
            m_StepDistance = 0f;
            var clips = m_Me != null ? m_Me.footstepClips : null;
            if (clips == null || clips.Length == 0 || m_Steps == null) return;
            m_LeftFoot = !m_LeftFoot;
            m_Steps.pitch = m_Me.footstepPitch * Random.Range(0.95f, 1.04f);
            m_Steps.PlayOneShot(clips[Random.Range(0, clips.Length)], m_Me.footstepVolume * (Speed > 3.5f ? 1f : 0.8f));
        }
    }
}
