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
        static readonly int k_MoveX = Animator.StringToHash("MoveX");
        static readonly int k_MoveZ = Animator.StringToHash("MoveZ");

        // The sitting animation has the hips this high above the feet; the seat cushion is a
        // little below the hip bone.
        const float SitHipHeight = 0.59f;
        // How far the head can turn from the body, and look up or down, in degrees.
        const float HeadTurnLimit = 100f, HeadTiltLimit = 70f;

        Animator m_Animator;
        Transform m_Spine, m_Neck, m_Head, m_LClavicle, m_RClavicle, m_Pelvis, m_LowerSpine;
        Vector2 m_Motion;
        float m_HipTwist;
        Transform m_LUpper, m_LFore, m_LHand, m_RUpper, m_RFore, m_RHand;
        // Finger bones, per hand: [finger 0-4][segment 0-2]; finger 0 is the thumb.
        readonly Transform[,] m_LFingers = new Transform[5, 3], m_RFingers = new Transform[5, 3];

        /// <summary>The head bone (for the name above it).</summary>
        public Transform Head => m_Head;
        /// <summary>The neck bone (steady while the head turns; the seated view follows it).</summary>
        public Transform Neck => m_Neck;

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
                    case "Bip01 Pelvis": m_Pelvis = t; break;
                    case "Bip01 Spine": m_LowerSpine = t; break;
                    case "Bip01 Neck": m_Neck = t; break;
                    case "Bip01 Head": m_Head = t; break;
                    case "Bip01 L Clavicle": m_LClavicle = t; break;
                    case "Bip01 R Clavicle": m_RClavicle = t; break;
                    case "Bip01 L UpperArm": m_LUpper = t; break;
                    case "Bip01 L Forearm": m_LFore = t; break;
                    case "Bip01 L Hand": m_LHand = t; break;
                    case "Bip01 R UpperArm": m_RUpper = t; break;
                    case "Bip01 R Forearm": m_RFore = t; break;
                    case "Bip01 R Hand": m_RHand = t; break;
                }
                // "Bip01 L Finger2", "Bip01 L Finger21", "Bip01 L Finger22": finger 2, segments 0-2.
                if (t.name.StartsWith("Bip01 L Finger") || t.name.StartsWith("Bip01 R Finger"))
                {
                    string code = t.name.Substring("Bip01 L Finger".Length);
                    if (code.Length >= 1 && code.Length <= 2 && char.IsDigit(code[0]) && (code.Length == 1 || char.IsDigit(code[1])))
                    {
                        int finger = code[0] - '0', segment = code.Length == 1 ? 0 : code[1] - '0';
                        if (finger < 5 && segment < 3) (t.name[6] == 'L' ? m_LFingers : m_RFingers)[finger, segment] = t;
                    }
                }
            }
        }

        /// <summary>
        /// Where the player is and what they're doing. Leaning back (degrees, in a chair) tips the
        /// whole body back around the hips.
        /// </summary>
        public void Pose(Vector3 feet, float yaw, float speed, bool seated, float dt, float leanBack = 0f)
        {
            if (Mathf.Abs(leanBack) > 0.01f)
            {
                var rotation = Quaternion.Euler(-leanBack, yaw, 0f);
                Vector3 hips = feet + Vector3.up * SitHipHeight;
                transform.SetPositionAndRotation(hips - rotation * Vector3.up * SitHipHeight, rotation);
            }
            else transform.SetPositionAndRotation(feet, Quaternion.Euler(0f, yaw, 0f));
            if (m_Animator == null) return;
            // How they're moving relative to their body picks the walk: forwards, backwards
            // (the walk in reverse), sideways (sidesteps), or in between.
            float damp = Mathf.Max(dt, 0.0001f);
            m_Animator.SetFloat(k_Speed, speed, 0.12f, damp);
            m_Animator.SetFloat(k_MoveX, m_Motion.x, 0.12f, damp);
            m_Animator.SetFloat(k_MoveZ, m_Motion.y, 0.12f, damp);
            m_Animator.SetBool(k_Seated, seated);
        }

        static readonly int k_Down = Animator.StringToHash("Down");
        static readonly int k_Backwards = Animator.StringToHash("Backwards");
        static readonly int k_Fall = Animator.StringToHash("Fall");
        int m_KnocksShown = -1;
        bool m_IsDown;

        /// <summary>Knocked over by a scooter: down now or not, which way, and how many times so far.</summary>
        public void Knock(bool down, bool backwards, int count)
        {
            m_IsDown = down;
            if (m_Animator == null) return;
            bool first = m_KnocksShown < 0;
            if (count != m_KnocksShown)
            {
                m_KnocksShown = count;
                if (!first && down && HasParameter(k_Fall))
                {
                    m_Animator.SetBool(k_Backwards, backwards);
                    m_Animator.SetTrigger(k_Fall);
                }
            }
            if (HasParameter(k_Down)) m_Animator.SetBool(k_Down, down);
        }

        bool HasParameter(int hash)
        {
            foreach (var p in m_Animator.parameters) if (p.nameHash == hash) return true;
            return false;
        }

        /// <summary>How they're moving relative to their body: x sideways (right +), y forward (m/s).</summary>
        public void Move(Vector2 motion) => m_Motion = motion;

        /// <summary>Where the head looks, relative to the body (degrees): turn (right +) and tilt (down +).</summary>
        public void Look(float headYaw, float headPitch, bool snap = false)
        {
            m_TargetHeadYaw = Mathf.Clamp(headYaw, -HeadTurnLimit, HeadTurnLimit);
            m_TargetHeadPitch = Mathf.Clamp(headPitch, -HeadTiltLimit, HeadTiltLimit);
            if (snap) { m_HeadYaw = m_TargetHeadYaw; m_HeadPitch = m_TargetHeadPitch; }
        }

        // The marker board: the right hand holds a marker (or the eraser) against the board.
        MarkerBoard.Tool m_Tool;
        Vector3 m_ToolPoint, m_ToolNormal;
        Color m_ToolColor;
        Transform m_Marker, m_Eraser;
        Renderer m_MarkerCap;

        /// <summary>Writing, drawing or wiping on the board: where the tip is, which way the board faces, and with what.</summary>
        public void UseBoard(Vector3 point, Vector3 boardFacing, MarkerBoard.Tool tool, Color color)
        {
            m_Tool = tool;
            m_ToolPoint = point;
            m_ToolNormal = boardFacing;
            m_ToolColor = color;
        }

        /// <summary>The scooter being ridden (the hands hold its grips), or null.</summary>
        public void Hold(StandUpScooter scooter) => m_Ride = scooter;

        // After the animation each frame: turn the head, and put the hands on the handlebar.
        void LateUpdate()
        {
            float k = 1f - Mathf.Exp(-14f * Time.deltaTime);
            m_HeadYaw = Mathf.Lerp(m_HeadYaw, m_TargetHeadYaw, k);
            m_HeadPitch = Mathf.Lerp(m_HeadPitch, m_TargetHeadPitch, k);
            // Not while falling over or getting up (those move the head themselves).
            var state = m_Animator != null ? m_Animator.GetCurrentAnimatorStateInfo(0) : default;
            if (m_Animator == null || state.IsName("Move") || state.IsName("Seated")) TurnHead();
            if (m_Ride != null) HoldGrips(m_Ride);
            UseTool();
        }

        void UseTool()
        {
            if (m_Tool == MarkerBoard.Tool.None)
            {
                if (m_Marker != null && m_Marker.gameObject.activeSelf) m_Marker.gameObject.SetActive(false);
                if (m_Eraser != null && m_Eraser.gameObject.activeSelf) m_Eraser.gameObject.SetActive(false);
                return;
            }
            EnsureTools();
            Vector3 n = m_ToolNormal.sqrMagnitude > 0.01f ? m_ToolNormal.normalized : -transform.forward;
            Vector3 boardRight = Vector3.Cross(Vector3.up, n);
            bool marker = m_Tool == MarkerBoard.Tool.Marker;
            m_Marker.gameObject.SetActive(marker);
            m_Eraser.gameObject.SetActive(!marker);
            Vector3 handAt;
            if (marker)
            {
                // A marker pointing at the board, its tip on the line being drawn.
                m_Marker.position = m_ToolPoint + n * 0.07f;
                m_Marker.rotation = Quaternion.FromToRotation(Vector3.up, n);
                if (m_MarkerCap != null) m_MarkerCap.material.color = m_ToolColor;
                handAt = m_ToolPoint + n * 0.12f + Vector3.down * 0.02f;
            }
            else
            {
                // The eraser flat against the board, the hand on its back.
                m_Eraser.position = m_ToolPoint + n * 0.022f;
                m_Eraser.rotation = Quaternion.LookRotation(-n, Vector3.up);
                handAt = m_ToolPoint + n * 0.07f;
            }
            Reach(m_RUpper, m_RFore, m_RHand, handAt, Vector3.down * 0.4f - n * 0.1f + boardRight * 0.1f);
            if (m_RFore == null || m_RHand == null) return;
            // The hand closed round it, knuckles towards the board.
            Vector3 along = (m_RHand.position - m_RFore.position).normalized;
            Vector3 fingersDir = Vector3.Slerp(along, -n, 0.5f).normalized;
            m_RHand.rotation = Quaternion.FromToRotation(-m_RHand.right, fingersDir) * m_RHand.rotation;
            Vector3 want = Vector3.ProjectOnPlane(-boardRight, fingersDir).normalized;
            float twist = Vector3.SignedAngle(Vector3.ProjectOnPlane(m_RHand.up, fingersDir), want, fingersDir);
            m_RFore.rotation = Quaternion.AngleAxis(twist * 0.5f, along) * m_RFore.rotation;
            twist = Vector3.SignedAngle(Vector3.ProjectOnPlane(m_RHand.up, fingersDir), want, fingersDir);
            m_RHand.rotation = Quaternion.AngleAxis(twist, fingersDir) * m_RHand.rotation;
            for (int f = 1; f < 5; f++)
            {
                Curl(m_RFingers[f, 0], Vector3.forward, -80f);
                Curl(m_RFingers[f, 1], Vector3.forward, -85f);
                Curl(m_RFingers[f, 2], Vector3.forward, -60f);
            }
            Curl(m_RFingers[0, 0], Vector3.up, 30f);
            Curl(m_RFingers[0, 1], Vector3.forward, -35f);
        }

        // A felt-tip marker and a board eraser, made once and kept hidden until needed.
        void EnsureTools()
        {
            if (m_Marker != null) return;
            int layer = gameObject.layer;
            m_Marker = new GameObject("Marker").transform;
            var body = Part(m_Marker, PrimitiveType.Cylinder, new Vector3(0.016f, 0.055f, 0.016f), Vector3.zero, new Color(0.92f, 0.92f, 0.9f), layer);
            m_MarkerCap = Part(m_Marker, PrimitiveType.Cylinder, new Vector3(0.017f, 0.02f, 0.017f), new Vector3(0f, -0.05f, 0f), Color.black, layer);
            Part(m_Marker, PrimitiveType.Cylinder, new Vector3(0.007f, 0.012f, 0.007f), new Vector3(0f, -0.075f, 0f), new Color(0.15f, 0.15f, 0.15f), layer);
            m_Eraser = new GameObject("Eraser").transform;
            Part(m_Eraser, PrimitiveType.Cube, new Vector3(0.13f, 0.055f, 0.022f), new Vector3(0f, 0f, 0.011f), new Color(0.2f, 0.2f, 0.22f), layer);
            Part(m_Eraser, PrimitiveType.Cube, new Vector3(0.13f, 0.055f, 0.02f), new Vector3(0f, 0f, -0.01f), new Color(0.55f, 0.4f, 0.25f), layer);
            m_Marker.gameObject.SetActive(false);
            m_Eraser.gameObject.SetActive(false);
        }

        static Renderer Part(Transform parent, PrimitiveType type, Vector3 scale, Vector3 at, Color color, int layer)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.layer = layer;
            go.transform.SetParent(parent, false);
            go.transform.localScale = scale;
            go.transform.localPosition = at;
            var r = go.GetComponent<Renderer>();
            r.material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            r.material.color = color;
            r.material.SetFloat("_Smoothness", 0.4f);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return r;
        }

        void OnDestroy()
        {
            if (m_Marker != null) Destroy(m_Marker.gameObject);
            if (m_Eraser != null) Destroy(m_Eraser.gameObject);
        }

        // Stepping sideways: the hips and legs turn towards the way they're going (up to 75°) while
        // the upper body keeps facing forward, so the legs walk sideways under a still chest.
        void TwistHips()
        {
            float target = 0f;
            if (m_Motion.magnitude > 0.3f && m_Pelvis != null && m_LowerSpine != null)
            {
                float forwardSign = m_Motion.y < -0.1f ? -1f : 1f;
                target = Mathf.Clamp(Mathf.Atan2(m_Motion.x * forwardSign, Mathf.Abs(m_Motion.y)) * Mathf.Rad2Deg, -75f, 75f);
            }
            m_HipTwist = Mathf.MoveTowards(m_HipTwist, target, 300f * Time.deltaTime);
            if (Mathf.Abs(m_HipTwist) < 0.1f || m_Pelvis == null || m_LowerSpine == null) return;
            Vector3 up = transform.up;
            m_Pelvis.rotation = Quaternion.AngleAxis(m_HipTwist, up) * m_Pelvis.rotation;
            m_LowerSpine.rotation = Quaternion.AngleAxis(-m_HipTwist, up) * m_LowerSpine.rotation;
        }

        // The turn is shared out: a little in the upper back, more in the neck, most in the head.
        // Looking up and down bends only the neck and head. The shoulders hang from the neck in
        // these skeletons, so they're held where they were: the arms never move with the head.
        void TurnHead()
        {
            Vector3 right = transform.right;
            float yawSoFar = 0f;
            Bend(m_Spine, 0.25f, 0f, ref yawSoFar, right);
            Quaternion left = m_LClavicle != null ? m_LClavicle.rotation : Quaternion.identity;
            Quaternion rightShoulder = m_RClavicle != null ? m_RClavicle.rotation : Quaternion.identity;
            Bend(m_Neck, 0.35f, 0.4f, ref yawSoFar, right);
            Bend(m_Head, 0.40f, 0.6f, ref yawSoFar, right);
            if (m_LClavicle != null) m_LClavicle.rotation = left;
            if (m_RClavicle != null) m_RClavicle.rotation = rightShoulder;
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
            HoldOne(m_LUpper, m_LFore, m_LHand, m_LFingers, scooter.GripPoint(-1), scooter, -1f);
            HoldOne(m_RUpper, m_RFore, m_RHand, m_RFingers, scooter.GripPoint(1), scooter, 1f);
        }

        // One hand on its grip: the wrist just behind the bar along the line of the arm, the hand
        // carrying straight on from the forearm with the palm resting on top of the bar, and the
        // fingers wrapped over it. The turn of the wrist is shared with the forearm, the way a
        // real arm turns, so the wrist never looks twisted or pinched. Elbows hang down and back.
        // (In these hands the fingers point along the hand's -X, the palm faces its +Y, fingers
        // close by turning about their own -Z, and the thumb about -Y left or +Y right.)
        static void HoldOne(Transform upper, Transform fore, Transform hand, Transform[,] fingers, Vector3 grip, StandUpScooter scooter, float side)
        {
            if (upper == null || fore == null || hand == null) return;
            Vector3 forward = scooter.transform.forward;
            Vector3 arm = (grip - upper.position).normalized;
            Vector3 wrist = grip - arm * 0.065f + Vector3.up * 0.012f;
            Reach(upper, fore, hand, wrist, -forward * 0.3f + Vector3.down * 0.3f + scooter.transform.right * side * 0.06f);

            // Straight on from the forearm, dipping slightly onto the bar.
            Vector3 along = (hand.position - fore.position).normalized;
            Vector3 toBar = (grip - hand.position).normalized;
            Vector3 fingersDir = Vector3.Slerp(along, toBar, 0.4f).normalized;
            hand.rotation = Quaternion.FromToRotation(-hand.right, fingersDir) * hand.rotation;
            // Palm down: half of the turn in the forearm, the rest in the hand.
            Vector3 want = Vector3.ProjectOnPlane(Vector3.down, fingersDir).normalized;
            float twist = Vector3.SignedAngle(Vector3.ProjectOnPlane(hand.up, fingersDir), want, fingersDir);
            fore.rotation = Quaternion.AngleAxis(twist * 0.5f, along) * fore.rotation;
            twist = Vector3.SignedAngle(Vector3.ProjectOnPlane(hand.up, fingersDir), want, fingersDir);
            hand.rotation = Quaternion.AngleAxis(twist, fingersDir) * hand.rotation;

            for (int f = 1; f < 5; f++)
            {
                Curl(fingers[f, 0], Vector3.forward, -55f);
                Curl(fingers[f, 1], Vector3.forward, -65f);
                Curl(fingers[f, 2], Vector3.forward, -45f);
            }
            Curl(fingers[0, 0], Vector3.up, 25f * side);
            Curl(fingers[0, 1], Vector3.forward, -25f);
            Curl(fingers[0, 2], Vector3.forward, -30f);
        }

        static void Curl(Transform bone, Vector3 localAxis, float degrees)
        {
            if (bone != null) bone.localRotation = bone.localRotation * Quaternion.AngleAxis(degrees, localAxis);
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

        /// <summary>How far someone in this chair leans back (for Pose).</summary>
        public static float LeanBack(Seat seat) => seat != null ? seat.recline : 0f;

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
                // Hips in the seat on the cushion, facing out of the chair (Pose leans them back).
                feet = seat.HipPoint - Vector3.up * SitHipHeight;
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
