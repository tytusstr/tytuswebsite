using UnityEngine;

namespace GlassRoom
{
    /// <summary>
    /// A visible person (one of the Rocketbox characters, Models/Players) standing in for a
    /// player: placed at their feet, facing their way, walking, running, standing still, sitting
    /// in a chair or riding a scooter. The head (with the neck and upper back) turns to look
    /// where the player looks, and on a scooter both hands hold the handlebar grips. Driven by
    /// LocalPlayerBody for you, and by the network for everyone else (NetworkPlayer).
    /// </summary>
    // After the players and scooters have moved this frame, so the hands land exactly on the grips.
    [DefaultExecutionOrder(100)]
    public class PlayerAvatar : MonoBehaviour
    {
        static readonly int k_Speed = Animator.StringToHash("Speed");
        static readonly int k_Seated = Animator.StringToHash("Seated");

        // The sitting animation has the hips this high above the feet; the seat cushion is a
        // little below the hip bone.
        const float SitHipHeight = 0.59f, HipAboveCushion = 0.1f;
        // How far the head can turn from the body, and look up or down, in degrees.
        const float HeadTurnLimit = 100f, HeadTiltLimit = 70f;

        Animator m_Animator;
        Transform m_Spine, m_Neck, m_Head;
        Transform m_LUpper, m_LFore, m_LHand, m_RUpper, m_RFore, m_RHand;

        float m_HeadYaw, m_HeadPitch, m_TargetHeadYaw, m_TargetHeadPitch;
        StandUpScooter m_Ride;

        void Awake()
        {
            m_Animator = GetComponentInChildren<Animator>();
            foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>()) r.updateWhenOffscreen = true;
            foreach (var t in GetComponentsInChildren<Transform>())
            {
                switch (t.name)
                {
                    case "Bip01 Spine2": m_Spine = t; break;
                    case "Bip01 Neck": m_Neck = t; break;
                    case "Bip01 Head": m_Head = t; break;
                    case "Bip01 L UpperArm": m_LUpper = t; break;
                    case "Bip01 L Forearm": m_LFore = t; break;
                    case "Bip01 L Hand": m_LHand = t; break;
                    case "Bip01 R UpperArm": m_RUpper = t; break;
                    case "Bip01 R Forearm": m_RFore = t; break;
                    case "Bip01 R Hand": m_RHand = t; break;
                }
            }
        }

        /// <summary>Where the player is and what they're doing.</summary>
        public void Pose(Vector3 feet, float yaw, float speed, bool seated, float dt)
        {
            transform.SetPositionAndRotation(feet, Quaternion.Euler(0f, yaw, 0f));
            if (m_Animator == null) return;
            m_Animator.SetFloat(k_Speed, speed, 0.12f, Mathf.Max(dt, 0.0001f));
            m_Animator.SetBool(k_Seated, seated);
        }

        /// <summary>Where the head looks, relative to the body (degrees): turn (right +) and tilt (down +).</summary>
        public void Look(float headYaw, float headPitch, bool snap = false)
        {
            m_TargetHeadYaw = Mathf.Clamp(headYaw, -HeadTurnLimit, HeadTurnLimit);
            m_TargetHeadPitch = Mathf.Clamp(headPitch, -HeadTiltLimit, HeadTiltLimit);
            if (snap) { m_HeadYaw = m_TargetHeadYaw; m_HeadPitch = m_TargetHeadPitch; }
        }

        /// <summary>The scooter being ridden (the hands hold its grips), or null.</summary>
        public void Hold(StandUpScooter scooter) => m_Ride = scooter;

        // After the animation each frame: turn the head, and put the hands on the handlebar.
        void LateUpdate()
        {
            float k = 1f - Mathf.Exp(-14f * Time.deltaTime);
            m_HeadYaw = Mathf.Lerp(m_HeadYaw, m_TargetHeadYaw, k);
            m_HeadPitch = Mathf.Lerp(m_HeadPitch, m_TargetHeadPitch, k);
            TurnHead();
            if (m_Ride != null) HoldGrips(m_Ride);
        }

        // The turn is shared out: a little in the upper back, more in the neck, most in the head.
        void TurnHead()
        {
            Vector3 right = transform.right;
            float yawSoFar = 0f;
            Bend(m_Spine, 0.25f, 0.15f, ref yawSoFar, right);
            Bend(m_Neck, 0.35f, 0.35f, ref yawSoFar, right);
            Bend(m_Head, 0.40f, 0.50f, ref yawSoFar, right);
        }

        void Bend(Transform bone, float yawShare, float pitchShare, ref float yawSoFar, Vector3 bodyRight)
        {
            if (bone == null) return;
            float yaw = m_HeadYaw * yawShare;
            yawSoFar += yaw;
            Vector3 axis = Quaternion.AngleAxis(yawSoFar, Vector3.up) * bodyRight;
            bone.rotation = Quaternion.AngleAxis(m_HeadPitch * pitchShare, axis) * Quaternion.AngleAxis(yaw, Vector3.up) * bone.rotation;
        }

        void HoldGrips(StandUpScooter scooter)
        {
            Vector3 left = scooter.GripPoint(-1), right = scooter.GripPoint(1);
            Vector3 back = -scooter.transform.forward, down = Vector3.down;
            Reach(m_LUpper, m_LFore, m_LHand, left, -scooter.transform.right * 0.35f + back * 0.25f + down * 0.3f);
            Reach(m_RUpper, m_RFore, m_RHand, right, scooter.transform.right * 0.35f + back * 0.25f + down * 0.3f);
        }

        // Two-bone reach: bends the elbow (towards the hint direction) so the hand lands on the target.
        static void Reach(Transform upper, Transform fore, Transform hand, Vector3 target, Vector3 elbowHint)
        {
            if (upper == null || fore == null || hand == null) return;
            Vector3 a = upper.position;
            float upperLength = Vector3.Distance(a, fore.position);
            float foreLength = Vector3.Distance(fore.position, hand.position);
            Vector3 toTarget = target - a;
            float reach = Mathf.Clamp(toTarget.magnitude, 0.01f, upperLength + foreLength - 0.001f);
            Vector3 dir = toTarget.normalized;
            // Where the elbow goes: in the plane of the hint, at the right distance from both ends.
            float along = (upperLength * upperLength - foreLength * foreLength + reach * reach) / (2f * reach);
            float off = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
            Vector3 side = Vector3.ProjectOnPlane(elbowHint, dir);
            if (side.sqrMagnitude < 0.0001f) side = Vector3.ProjectOnPlane(Vector3.down, dir);
            Vector3 elbow = a + dir * along + side.normalized * off;
            upper.rotation = Quaternion.FromToRotation(fore.position - a, elbow - a) * upper.rotation;
            fore.rotation = Quaternion.FromToRotation(hand.position - fore.position, a + dir * reach - fore.position) * fore.rotation;
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
                // Hips over the middle of the seat, sitting on the cushion, facing out of the chair.
                feet = seat.SitPoint + Vector3.up * (seat.seatHeight + HipAboveCushion - SitHipHeight);
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
