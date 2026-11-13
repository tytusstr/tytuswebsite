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
        LocalPlayerBody m_MyBody;
        MusicSpeaker m_Radio;
        PlayerAvatar m_Avatar;
        int m_AvatarShown = -1;
        AudioSource m_Steps;
        float m_StepDistance;
        Vector3 m_LastPosition;
        bool m_LeftFoot;

        public override void Spawned()
        {
            Multiplayer.Players.Add(this);
            m_Me = FindAnyObjectByType<FirstPersonController>();
            m_Radio = FindAnyObjectByType<MusicSpeaker>();
            m_LastPosition = transform.position;
            if (HasStateAuthority)
            {
                Multiplayer.Local = this;
                m_MyBody = m_Me != null ? m_Me.GetComponent<LocalPlayerBody>() : null;
                SeatIndex = -1;
                RideIndex = -1;
                MusicTrack = -1;
                if (m_Radio != null) m_Radio.UserChanged += OnRadioPressed;
                BloodTracks.MarkPlaced += OnMarkPlaced;
                BloodTracks.Squelched += OnSquelched;
                SharedSounds.Played += OnSound;
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
            if (m_Avatar != null) Destroy(m_Avatar.gameObject);
        }

        public string Describe() =>
            $"player {PlayerId} local {IsLocal} at {transform.position:F2} yaw {transform.eulerAngles.y:F0} head {HeadYaw:F0}/{HeadPitch:F0} speed {Speed:F1} seated {Seated} ({SeatIndex}) riding {Riding} ({RideIndex}) avatar {(m_Avatar != null ? m_Avatar.transform.position.ToString("F2") + " yaw " + m_Avatar.transform.eulerAngles.y.ToString("F0") : "none")}";

        // ---- You: share your state every network tick ----

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority || m_Me == null) return;
            AvatarIndex = m_MyBody != null ? m_MyBody.AvatarIndex : 0;
            PlayerAvatar.Placement(m_Me.transform.position, m_Me.BodyYaw, m_Me.CurrentSeat, m_Me.CurrentRide, out var feet, out var yaw);
            transform.SetPositionAndRotation(feet, Quaternion.Euler(0f, yaw, 0f));
            Speed = m_Me.GroundSpeed;
            Seated = m_Me.CurrentSeat != null;
            SeatIndex = Seated ? Multiplayer.SeatIndex(m_Me.CurrentSeat) : -1;
            Riding = m_Me.CurrentRide != null;
            RideIndex = Riding ? Multiplayer.ScooterIndex(m_Me.CurrentRide) : -1;
            HeadYaw = m_Me.HeadYaw;
            HeadPitch = m_Me.HeadPitch;

            // The scooters everyone follows yours of: keep them up to date.
            var scooters = Multiplayer.Scooters;
            for (int i = 0; i < scooters.Length; i++)
            {
                var scooter = scooters[i];
                if (scooter == null || scooter.RemoteControlled || Multiplayer.ScooterOwner(i) != this) continue;
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
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        void RPC_MarkFor([RpcTarget] PlayerRef player, NetworkBool tyre, Vector3 at, float yaw, Vector3 size, float strength, NetworkBool mirror)
        {
            if (BloodTracks.Instance != null) BloodTracks.Instance.PlaceRemote(tyre, at, yaw, size, strength, mirror);
        }

        // ---- Everyone else: show them ----

        public override void Render()
        {
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
                body.center = new Vector3(0f, 0.9f, 0f);
                body.height = 1.75f;
                body.radius = 0.25f;
                m_AvatarShown = AvatarIndex;
            }

            // On a scooter they stand exactly on the deck wherever it's shown, holding its
            // handlebar; in a chair, exactly in the seat.
            var ride = Riding ? Multiplayer.ScooterAt(RideIndex) : null;
            var seat = Seated ? Multiplayer.SeatAt(SeatIndex) : null;
            Vector3 feet = transform.position;
            float yaw = transform.eulerAngles.y;
            if (ride != null || seat != null) PlayerAvatar.Placement(feet, yaw, seat, ride, out feet, out yaw);
            m_Avatar.Pose(feet, yaw, Speed, Seated, Time.deltaTime);
            m_Avatar.Look(HeadYaw, HeadPitch);
            m_Avatar.Hold(ride);
            Footsteps();
        }

        // Their footsteps: one every stride while they walk or run.
        void Footsteps()
        {
            Vector3 p = transform.position;
            float moved = new Vector2(p.x - m_LastPosition.x, p.z - m_LastPosition.z).magnitude;
            m_LastPosition = p;
            if (Seated || Riding || Speed < 0.4f || moved > 2f) { m_StepDistance = 0f; return; }
            m_StepDistance += moved;
            float stride = Speed > 4f ? 1.5f : 0.75f;
            if (m_StepDistance < stride) return;
            m_StepDistance = 0f;
            var clips = m_Me != null ? m_Me.footstepClips : null;
            if (clips == null || clips.Length == 0 || m_Steps == null) return;
            m_LeftFoot = !m_LeftFoot;
            m_Steps.pitch = m_Me.footstepPitch * Random.Range(0.95f, 1.04f);
            m_Steps.PlayOneShot(clips[Random.Range(0, clips.Length)], m_Me.footstepVolume * (Speed > 4f ? 1f : 0.8f));
        }
    }
}
