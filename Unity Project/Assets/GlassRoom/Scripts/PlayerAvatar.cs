using UnityEngine;

namespace GlassRoom
{
    /// <summary>
    /// A visible person (one of the Rocketbox characters, Models/Players) standing in for a
    /// player: placed at their feet, facing their way, walking, running, standing still, sitting
    /// in a chair or riding the scooter. Driven by LocalPlayerBody for you, and by the network for
    /// everyone else (NetworkPlayer).
    /// </summary>
    public class PlayerAvatar : MonoBehaviour
    {
        static readonly int k_Speed = Animator.StringToHash("Speed");
        static readonly int k_Seated = Animator.StringToHash("Seated");

        Animator m_Animator;

        void Awake()
        {
            m_Animator = GetComponentInChildren<Animator>();
            foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>()) r.updateWhenOffscreen = true;
        }

        /// <summary>Where the player is and what they're doing.</summary>
        public void Pose(Vector3 feet, float yaw, float speed, bool seated, float dt)
        {
            transform.SetPositionAndRotation(feet, Quaternion.Euler(0f, yaw, 0f));
            if (m_Animator == null) return;
            m_Animator.SetFloat(k_Speed, speed, 0.12f, Mathf.Max(dt, 0.0001f));
            m_Animator.SetBool(k_Seated, seated);
        }

        public void SetLayer(int layer)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }

        /// <summary>Where the body stands for a player in each situation.</summary>
        public static void Placement(Vector3 position, float yaw, Seat seat, StandUpScooter ride, out Vector3 feet, out float bodyYaw)
        {
            feet = position;
            bodyYaw = yaw;
            if (seat != null)
            {
                feet = seat.FloorPoint;
                bodyYaw = Quaternion.LookRotation(seat.Facing).eulerAngles.y;
            }
            else if (ride != null)
            {
                bodyYaw = ride.transform.eulerAngles.y;
                feet = ride.transform.position + Vector3.up * ride.deckHeight + ride.transform.forward * ride.riderOffsetZ;
            }
        }
    }
}
