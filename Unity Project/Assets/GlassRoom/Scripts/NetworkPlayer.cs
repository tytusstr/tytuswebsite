using Fusion;
using UnityEngine;

namespace GlassRoom
{
    /// <summary>
    /// One person in the shared room. Yours shares where you are and what you're doing (walking,
    /// running, sitting in which chair, riding the scooter), which person you look like, the
    /// scooter's position after you've ridden it, and what the radio is playing. Everyone else's
    /// shows up here as a real person (PlayerAvatar) moving smoothly, with their footsteps,
    /// their bloody prints and tracks, and their squelches.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class NetworkPlayer : NetworkBehaviour
    {
        [Networked] public int AvatarIndex { get; set; }
        [Networked] public float Speed { get; set; }
        [Networked] public NetworkBool Seated { get; set; }
        [Networked] public int SeatIndex { get; set; }
        [Networked] public NetworkBool Riding { get; set; }

        // The scooter as this player last left it (everyone follows the newest).
        [Networked] public int ScooterVersion { get; set; }
        [Networked] public Vector3 ScooterPosition { get; set; }
        [Networked] public float ScooterYaw { get; set; }
        [Networked] public float ScooterSpeed { get; set; }

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
        StandUpScooter m_Scooter;
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
            m_Scooter = FindAnyObjectByType<StandUpScooter>();
            m_LastPosition = transform.position;
            if (HasStateAuthority)
            {
                Multiplayer.Local = this;
                m_MyBody = m_Me != null ? m_Me.GetComponent<LocalPlayerBody>() : null;
                SeatIndex = -1;
                MusicTrack = -1;
                if (m_Radio != null) m_Radio.UserChanged += OnRadioPressed;
                BloodTracks.MarkPlaced += OnMarkPlaced;
                BloodTracks.Squelched += OnSquelched;
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
            }
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            Multiplayer.Players.Remove(this);
            if (Multiplayer.Local == this) Multiplayer.Local = null;
            if (m_Radio != null) m_Radio.UserChanged -= OnRadioPressed;
            BloodTracks.MarkPlaced -= OnMarkPlaced;
            BloodTracks.Squelched -= OnSquelched;
            if (m_Avatar != null) Destroy(m_Avatar.gameObject);
        }

        public string Describe() =>
            $"player {PlayerId} local {IsLocal} at {transform.position:F2} yaw {transform.eulerAngles.y:F0} speed {Speed:F1} seated {Seated} riding {Riding} avatar {(m_Avatar != null ? m_Avatar.transform.position.ToString("F2") + (m_Avatar.gameObject.activeInHierarchy ? " shown" : " hidden") : "none")}";

        // ---- You: share your state every network tick ----

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority || m_Me == null) return;
            AvatarIndex = m_MyBody != null ? m_MyBody.AvatarIndex : 0;
            PlayerAvatar.Placement(m_Me.transform.position, m_Me.Yaw, m_Me.CurrentSeat, m_Me.CurrentRide, out var feet, out var yaw);
            transform.SetPositionAndRotation(feet, Quaternion.Euler(0f, yaw, 0f));
            Speed = m_Me.GroundSpeed;
            Seated = m_Me.CurrentSeat != null;
            SeatIndex = Seated ? Multiplayer.SeatIndex(m_Me.CurrentSeat) : -1;
            Riding = m_Me.CurrentRide != null;

            // The scooter: if everyone follows yours, keep them up to date with it.
            if (m_Scooter != null && Multiplayer.ScooterOwner() == this && !m_Scooter.RemoteControlled)
            {
                ScooterPosition = m_Scooter.transform.position;
                ScooterYaw = m_Scooter.transform.eulerAngles.y;
                ScooterSpeed = m_Scooter.Speed;
            }
            SyncRadio();
        }

        public void ClaimScooter(int version) => ScooterVersion = version;

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

        // Your prints, tracks and squelches, sent to everyone else.
        void OnMarkPlaced(bool tyre, Vector3 at, float yaw, Vector3 size, float strength, bool mirror)
        {
            if (Object != null && Object.IsValid && HasStateAuthority) RPC_Mark(tyre, at, yaw, size, strength, mirror);
        }

        void OnSquelched(Vector3 at, float loudness)
        {
            if (Object != null && Object.IsValid && HasStateAuthority) RPC_Squelch(at, loudness);
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

        // ---- Everyone else: show them ----

        public override void Render()
        {
            if (HasStateAuthority) return;

            // The scooter follows them if they rode it last.
            if (m_Scooter != null && Multiplayer.ScooterOwner() == this && (m_Me == null || m_Me.CurrentRide == null))
                m_Scooter.FollowRemote(ScooterPosition, ScooterYaw, ScooterSpeed, Riding);

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
            m_Avatar.Pose(transform.position, transform.eulerAngles.y, Speed, Seated, Time.deltaTime);
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
