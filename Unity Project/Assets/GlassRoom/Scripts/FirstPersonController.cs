using UnityEngine;
using UnityEngine.InputSystem;

namespace GlassRoom
{
    /// <summary>
    /// First-person walking with keyboard and mouse or touch, head bob and footsteps,
    /// plus stepping onto and riding a <see cref="StandUpScooter"/>.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class FirstPersonController : MonoBehaviour
    {
        [Header("References")]
        public Transform cameraPivot;

        [Header("Movement")]
        public float walkSpeed = 2.14f;
        public float runSpeed = 4.52f;
        public float acceleration = 10f;
        public float gravity = 20f;
        [Tooltip("Upward speed when jumping (Space, or the Jump button on a phone).")]
        public float jumpSpeed = 6.6f;

        [Header("Look")]
        public float mouseSensitivity = 0.16f;
        public float touchSensitivity = 0.26f;
        public float maxPitch = 85f;

        [Header("Feel")]
        public float bobAmount = 0.028f;
        public float walkStride = 0.9f;
        public float runStride = 1.6f;
        public float footstepVolume = 0.9f;
        [Tooltip("Recorded footstep sounds.")]
        public AudioClip[] footstepClips;
        [Tooltip("Below 1 makes steps deeper and heavier, like boots.")]
        public float footstepPitch = 0.78f;
        [Tooltip("Higher frequencies above this are muffled, for a darker sound.")]
        public float footstepCutoff = 2200f;
        [Tooltip("Seconds of silence added after each step so the echo can ring out.")]
        public float echoTail = 2.5f;

        [Header("Interacting")]
        [Tooltip("How close you need to stand to the scooter or speaker to use it: metres from your feet to its nearest point.")]
        public float interactRange = 1.3f;

        // How far a click or tap looks along the view; reach is then checked from your feet.
        const float ClickRayLength = 5f;

        CharacterController m_Controller;
        ControlsHUD m_Hud;
        Camera m_Camera;
        AudioSource m_StepSource;
        AudioClip[] m_StepClips;

        Vector3 m_Velocity;
        float m_Yaw, m_Pitch;
        float m_StrideProgress;
        float m_BobWeight;
        float m_EyeHeight;
        int m_LastClip = -1;
        bool m_LeftFoot;
        bool m_JumpPressed;
        // Holding Space jumps again only after standing on the ground this long.
        const float HeldJumpPause = 0.02f;
        float m_GroundedTime;
        // The "Space to jump" hint: waits until the menu is closed, then shows for a few seconds.
        bool m_JumpHintPending;
        // Seconds of smooth play the jump hint has been on screen. Counted only while the menu
        // is closed, and at most a short step per frame, so the slow first moments while the
        // page is still loading don't use it up before anyone can see it.
        float m_JumpHintShown = float.MaxValue;
        const float JumpHintSeconds = 4f, JumpHintFade = 0.8f;

        // Riding.
        StandUpScooter m_Ride;
        float m_RideSpeed;
        // Current amount of turning, from -1 to 1, which follows the steering input gradually.
        float m_RideTurn;
        // Head turn relative to the scooter's heading while riding, in degrees.
        float m_RideLookYaw;
        const float RideLookLimit = 100f;
        float m_WalkRadius, m_WalkHeight, m_WalkStep;
        Vector3 m_WalkCenter;
        // Scooters and speakers in the scene, for the phone buttons.
        readonly System.Collections.Generic.List<Component> m_Interactables = new System.Collections.Generic.List<Component>();

        void Awake()
        {
            m_Controller = GetComponent<CharacterController>();
            // By default tiny moves are ignored, which made a scooter starting from rest look
            // blocked and never get going.
            m_Controller.minMoveDistance = 0f;
            // A slim body whose top is level with the top of the head (people can get close
            // without walking into each other), and only small steps up (not onto chairs).
            m_Controller.radius = 0.22f;
            m_Controller.height = 1.72f;
            m_Controller.center = new Vector3(0f, 0.86f, 0f);
            m_Controller.stepOffset = 0.18f;
            if (cameraPivot == null)
                cameraPivot = GetComponentInChildren<Camera>().transform;
            m_Camera = cameraPivot.GetComponent<Camera>();
            m_EyeHeight = cameraPivot.localPosition.y;
            m_Yaw = transform.eulerAngles.y;
            m_BodyYaw = m_Yaw;
            m_StartPosition = transform.position;
            m_StartYaw = m_Yaw;
            m_Pitch = cameraPivot.localEulerAngles.x;
            if (m_Pitch > 180f) m_Pitch -= 360f;

            m_Hud = ControlsHUD.Create();
            m_Interactables.AddRange(FindObjectsByType<StandUpScooter>(FindObjectsSortMode.None));
            m_Interactables.AddRange(FindObjectsByType<MusicSpeaker>(FindObjectsSortMode.None));
            m_Interactables.AddRange(FindObjectsByType<Seat>(FindObjectsSortMode.None));
            m_Interactables.AddRange(FindObjectsByType<MarkerBoard>(FindObjectsSortMode.None));
            SetupFootsteps();
            InputModeManager.ModeChanged += OnModeChanged;
            OnModeChanged(InputModeManager.Mode);
        }

        void OnDestroy()
        {
            InputModeManager.ModeChanged -= OnModeChanged;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void SetupFootsteps()
        {
            var source = footstepClips ?? new AudioClip[0];
            m_StepClips = new AudioClip[source.Length];
            for (int i = 0; i < source.Length; i++)
                m_StepClips[i] = WithSilentTail(source[i], echoTail);

            var go = new GameObject("Footsteps");
            go.transform.SetParent(transform, false);
            m_StepSource = go.AddComponent<AudioSource>();
            m_StepSource.playOnAwake = false;
            m_StepSource.spatialBlend = 0f;

#if UNITY_WEBGL && !UNITY_EDITOR
            // Browsers can't run these sound filters; skip them on the website.
            return;
#endif
            // Filters run in the order they are added: darken first, then the room echo.
            var lowPass = go.AddComponent<AudioLowPassFilter>();
            lowPass.cutoffFrequency = footstepCutoff;
            lowPass.lowpassResonanceQ = 1f;

            // A large hard room: early slap-back off the walls, then a dark tail.
            var reverb = go.AddComponent<AudioReverbFilter>();
            reverb.reverbPreset = AudioReverbPreset.User;
            reverb.dryLevel = 0f;
            reverb.room = -600f;
            reverb.roomHF = -1200f;
            reverb.decayTime = 2.2f;
            reverb.decayHFRatio = 0.5f;
            reverb.reflectionsLevel = -700f;
            reverb.reflectionsDelay = 0.03f;
            reverb.reverbLevel = -500f;
            reverb.reverbDelay = 0.045f;
            reverb.diffusion = 80f;
            reverb.density = 100f;
        }

        // An AudioSource's filters stop when its clip ends, which cuts the echo off.
        // Padding each clip with silence keeps the source playing while the echo fades.
        static AudioClip WithSilentTail(AudioClip clip, float tailSeconds)
        {
            if (clip == null || tailSeconds <= 0f)
                return clip;
#if UNITY_WEBGL && !UNITY_EDITOR
            // Browsers decode sounds themselves, so the samples can't be read or padded there.
            return clip;
#endif
            // GetData needs decompressed samples; the builder imports the clips that way.
            if (clip.loadState != AudioDataLoadState.Loaded)
                clip.LoadAudioData();

            int channels = clip.channels;
            var data = new float[clip.samples * channels];
            if (!clip.GetData(data, 0))
                return clip;

            int tail = Mathf.CeilToInt(tailSeconds * clip.frequency);
            var padded = new float[(clip.samples + tail) * channels];
            System.Array.Copy(data, padded, data.Length);
            var result = AudioClip.Create(clip.name + " (tail)", clip.samples + tail, channels, clip.frequency, false);
            result.SetData(padded, 0);
            return result;
        }

        void OnModeChanged(ControlMode mode)
        {
            if (mode == ControlMode.Touchscreen)
                SetCursorLocked(false);
            else
                m_JumpHintPending = true;
            UpdatePrompts();
        }

        void Update()
        {
            // Until the visitor presses Enter on the website's cover, the game doesn't respond
            // to anything and makes no sound at all.
            bool covered = WebPage.Covered;
            if (AudioListener.pause != covered) AudioListener.pause = covered;
            if (covered)
            {
                if (m_Hud != null) m_Hud.ConsumeJump();
                UpdatePrompts();
                return;
            }
            SyncPointer();
            ReadInput(out Vector2 move, out Vector2 look, out bool run);
            if (TestMove != Vector2.zero) { move = TestMove; run = TestRun; }
            if (m_Camera != null && !Mathf.Approximately(m_Camera.fieldOfView, FovSettings.FieldOfView))
                m_Camera.fieldOfView = FovSettings.FieldOfView;
            if (m_Ride != null && InputModeManager.Mode == ControlMode.Touchscreen)
            {
                // Riding on a phone: the joystick drives forward and back and steers left and
                // right. The finger on the right side only looks up and down.
                Ride(move);
                m_RideLookYaw = 0f;
                m_Pitch = Mathf.Clamp(m_Pitch - look.y, -maxPitch, maxPitch);
                cameraPivot.localRotation = Quaternion.Euler(m_Pitch, 0f, 0f);
            }
            else if (m_Seat != null)
            {
                // Seated: look around, but not round behind the chair's back; walking or jumping
                // stands you up.
                ApplyLook(look);
                // Looking down only so far: any further and you'd see inside your own shirt.
                if (m_Pitch > SeatedLookDown)
                {
                    m_Pitch = SeatedLookDown;
                    cameraPivot.localRotation = Quaternion.Euler(m_Pitch, 0f, 0f);
                }
                float chair = Quaternion.LookRotation(m_Seat.Facing).eulerAngles.y;
                float turned = Mathf.Clamp(Mathf.DeltaAngle(chair, m_Yaw), -SeatedLookLimit, SeatedLookLimit);
                if (!Mathf.Approximately(chair + turned, m_Yaw))
                {
                    m_Yaw = chair + turned;
                    transform.rotation = Quaternion.Euler(0f, m_Yaw, 0f);
                }
                if (move.sqrMagnitude > 0.04f || m_JumpPressed) StandUp();
            }
            else if (m_Ride != null)
            {
                // Riding on a computer: the keys steer the scooter. The mouse turns only your
                // head, within a range, like sitting in a boat; it never changes where the
                // scooter goes.
                Ride(move);
                m_RideLookYaw = Mathf.Clamp(m_RideLookYaw + look.x, -RideLookLimit, RideLookLimit);
                m_Pitch = Mathf.Clamp(m_Pitch - look.y, -maxPitch, maxPitch);
                cameraPivot.localRotation = Quaternion.Euler(m_Pitch, m_RideLookYaw, 0f);
            }
            else
            {
                if (Down) { UpdateDown(look, Time.deltaTime); PushedByScooters(Time.deltaTime); UpdatePrompts(); return; }
                ApplyLook(look);
                // Writing or drawing on the board: you stay where you are until it's up.
                if (!MarkerBoard.LocalBusy)
                {
                    ApplyMove(move, run);
                    PushedByScooters(Time.deltaTime);
                }
            }
            UpdateBodyYaw(Time.deltaTime);
            // Somehow fallen out of the building: back to where you started.
            if (transform.position.y < FallLimit && m_Seat == null && m_Ride == null) Teleport(m_StartPosition, m_StartYaw);
            UpdatePrompts();
        }

        // ---------- Your body, as others (and mirrors) see it ----------

        // The body turns a little towards where you're walking (sideways steps turn it a bit),
        // but never more than this from where your head points.
        const float BodyTurnLimit = 15f;
        const float FallLimit = -20f;
        Vector3 m_StartPosition;
        float m_StartYaw;
        const float SeatedLookLimit = 90f, SeatedLookDown = 40f;
        float m_BodyYaw;

        /// <summary>Which way your body faces, in degrees.</summary>
        public float BodyYaw
        {
            get
            {
                if (m_Seat != null) return Quaternion.LookRotation(m_Seat.Facing).eulerAngles.y;
                if (m_Ride != null) return m_Ride.transform.eulerAngles.y;
                return m_BodyYaw;
            }
        }

        /// <summary>Your movement over the ground relative to your body: x sideways (right +), y forward.</summary>
        public Vector2 LocalMotion
        {
            get
            {
                if (m_Ride != null || m_Seat != null || m_Controller == null || !m_Controller.enabled) return Vector2.zero;
                // Turning on the spot to catch up with the head: small steps, not sliding feet.
                if (m_BodyCatchingUp && m_SmoothVelocity.magnitude < 0.3f) return new Vector2(0f, 0.55f);
                Vector3 v = Quaternion.Euler(0f, -BodyYaw, 0f) * m_SmoothVelocity;
                return new Vector2(v.x, v.z);
            }
        }

        /// <summary>Where your head is turned, relative to your body (degrees, right is positive).</summary>
        public float HeadYaw => Mathf.DeltaAngle(BodyYaw, m_Yaw + (m_Ride != null ? m_RideLookYaw : 0f));

        /// <summary>How far you're looking down (positive) or up (negative), in degrees.</summary>
        public float HeadPitch => m_Pitch;

        // Looking around turns only your head; once it's turned more than this far, the body
        // turns to catch up. Walking, the body turns towards where you're going (a little).
        const float HeadOnlyLimit = 40f;
        bool m_BodyCatchingUp;

        void UpdateBodyYaw(float dt)
        {
            bool moving = false;
            float target = m_Yaw;
            // Your movement, smoothed: the controller's own reading jumps about from frame to frame.
            Vector3 raw = m_Controller.enabled ? m_Controller.velocity : Vector3.zero;
            raw.y = 0f;
            m_SmoothVelocity = Vector3.Lerp(m_SmoothVelocity, raw, 1f - Mathf.Exp(-10f * dt));
            if (m_Ride == null && m_Seat == null && m_Controller.enabled)
            {
                Vector3 v = m_SmoothVelocity;
                if (v.magnitude > 0.3f)
                {
                    moving = true;
                    float heading = Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;
                    float delta = Mathf.DeltaAngle(m_Yaw, heading);
                    // Walking backwards the body faces forward, turned towards the side stepped to.
                    if (Mathf.Abs(delta) > 90f) delta = Mathf.DeltaAngle(m_Yaw, heading + 180f);
                    // Going straight sideways the sidestep does it all and the body stays square
                    // (fading in from either side, so it never flips between left and right).
                    float square = Mathf.Clamp01((90f - Mathf.Abs(delta)) / 35f);
                    target = m_Yaw + Mathf.Clamp(delta, -BodyTurnLimit, BodyTurnLimit) * square;
                }
            }
            if (moving)
            {
                m_BodyCatchingUp = false;
                m_BodyYaw = Mathf.MoveTowardsAngle(m_BodyYaw, target, 200f * dt);
            }
            else
            {
                if (Mathf.Abs(Mathf.DeltaAngle(m_BodyYaw, m_Yaw)) > HeadOnlyLimit) m_BodyCatchingUp = true;
                if (m_BodyCatchingUp)
                {
                    m_BodyYaw = Mathf.MoveTowardsAngle(m_BodyYaw, m_Yaw, 160f * dt);
                    if (Mathf.Abs(Mathf.DeltaAngle(m_BodyYaw, m_Yaw)) < 2f) m_BodyCatchingUp = false;
                }
            }
            // However fast you turn, the head never ends up more than a quarter turn from the body.
            m_BodyYaw = m_Yaw + Mathf.Clamp(Mathf.DeltaAngle(m_Yaw, m_BodyYaw), -90f, 90f);
        }

        Vector3 m_SmoothVelocity;

        /// <summary>Testing only (see Multiplayer.TestWalk): held joystick input.</summary>
        public static Vector2 TestMove;
        public static bool TestRun;

        void ReadInput(out Vector2 move, out Vector2 look, out bool run)
        {
            move = Vector2.zero;
            look = Vector2.zero;
            run = false;
            // Read every frame so a press while the menu is open doesn't jump later.
            bool phoneJump = m_Hud.ConsumeJump();
            m_JumpPressed = false;
            // Typing (chat, your name): the game leaves the keyboard alone.
            if (ChatBox.Typing || (ChatBox.Instance != null && ChatBox.Instance.IsOpen)) return;

            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                // E steps off the scooter. Esc only frees the mouse (the menu has its own button).
                if (keyboard.eKey.wasPressedThisFrame && m_Ride != null && !m_Hud.IsMenuOpen)
                    StepOff();
                if (keyboard.escapeKey.wasPressedThisFrame)
                    SetCursorLocked(false);
                // Q opens and closes the menu on a computer. The mouse stays captured: the menu
                // gets the game's own pointer (FakeCursor), so there's no clicking back in after.
                if (keyboard.qKey.wasPressedThisFrame && InputModeManager.Mode == ControlMode.KeyboardMouse)
                {
                    m_Hud.ToggleMenu();
                    if (!m_Hud.IsMenuOpen) SetCursorLocked(true);
                }
                // N skips to the next song while looking at the speaker (same as right click).
                if (keyboard.nKey.wasPressedThisFrame && InputModeManager.Mode == ControlMode.KeyboardMouse
                    && !m_Hud.IsMenuOpen && InteractableAt(ScreenCentre) is MusicSpeaker skipped)
                    skipped.Skip();
            }

            if (m_Hud.IsMenuOpen)
                return;

            if (InputModeManager.Mode == ControlMode.Touchscreen)
            {
                // The stick sets the direction and how far it's pushed sets the speed. The Sprint
                // button turns sprinting on until it's tapped again or you stop moving.
                move = m_Hud.Joystick.Value;
                // (Tapping it before moving is fine: it waits until you've moved, then stopped.)
                bool movingNow = move.sqrMagnitude >= 0.01f;
                if (!m_Hud.SprintOn) m_SprintMoved = false;
                else if (movingNow) m_SprintMoved = true;
                else if (m_SprintMoved) { m_Hud.SetSprint(false); m_SprintMoved = false; }
                run = m_Hud.SprintOn;
                look = m_Hud.LookPad.ConsumeDelta() * touchSensitivity * LookSettings.Sensitivity;
                m_JumpPressed = phoneJump;
                // Mounting and the speaker are handled by the on-screen buttons (UpdatePrompts).
                return;
            }

            if (keyboard != null)
            {
                move.x = (Pressed(keyboard.dKey, keyboard.rightArrowKey) ? 1f : 0f) - (Pressed(keyboard.aKey, keyboard.leftArrowKey) ? 1f : 0f);
                move.y = (Pressed(keyboard.wKey, keyboard.upArrowKey) ? 1f : 0f) - (Pressed(keyboard.sKey, keyboard.downArrowKey) ? 1f : 0f);
                move = Vector2.ClampMagnitude(move, 1f);
                // Shift, or a quick double tap of W (or the up arrow) held down, sprints.
                bool forwardPressed = keyboard.wKey.wasPressedThisFrame || keyboard.upArrowKey.wasPressedThisFrame;
                bool forwardHeld = Pressed(keyboard.wKey, keyboard.upArrowKey);
                if (forwardPressed)
                {
                    m_DoubleTapSprint = Time.unscaledTime - m_LastForwardTap < DoubleTapTime;
                    m_LastForwardTap = Time.unscaledTime;
                }
                if (!forwardHeld) m_DoubleTapSprint = false;
                run = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed || m_DoubleTapSprint;
                // Holding Space keeps jumping: a new jump starts a moment after each landing.
                m_JumpPressed = keyboard.spaceKey.wasPressedThisFrame
                    || (keyboard.spaceKey.isPressed && m_GroundedTime >= HeldJumpPause);
            }

            var mouse = Mouse.current;
            if (mouse == null)
                return;
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                look = mouse.delta.ReadValue() * mouseSensitivity * LookSettings.Sensitivity;
                if (mouse.leftButton.wasPressedThisFrame) ClickAt(ScreenCentre, rightButton: false);
                if (mouse.rightButton.wasPressedThisFrame) ClickAt(ScreenCentre, rightButton: true);
            }
            else if (mouse.leftButton.wasPressedThisFrame && !m_Hud.IsPointerOverInteractive(mouse.position.ReadValue()))
            {
                // In computer mode every click comes from the centre plus, even the first one that
                // captures the mouse for looking.
                ClickAt(ScreenCentre, rightButton: false);
                SetCursorLocked(true);
            }
        }

        // Double-tapping forward to sprint: the second tap must come this soon after the first.
        const float DoubleTapTime = 0.3f;
        float m_LastForwardTap = -1f;
        bool m_DoubleTapSprint;
        bool m_SprintMoved;

        static bool Pressed(UnityEngine.InputSystem.Controls.KeyControl a, UnityEngine.InputSystem.Controls.KeyControl b) => a.isPressed || b.isPressed;

        // "Left click" with a mouse, just "Click" on a trackpad (the Mouse? switch).
        static string Click => MouseSettings.Enabled ? "Left click" : "Click";

        Vector2 ScreenCentre => new Vector2(Screen.width / 2f, Screen.height / 2f);

        void ApplyLook(Vector2 look)
        {
            m_Yaw += look.x;
            m_Pitch = Mathf.Clamp(m_Pitch - look.y, -maxPitch, maxPitch);
            transform.rotation = Quaternion.Euler(0f, m_Yaw, 0f);
            cameraPivot.localRotation = Quaternion.Euler(m_Pitch, 0f, 0f);
        }

        void ApplyMove(Vector2 move, bool run)
        {
            float dt = Time.deltaTime;
            float speed = run ? runSpeed : walkSpeed;
            Vector3 target = (transform.forward * move.y + transform.right * move.x) * speed;

            Vector3 horizontal = new Vector3(m_Velocity.x, 0f, m_Velocity.z);
            horizontal = Vector3.Lerp(horizontal, target, 1f - Mathf.Exp(-acceleration * dt));

            // Perched on something nearly vertical (like the side of the lamp shade), you can't
            // stand there: you're eased off it, outward, at an easy pace, and fall. Only for really
            // steep surfaces, and it never builds up speed (it just makes sure you're moving
            // outward at least that fast).
            bool steep = m_Controller.isGrounded && Vector3.Angle(m_GroundNormal, Vector3.up) > SteepAngle;
            bool grounded = m_Controller.isGrounded && !steep;
            if (steep)
            {
                var away = new Vector3(m_GroundNormal.x, 0f, m_GroundNormal.z);
                if (away.sqrMagnitude > 0.0001f)
                {
                    away.Normalize();
                    float outward = Vector3.Dot(horizontal, away);
                    if (outward < SlideSpeed) horizontal += away * (SlideSpeed - outward);
                }
            }
            float vertical = grounded ? -2f : Mathf.Min(m_Velocity.y, steep ? 0f : m_Velocity.y) - gravity * dt;
            // Landing after a jump or a drop: a heavier step.
            if (grounded && !m_WasGrounded && m_FallSpeed > 3f)
                JumpSound(SharedSound.Land, Mathf.Lerp(0.8f, 1.15f, Mathf.InverseLerp(3f, 9f, m_FallSpeed)));
            m_WasGrounded = grounded;
            m_FallSpeed = grounded ? 0f : Mathf.Max(m_FallSpeed, -m_Velocity.y);
            m_GroundedTime = grounded ? m_GroundedTime + dt : 0f;
            if (m_JumpPressed && grounded)
            {
                vertical = jumpSpeed;
                m_GroundedTime = 0f;
                JumpSound(SharedSound.Jump, 0.55f);
            }
            m_Velocity = new Vector3(horizontal.x, vertical, horizontal.z);
            m_GroundNormal = Vector3.up;
            // A shove from a scooter carries you on for a moment, on top of your own walking.
            m_Shove = Vector3.MoveTowards(m_Shove, Vector3.zero, 5f * dt);
            m_Controller.Move((m_Velocity + m_Shove) * dt);

            // Use the actual distance moved so walking into a wall makes no steps.
            Vector3 actual = m_Controller.velocity;
            float groundSpeed = new Vector2(actual.x, actual.z).magnitude;
            UpdateSteps(groundSpeed, dt);
        }

        bool m_WasGrounded = true;
        float m_FallSpeed;

        // A footstep for pushing off or landing, heard by everyone nearby.
        void JumpSound(SharedSound sound, float loudness)
        {
            if (m_StepClips == null || m_StepClips.Length == 0) return;
            int index = Random.Range(0, m_StepClips.Length);
            m_StepSource.pitch = footstepPitch * (sound == SharedSound.Land ? 0.9f : 1f) * Random.Range(0.95f, 1.04f);
            m_StepSource.panStereo = 0f;
            m_StepSource.PlayOneShot(m_StepClips[index], footstepVolume * loudness);
            SharedSounds.Raise(sound, index, transform.position, footstepVolume * loudness);
        }

        // The surface under your feet, from the last move (see ApplyMove).
        Vector3 m_GroundNormal = Vector3.up;
        const float SlideSpeed = 1.2f, SteepAngle = 70f;

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            // Something below the middle of you, i.e. what you're standing on.
            if (hit.point.y < transform.position.y + m_Controller.radius)
                m_GroundNormal = hit.normal;
            // Running into the body on the floor: walking shoves it slowly along; a scooter
            // hits it and sends it sliding, harder the faster you were going.
            var victim = hit.collider.GetComponentInParent<VictimBody>();
            if (victim == null) return;
            Vector3 dir = hit.moveDirection;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;
            dir.Normalize();
            if (m_Ride != null)
            {
                float speed = Mathf.Abs(m_RideSpeed);
                if (speed < 0.4f || Time.time - m_LastBodyHit < 0.3f) return;
                m_LastBodyHit = Time.time;
                victim.Hit(hit.point, dir, speed);
                m_RideSpeed *= 0.55f;
                return;
            }
            Vector3 walking = new Vector3(m_Velocity.x, 0f, m_Velocity.z);
            if (walking.magnitude < 0.2f) return;
            victim.Push(hit.point + Vector3.up * 0.05f, dir * Mathf.Lerp(600f, 950f, Mathf.InverseLerp(walkSpeed, runSpeed, walking.magnitude)), false);
        }

        float m_LastBodyHit = -10f;

        void UpdateSteps(float groundSpeed, float dt)
        {
            bool moving = m_WasGrounded && m_Controller.isGrounded && groundSpeed > 0.25f;
            float runT = Mathf.InverseLerp(walkSpeed, runSpeed, groundSpeed);
            float stride = Mathf.Lerp(walkStride, runStride, runT);

            if (moving)
            {
                m_StrideProgress += groundSpeed * dt / stride;
                if (m_StrideProgress >= 1f)
                {
                    m_StrideProgress -= 1f;
                    PlayStep(groundSpeed);
                }
            }
            else
            {
                // Ready the next step so starting to walk sounds quickly.
                m_StrideProgress = Mathf.MoveTowards(m_StrideProgress, 0.6f, dt * 2f);
            }

            m_BobWeight = Mathf.MoveTowards(m_BobWeight, moving ? Mathf.Clamp01(groundSpeed / walkSpeed) : 0f, dt * 4f);
            float dip = (1f + Mathf.Cos(m_StrideProgress * 2f * Mathf.PI)) * 0.5f;
            float sway = Mathf.Sin(m_StrideProgress * Mathf.PI) * (m_LeftFoot ? 1f : -1f);
            float amount = bobAmount * Mathf.Lerp(1f, 1.5f, runT) * m_BobWeight;
            cameraPivot.localPosition = new Vector3(sway * amount * 0.5f, m_EyeHeight - dip * amount, 0f);
        }

        void PlayStep(float groundSpeed)
        {
            int index = Random.Range(0, m_StepClips.Length);
            if (index == m_LastClip) index = (index + 1) % m_StepClips.Length;
            m_LastClip = index;
            m_LeftFoot = !m_LeftFoot;

            float loudness = Mathf.Lerp(0.75f, 1.1f, Mathf.InverseLerp(walkSpeed * 0.5f, runSpeed, groundSpeed));
            m_StepSource.pitch = footstepPitch * Random.Range(0.95f, 1.04f) * (m_LeftFoot ? 1f : 0.97f);
            m_StepSource.panStereo = m_LeftFoot ? -0.08f : 0.08f;
            m_StepSource.PlayOneShot(m_StepClips[index], footstepVolume * loudness * Random.Range(0.85f, 1f));
            // Blood underfoot: a squelch, and bloody prints for the next few steps.
            if (BloodTracks.Instance != null) BloodTracks.Instance.Footstep(transform.position, m_Yaw, m_LeftFoot);
        }

        // ---------- Scooter ----------

        // The nearest scooter or speaker exactly under that point on screen, or null. A thin ray,
        // so only looking right at the scooter's deck, pole or handlebar counts (its taller
        // walking blocker is on the Ignore Raycast layer).
        Component InteractableAt(Vector2 screenPosition)
        {
            var ray = m_Camera.ScreenPointToRay(screenPosition);
            Component found = null;
            float nearest = float.MaxValue;
            foreach (var hit in Physics.RaycastAll(ray, ClickRayLength, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
            {
                Component thing = hit.collider.GetComponentInParent<StandUpScooter>();
                if (thing == null) thing = hit.collider.GetComponentInParent<MusicSpeaker>();
                if (thing == null) thing = hit.collider.GetComponentInParent<Seat>();
                if (thing == null) thing = hit.collider.GetComponentInParent<MarkerBoard>();
                // The board only from right in front of it.
                if (thing is MarkerBoard board && !board.InFront(transform.position)) continue;
                if (thing == null || !WithinReach(hit.collider)) continue;
                if (hit.distance < nearest)
                {
                    nearest = hit.distance;
                    found = thing;
                }
            }
            return found;
        }

        // Only things right beside you count: measured along the floor from your feet to the
        // nearest point of the thing, so looking at it from across the room does nothing.
        bool WithinReach(Collider target)
        {
            Vector3 feet = transform.position;
            // (Solid shapes like the chairs can't answer this exactly; their box is close enough.)
            Vector3 nearestPoint = target is MeshCollider mesh && !mesh.convex ? target.bounds.ClosestPoint(feet) : target.ClosestPoint(feet);
            Vector2 apart = new Vector2(nearestPoint.x - feet.x, nearestPoint.z - feet.z);
            return apart.magnitude <= interactRange;
        }

        // Computer clicks from the centre plus. Scooter: left click steps on, and while riding
        // left click steps off (E works too).
        // Speaker: left click plays or pauses, right click skips to the next song.
        void ClickAt(Vector2 screenPosition, bool rightButton)
        {
            // On the ground, a click only gets you up (see UpdateDown).
            if (Down) return;
            var thing = InteractableAt(screenPosition);
            // Sitting: a click stands you back up (the speaker still works from the chair).
            if (m_Seat != null && !(thing is MusicSpeaker))
            {
                if (!rightButton) StandUp();
                return;
            }
            if (thing is MarkerBoard && !rightButton && m_Ride == null && BoardPanel.Instance != null && !MarkerBoard.LocalBusy)
            {
                BoardPanel.Instance.Open();
                return;
            }
            if (thing is Seat seat && !rightButton && m_Ride == null)
            {
                SitDown(seat);
                return;
            }
            if (thing is MusicSpeaker speaker)
            {
                if (rightButton) speaker.Skip();
                else speaker.TogglePlayPause();
            }
            else if (!rightButton && m_Ride != null)
                StepOff();
            else if (thing is StandUpScooter scooter && !rightButton)
                StepOn(scooter);
        }

        // ---------- Sitting ----------

        Seat m_Seat;

        // What the player is doing, for their visible body (LocalPlayerBody) and for other players.
        public Seat CurrentSeat => m_Seat;
        public StandUpScooter CurrentRide => m_Ride;
        public float Yaw => m_Yaw;
        /// <summary>How fast you're moving over the ground (0 while seated or riding).</summary>
        public float GroundSpeed
        {
            get
            {
                if (m_Ride != null || m_Seat != null || m_Controller == null || !m_Controller.enabled) return 0f;
                var v = m_Controller.velocity;
                return new Vector2(v.x, v.z).magnitude;
            }
        }
        /// <summary>Moves you to a spot on the floor, facing a direction (e.g. where you start).</summary>
        public void Teleport(Vector3 feet, float yaw)
        {
            if (m_Seat != null || m_Ride != null) return;
            bool was = m_Controller.enabled;
            m_Controller.enabled = false;
            transform.position = feet;
            m_Yaw = yaw;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            m_BodyYaw = yaw;
            m_Velocity = Vector3.zero;
            m_Controller.enabled = was;
        }

        /// <summary>Puts you at a spot, turning you towards a direction (used while you write on the board).</summary>
        public void HoldAt(Vector3 feet, float yaw)
        {
            if (m_Seat != null || m_Ride != null) return;
            bool was = m_Controller.enabled;
            m_Controller.enabled = false;
            transform.position = feet;
            m_Controller.enabled = was;
            m_Velocity = Vector3.zero;
            m_Yaw = Mathf.MoveTowardsAngle(m_Yaw, yaw, 240f * Time.deltaTime);
            m_BodyYaw = m_Yaw;
            transform.rotation = Quaternion.Euler(0f, m_Yaw, 0f);
        }

        public bool Airborne => m_Ride == null && m_Seat == null && m_Controller != null && m_Controller.enabled && !m_Controller.isGrounded;
        const float SeatedEyeAboveSeat = 0.74f;

        // Sits in the chair, facing out of it. You can look around but not walk; moving,
        // jumping or clicking stands you back up.
        /// <summary>Testing only (see Multiplayer.TestAct): sit, stand, ride, off.</summary>
        public void TestAct(string what)
        {
            if (what == "sit" || what == "ride")
            {
                Component best = null; float bestDistance = float.MaxValue;
                Component[] options = what == "sit" ? FindObjectsByType<Seat>(FindObjectsInactive.Exclude) : FindObjectsByType<StandUpScooter>(FindObjectsInactive.Exclude);
                foreach (var c in options)
                {
                    float d = Vector3.Distance(c.transform.position, transform.position);
                    if (d < bestDistance) { bestDistance = d; best = c; }
                }
                if (best is Seat seat) SitDown(seat);
                else if (best is StandUpScooter scooter) StepOn(scooter);
            }
            else if (what == "stand" && m_Seat != null) StandUp();
            else if (what == "off" && m_Ride != null) StepOff();
        }

        void SitDown(Seat seat)
        {
            // Someone else is already sitting there.
            if (Multiplayer.SeatTakenByOther(seat)) return;
            m_Seat = seat;
            m_Velocity = Vector3.zero;
            m_Controller.enabled = false;
            transform.position = seat.SitPoint;
            m_Yaw = Quaternion.LookRotation(seat.Facing).eulerAngles.y;
            transform.rotation = Quaternion.Euler(0f, m_Yaw, 0f);
            m_Pitch = 0f;
            cameraPivot.localRotation = Quaternion.identity;
            cameraPivot.localPosition = new Vector3(0f, seat.seatHeight + SeatedEyeAboveSeat, 0f);
            seat.PlaySitSound();
            SharedSounds.Raise(SharedSound.Sit, Multiplayer.SeatIndex(seat), seat.SitPoint + Vector3.up * 0.5f, seat.sitVolume);
        }

        void StandUp()
        {
            var seat = m_Seat;
            if (seat == null) return;
            m_Seat = null;
            // Step out in front of the chair: just clear of it, or a little further if something's
            // in the way there (the chair itself doesn't count).
            float r = m_Controller.radius;
            Vector3 front = seat.SitPoint + seat.Facing * 0.8f;
            foreach (float distance in new[] { 0.8f, 0.95f, 1.1f, 1.3f, 1.5f })
            {
                Vector3 p = seat.SitPoint + seat.Facing * distance;
                bool blocked = false;
                foreach (var c in Physics.OverlapCapsule(p + Vector3.up * (r + 0.05f), p + Vector3.up * (m_Controller.height - r), r, ~0, QueryTriggerInteraction.Ignore))
                    if (!c.transform.IsChildOf(seat.transform) && c != m_Controller) { blocked = true; break; }
                if (!blocked) { front = p; break; }
            }
            transform.position = front + Vector3.up * 0.02f;
            m_Yaw = Quaternion.LookRotation(seat.Facing).eulerAngles.y + Mathf.Clamp(Mathf.DeltaAngle(Quaternion.LookRotation(seat.Facing).eulerAngles.y, m_Yaw), -SeatedLookLimit, SeatedLookLimit);
            transform.rotation = Quaternion.Euler(0f, m_Yaw, 0f);
            m_BodyYaw = m_Yaw;
            cameraPivot.localPosition = new Vector3(0f, m_EyeHeight, 0f);
            m_Controller.enabled = true;
            m_Velocity = Vector3.zero;
        }

        void StepOn(StandUpScooter scooter)
        {
            // Someone else is riding it.
            if (Multiplayer.ScooterRiddenByOther(scooter)) return;
            scooter.TakeBackControl();
            Multiplayer.ClaimScooter(scooter);
            m_Ride = scooter;
            m_RideSpeed = 0f;
            m_Velocity = Vector3.zero;
            m_WalkRadius = m_Controller.radius;
            m_WalkHeight = m_Controller.height;
            m_WalkCenter = m_Controller.center;
            m_WalkStep = m_Controller.stepOffset;

            // Stand on the deck: the player's capsule becomes the scooter's footprint.
            m_Controller.enabled = false;
            transform.position = scooter.transform.position;
            // Keep looking where you were: the head stays turned, the scooter faces its own way.
            float lookingAt = m_Yaw;
            m_Yaw = scooter.transform.eulerAngles.y;
            transform.rotation = Quaternion.Euler(0f, m_Yaw, 0f);
            m_Controller.radius = scooter.footprintRadius;
            m_Controller.stepOffset = 0.05f;
            m_Controller.enabled = true;

            cameraPivot.localPosition = new Vector3(0f, m_EyeHeight + scooter.deckHeight, scooter.riderOffsetZ);
            m_RideLookYaw = Mathf.Clamp(Mathf.DeltaAngle(m_Yaw, lookingAt), -RideLookLimit, RideLookLimit);
            m_RideTurn = 0f;
            cameraPivot.localRotation = Quaternion.Euler(m_Pitch, m_RideLookYaw, 0f);
            scooter.SetRidden(true);
            IgnoreScooters(true);
            SharedSounds.Raise(SharedSound.ScooterOn, Multiplayer.ScooterIndex(scooter), scooter.transform.position, 0.9f);
        }

        void StepOff()
        {
            var scooter = m_Ride;
            if (scooter == null) return;
            m_Ride = null;
            scooter.SetRidden(false);
            IgnoreScooters(false);
            SharedSounds.Raise(SharedSound.ScooterOff, Multiplayer.ScooterIndex(scooter), scooter.transform.position, 0.9f);

            // Step off to the left if there's room, otherwise the right, otherwise behind.
            Vector3 origin = scooter.transform.position;
            Vector3[] options = { -scooter.transform.right, scooter.transform.right, -scooter.transform.forward };
            Vector3 exit = origin + options[0] * 0.8f;
            foreach (var dir in options)
            {
                Vector3 p = origin + dir * 0.8f;
                if (!Physics.CheckCapsule(p + Vector3.up * (m_WalkRadius + 0.05f), p + Vector3.up * (m_WalkHeight - m_WalkRadius), m_WalkRadius, ~0, QueryTriggerInteraction.Ignore))
                {
                    exit = p;
                    break;
                }
            }

            m_Controller.enabled = false;
            m_Controller.radius = m_WalkRadius;
            m_Controller.height = m_WalkHeight;
            m_Controller.center = m_WalkCenter;
            m_Controller.stepOffset = m_WalkStep;
            transform.position = exit + Vector3.up * 0.02f;
            m_Controller.enabled = true;
            m_Velocity = Vector3.zero;
            // Keep facing wherever you were looking.
            m_Yaw += m_RideLookYaw;
            m_RideLookYaw = 0f;
            transform.rotation = Quaternion.Euler(0f, m_Yaw, 0f);
            m_BodyYaw = m_Yaw;
            cameraPivot.localRotation = Quaternion.Euler(m_Pitch, 0f, 0f);
            cameraPivot.localPosition = new Vector3(0f, m_EyeHeight, 0f);
        }

        // Forward/back from W/S, the arrow keys or the stick's up/down; turning from A/D, the
        // arrow keys or the stick's left/right. It can turn on the spot.
        void Ride(Vector2 input)
        {
            var scooter = m_Ride;
            float dt = Time.deltaTime;
            float throttle = input.y, steer = input.x;

            float target = throttle >= 0f ? throttle * scooter.maxSpeed : throttle * scooter.reverseSpeed;
            float rate = Mathf.Approximately(throttle, 0f) ? scooter.coastDeceleration
                : Mathf.Sign(target) != Mathf.Sign(m_RideSpeed) && Mathf.Abs(m_RideSpeed) > 0.05f ? scooter.brakeDeceleration
                : scooter.acceleration;
            m_RideSpeed = Mathf.MoveTowards(m_RideSpeed, target, rate * dt);

            // Turning builds up over a moment instead of snapping to full, and eases off when released.
            float turnRate = Mathf.Abs(steer) > Mathf.Abs(m_RideTurn) ? scooter.turnBuildUp : scooter.turnEaseOff;
            m_RideTurn = Mathf.MoveTowards(m_RideTurn, steer, turnRate * dt);
            m_Yaw += m_RideTurn * scooter.turnRate * dt;
            transform.rotation = Quaternion.Euler(0f, m_Yaw, 0f);

            float vertical = m_Controller.isGrounded ? -2f : m_Velocity.y - gravity * dt;
            m_Velocity = transform.forward * m_RideSpeed + Vector3.up * vertical;
            IgnoreScooters(true);
            m_Controller.Move(m_Velocity * dt);
            ShoveScooters(scooter, dt);
            HitPeople(scooter);

            // Bumping into something stops the scooter instead of grinding against it; hitting it
            // fast makes a crash.
            Vector3 actual = m_Controller.velocity;
            float along = Vector3.Dot(new Vector3(actual.x, 0f, actual.z), transform.forward);
            // Only judged once it's properly moving, so pulling away from rest isn't mistaken for a bump.
            if (Mathf.Abs(m_RideSpeed) > 0.5f && Mathf.Abs(along) < Mathf.Abs(m_RideSpeed) * 0.5f)
            {
                float lost = Mathf.Abs(m_RideSpeed) - Mathf.Abs(along);
                if (lost > StandUpScooter.CrashSpeed) scooter.Crash(lost);
                m_RideSpeed = along;
            }

            scooter.transform.SetPositionAndRotation(transform.position, transform.rotation);
            scooter.UpdateMotion(m_RideSpeed);
        }

        // ---------- Scooters bumping ----------

        // While riding, the other scooters don't stop you: you shove them (unless someone is on
        // one, which stays solid). Walking, every scooter is solid as usual.
        void IgnoreScooters(bool riding)
        {
            foreach (var other in Multiplayer.Scooters)
            {
                if (other == null || other == m_Ride) continue;
                bool ignore = riding && !Multiplayer.ScooterRiddenByOther(other);
                foreach (var c in other.GetComponentsInChildren<Collider>())
                    if (c.enabled && !c.isTrigger) Physics.IgnoreCollision(m_Controller, c, ignore);
            }
        }

        // Your scooter running into a free one pushes it along (it's then yours to share, like
        // stepping on it), and it rolls on a little.
        void ShoveScooters(StandUpScooter mine, float dt)
        {
            Vector3 me = transform.position;
            foreach (var other in Multiplayer.Scooters)
            {
                if (other == null || other == mine || Multiplayer.ScooterRiddenByOther(other)) continue;
                Vector3 apart = other.transform.position - me;
                apart.y = 0f;
                float gap = mine.footprintRadius + other.footprintRadius - 0.1f;
                float distance = apart.magnitude;
                if (distance >= gap || distance < 0.001f) continue;
                Vector3 dir = apart / distance;
                float closing = Vector3.Dot(m_Controller.velocity, dir);
                if (closing <= 0.05f) continue;
                if (other.RemoteControlled) { other.TakeBackControl(); Multiplayer.ClaimScooter(other); }
                if (!other.Shove(dir * (gap - distance), closing * 0.8f))
                {
                    // It's against a wall: you stop instead of pushing it through.
                    m_Controller.Move(-dir * (gap - distance));
                    if (Mathf.Abs(m_RideSpeed) > StandUpScooter.CrashSpeed) mine.Crash(Mathf.Abs(m_RideSpeed));
                    m_RideSpeed = 0f;
                }
                else if (closing > StandUpScooter.CrashSpeed) mine.Crash(closing * 0.6f);
            }
        }

        // ---------- Hitting people ----------

        /// <summary>Speed (m/s) above which being hit by a scooter knocks you over (top speed is about 7.8).</summary>
        public const float KnockSpeed = 5f;
        readonly System.Collections.Generic.Dictionary<NetworkPlayer, float> m_LastHit = new System.Collections.Generic.Dictionary<NetworkPlayer, float>();
        Vector3 m_Shove;
        float m_LastScooterHit = -10f;

        // Riding into someone. People on foot notice it themselves, on their own screen, so
        // there's no delay (see PushedByScooters); here you just lose speed and hear the bump.
        // Someone else on a scooter is told, and shoved more gently.
        void HitPeople(StandUpScooter mine)
        {
            float speed = Mathf.Abs(m_RideSpeed);
            if (speed < 0.4f || Multiplayer.Local == null) return;
            Vector3 me = mine.transform.position;
            Vector3 heading = mine.transform.forward * Mathf.Sign(m_RideSpeed);
            foreach (var p in Multiplayer.Players)
            {
                if (p == null || p.IsLocal || p.Seated || p.Object == null || !p.Object.IsValid) continue;
                Vector3 them = p.Riding && Multiplayer.ScooterAt(p.RideIndex) != null ? Multiplayer.ScooterAt(p.RideIndex).transform.position : p.transform.position;
                Vector3 apart = them - me;
                apart.y = 0f;
                float reach = mine.footprintRadius + (p.Riding ? 0.38f : p.IsDown ? 0.45f : 0.22f);
                if (apart.magnitude > reach) continue;
                Vector3 dir = apart.magnitude > 0.01f ? apart.normalized : heading;
                if (Vector3.Dot(heading, dir) < 0.1f) continue; // only what's in front of you
                if (m_LastHit.TryGetValue(p, out float last) && Time.time - last < 0.7f) continue;
                m_LastHit[p] = Time.time;
                if (p.Riding) Multiplayer.Local.Hit(p, (dir + heading).normalized, speed * 0.5f);
                if (speed > StandUpScooter.CrashSpeed) mine.Crash(speed);
                m_RideSpeed *= p.Riding ? 0.6f : speed >= KnockSpeed ? 0.7f : 0.55f;
            }
        }

        /// <summary>
        /// You've been hit by a scooter going this fast. Walking: thrown along the way it was
        /// going (further the faster it was), and above <see cref="KnockSpeed"/> knocked over.
        /// On the ground: pushed along, and still down. Riding: your scooter is pushed.
        /// </summary>
        public void TakeHit(Vector3 dir, float speed)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;
            dir.Normalize();
            if (m_Seat != null) return;
            if (m_Ride != null)
            {
                m_Controller.Move(dir * Mathf.Min(0.4f, speed * 0.08f));
                m_RideSpeed += Vector3.Dot(dir, transform.forward) * speed * 0.6f;
                m_Ride.Crash(speed);
                return;
            }
            float t = Mathf.InverseLerp(KnockSpeed, 8f, speed);
            if (Down)
            {
                // Already down: shoved along the floor (falling again if you were getting up).
                m_Knock = dir * speed * Mathf.Lerp(0.9f, 1.3f, t);
                if (m_Rising) Fall(dir);
                Debug.Log($"Hit by a scooter at {speed:F1} m/s while down");
                return;
            }
            Debug.Log($"Hit by a scooter at {speed:F1} m/s" + (speed >= KnockSpeed ? ": knocked over" : ""));
            if (speed < KnockSpeed)
            {
                // A shove: thrown a little way, more the faster it was going.
                m_Shove = dir * speed * Mathf.Lerp(0.9f, 1.3f, speed / KnockSpeed);
                return;
            }
            m_Knock = dir * speed * Mathf.Lerp(1.1f, 1.5f, t);
            Fall(dir);
        }

        // Knocked over: backwards if hit from the front, onto your front if from behind.
        void Fall(Vector3 dir)
        {
            m_DownBackwards = Vector3.Dot(dir, transform.forward) < 0f;
            m_Down = true;
            m_Rising = false;
            m_DownTime = 0f;
            m_DownYaw = m_Yaw;
            m_Shove = Vector3.zero;
            KnockCount++;
        }

        /// <summary>Getting up (left click on a computer, the Get up button on a phone).</summary>
        public void GetUp()
        {
            if (!m_Down || m_Rising || m_DownTime < 0.9f) return;
            m_Rising = true;
            m_RiseTime = 0f;
        }

        /// <summary>True while you're lying on the ground (until you get up).</summary>
        public bool Lying => m_Down && !m_Rising;

        /// <summary>True while you're on the ground or getting up.</summary>
        public bool Down => m_Down;
        /// <summary>Fell onto your back (else onto your front).</summary>
        public bool DownBackwards => m_DownBackwards;
        /// <summary>Goes up by one each time you're knocked over.</summary>
        public int KnockCount { get; private set; }
        /// <summary>How long getting up takes (matches the animation for each side).</summary>
        float GetUpTime => m_DownBackwards ? 1.95f : 2.2f;

        bool m_Down, m_Rising;
        float m_DownTime, m_RiseTime;
        bool m_DownBackwards;
        Vector3 m_Knock;
        float m_DownYaw;

        // On the ground: you slide to a stop and can only look around a little, until you get
        // up; the view drops to the floor and comes back up.
        void UpdateDown(Vector2 look, float dt)
        {
            m_DownTime += dt;
            if (m_Rising)
            {
                m_RiseTime += dt;
                if (m_RiseTime >= GetUpTime) { m_Down = m_Rising = false; }
            }
            else
            {
                var mouse = UnityEngine.InputSystem.Mouse.current;
                if (InputModeManager.Mode == ControlMode.KeyboardMouse && mouse != null && mouse.leftButton.wasPressedThisFrame
                    && !(m_Hud != null && m_Hud.IsMenuOpen) && !ChatBox.Typing)
                    GetUp();
            }
            m_Knock = Vector3.MoveTowards(m_Knock, Vector3.zero, 4f * dt);
            float vertical = m_Controller.isGrounded ? -2f : m_Velocity.y - gravity * dt;
            m_Velocity = new Vector3(m_Knock.x, vertical, m_Knock.z);
            m_Controller.Move(m_Velocity * dt);
            KeepRoomToLie();

            m_Yaw = m_DownYaw + Mathf.Clamp(Mathf.DeltaAngle(m_DownYaw, m_Yaw + look.x), -35f, 35f);
            transform.rotation = Quaternion.Euler(0f, m_Yaw, 0f);
            m_BodyYaw = m_DownYaw;

            // 0 standing, 1 lying: down as you fall, back up while getting up.
            float fall = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(m_DownTime / 0.45f));
            float rise = m_Rising ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(m_RiseTime / GetUpTime)) : 0f;
            float down = fall * (1f - rise);
            float lying = m_DownBackwards ? -55f : 50f;
            m_Pitch = Mathf.Clamp(m_Pitch - look.y, -25f, 25f);
            float roll = Mathf.Sin(down * Mathf.PI) * (m_DownBackwards ? 8f : -8f);
            cameraPivot.localRotation = Quaternion.Euler(m_Pitch * (1f - down) + (lying + m_Pitch * 0.3f) * down, 0f, roll);
            cameraPivot.localPosition = new Vector3(0f, Mathf.Lerp(m_EyeHeight, 0.32f, down), Mathf.Lerp(0f, m_DownBackwards ? -0.6f : 0.6f, down));
            if (!Down)
            {
                cameraPivot.localPosition = new Vector3(0f, m_EyeHeight, 0f);
                cameraPivot.localRotation = Quaternion.Euler(m_Pitch, 0f, 0f);
            }
            else KeepViewInside();
        }

        // Lying down your body reaches out up to a metre or so from where you stood: you're
        // moved off any wall so all of it stays inside the room.
        void KeepRoomToLie()
        {
            Vector3 along = Quaternion.Euler(0f, m_DownYaw, 0f) * Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, along);
            Vector3 push = Vector3.zero;
            // How far the body reaches each way (measured from the lying poses, plus a little):
            // on the back it lies across, mostly to the left; on the front, along the way you fell.
            var reach = m_DownBackwards
                ? new[] { (along, 0.85f), (-along, 0.52f), (side, 0.9f), (-side, 1.2f) }
                : new[] { (along, 1.05f), (-along, 1.05f), (side, 0.78f), (-side, 0.78f) };
            foreach (var (dir, need) in reach)
                foreach (float height in new[] { 0.12f, 0.4f })
                {
                    Vector3 from = transform.position + Vector3.up * height;
                    if (FirstWall(from, dir, need, out float distance)) { push -= dir * (need - distance); break; }
                }
            if (push.sqrMagnitude > 0.000001f) m_Controller.Move(push);
        }

        // Your view on the floor never goes through a wall: it stops just short of it.
        void KeepViewInside()
        {
            Vector3 from = transform.position + Vector3.up * 0.45f;
            Vector3 to = cameraPivot.position;
            Vector3 dir = to - from;
            float length = dir.magnitude;
            if (length < 0.001f) return;
            dir /= length;
            if (FirstWall(from, dir, length + 0.15f, out float distance, 0.1f))
                cameraPivot.position = from + dir * Mathf.Max(0f, distance - 0.15f);
        }

        // The nearest wall or fixed object along a line (not people, the scooters or the body on the floor).
        static bool FirstWall(Vector3 from, Vector3 dir, float range, out float distance, float radius = 0.12f)
        {
            distance = range;
            bool found = false;
            foreach (var hit in Physics.SphereCastAll(from, radius, dir, range, ~0, QueryTriggerInteraction.Ignore))
            {
                var c = hit.collider;
                if (hit.distance <= 0f || c is CharacterController || c.attachedRigidbody != null
                    || c.GetComponentInParent<PlayerAvatar>() != null || c.GetComponentInParent<StandUpScooter>() != null) continue;
                if (Vector3.Dot(hit.normal, Vector3.up) > 0.6f) continue; // the floor
                if (hit.distance < distance) { distance = hit.distance; found = true; }
            }
            return found;
        }

        // A moving scooter (anyone's, as you see it here, so there's no delay) that runs into
        // you: you're kept in front of it and thrown the way it's going, harder the faster it
        // was going. From behind or the side it just nudges you out of the way.
        void PushedByScooters(float dt)
        {
            if (m_Ride != null || m_Seat != null || !m_Controller.enabled) return;
            Vector3 me = transform.position;
            foreach (var scooter in Multiplayer.Scooters)
            {
                if (scooter == null) continue;
                float speed = Mathf.Abs(scooter.Speed);
                if (speed < 0.3f) continue;
                Vector3 heading = scooter.transform.forward * Mathf.Sign(scooter.Speed);
                heading.y = 0f;
                heading.Normalize();
                Vector3 rel = me - scooter.transform.position;
                rel.y = 0f;
                // Lying down you take up more room.
                float gap = scooter.footprintRadius + (Lying ? 0.45f : m_Controller.radius);
                if (rel.magnitude >= gap) continue;
                float ahead = Vector3.Dot(rel, heading);
                Vector3 side = rel - heading * ahead;
                if (ahead < -0.05f)
                {
                    // Behind it: just out of its way.
                    Vector3 dir = rel.magnitude > 0.001f ? rel.normalized : -heading;
                    m_Controller.Move(dir * (gap - rel.magnitude + 0.01f));
                    continue;
                }
                // In front of it: moved to stay just ahead, never through it.
                float clear = Mathf.Sqrt(Mathf.Max(0f, gap * gap - side.sqrMagnitude)) + 0.02f;
                if (clear > ahead) m_Controller.Move(heading * (clear - ahead));
                if (Time.time - m_LastScooterHit > 0.5f)
                {
                    m_LastScooterHit = Time.time;
                    Vector3 sideways = side.sqrMagnitude > 0.0001f ? side.normalized * Mathf.Clamp01(side.magnitude / gap) * 0.5f : Vector3.zero;
                    TakeHit(heading + sideways, speed);
                    // (The rider's own game makes the sound for a ridden one.)
                    if (speed > StandUpScooter.CrashSpeed && !Multiplayer.ScooterRiddenByOther(scooter)) scooter.Crash(speed);
                }
                else if (!Down && Vector3.Dot(m_Shove, heading) < speed)
                    m_Shove += heading * (speed - Vector3.Dot(m_Shove, heading)); // carried along
            }
        }

        // ---------- Cursor and hints ----------

        // In a browser the page captures the pointer itself, inside clicks (browsers allow it
        // nowhere else), and the browser's actual state is what counts: the game never thinks
        // the pointer is captured while it's still showing.
        void SyncPointer()
        {
            if (!WebPage.InBrowser) return;
            bool computer = InputModeManager.Mode == ControlMode.KeyboardMouse;
            WebPage.SetPointer(computer, m_Hud != null && m_Hud.IsMenuOpen);
            bool locked = WebPage.PointerLocked;
            if (locked != (Cursor.lockState == CursorLockMode.Locked))
            {
                Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
                Cursor.visible = !locked;
            }
        }

        void SetCursorLocked(bool locked)
        {
            // In a browser, only letting go is done from here; capturing happens in the page.
            if (WebPage.InBrowser && locked) return;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        // Keeps the on-screen prompts in step with what you're near or looking at.
        // Computer: the centre plus (grows over something usable) and key hints along the bottom.
        // Phone: Mount / Unmount and Play / Pause + Skip buttons in the bottom-right corner.
        void UpdatePrompts()
        {
            if (m_Hud == null) return;
            bool computer = InputModeManager.Mode == ControlMode.KeyboardMouse;
            // The website's Enter screen counts as a menu: nothing is shown or timed behind it.
            bool menu = m_Hud.IsMenuOpen || WebPage.Covered;

            if (computer)
            {
                // Start the jump hint's few seconds once the menu is out of the way.
                if (m_JumpHintPending && !menu)
                {
                    m_JumpHintPending = false;
                    m_JumpHintShown = 0f;
                }
                if (!menu && m_JumpHintShown < JumpHintSeconds + JumpHintFade)
                    m_JumpHintShown += Mathf.Min(Time.unscaledDeltaTime, 0.05f);
                var target = menu ? null : InteractableAt(ScreenCentre);
                m_Hud.SetCrosshair(!menu, target != null);
                m_Hud.SetContextButtons();
                // (Always shown: you stay down until you click.)
                if (!menu && Lying) m_Hud.SetKeyHints((Click, "to get up"));
                else if (menu || !HintSettings.Enabled) m_Hud.SetKeyHints();
                else if (target is MusicSpeaker hinted)
                {
                    // Skipping only works while music is playing. Without a mouse (a trackpad),
                    // N skips instead of right click.
                    var playPause = MouseSettings.Enabled ? ("Left click", "play / pause") : ("Click", "pause / play");
                    var next = MouseSettings.Enabled ? ("Right click", "next song") : ("N", "next song");
                    if (hinted.IsOn) m_Hud.SetKeyHints(playPause, next);
                    else m_Hud.SetKeyHints(playPause);
                }
                else if (m_Seat != null) m_Hud.SetKeyHints((Click, "to stand up"));
                else if (target is Seat) m_Hud.SetKeyHints((Click, "to sit"));
                else if (target is MarkerBoard) m_Hud.SetKeyHints((Click, "to write or draw"));
                else if (m_Ride != null) m_Hud.SetKeyHints((Click, "to get off"));
                else if (target is StandUpScooter) m_Hud.SetKeyHints((Click, "to mount"));
                else m_Hud.SetKeyHints();
                // The first few seconds: how to jump and sprint, on two lines at the bottom.
                bool tutorial = !menu && HintSettings.Enabled && m_JumpHintShown < JumpHintSeconds + JumpHintFade
                                && target == null && m_Seat == null && m_Ride == null && !Down;
                if (tutorial)
                    m_Hud.SetTutorial(1f - Mathf.Clamp01((m_JumpHintShown - JumpHintSeconds) / JumpHintFade),
                        ("Jump", "<b>SPACE</b> to jump"), ("Sprint", "<b>Double tap W</b> / <b>SHIFT</b> to sprint"));
                else m_Hud.SetTutorial(0f);
                return;
            }

            m_Hud.SetCrosshair(false, false);
            m_Hud.SetKeyHints();
            m_Hud.SetTutorial(0f);
            // You can't jump on the scooter or from a chair, so the Jump button goes away then;
            // and seated, there's no walking either, so the joystick goes too.
            m_Hud.SetJumpVisible(m_Ride == null && m_Seat == null && !menu);
            m_Hud.SetSprintVisible(m_Ride == null && m_Seat == null && !menu);
            m_Hud.SetJoystickVisible(m_Seat == null && !menu);
            if (menu) { m_Hud.SetContextButtons(); return; }
            if (Down) { if (Lying) m_Hud.SetContextButtons(("Get up", GetUp)); else m_Hud.SetContextButtons(); return; }
            if (m_Ride != null) { m_Hud.SetContextButtons(("Unmount", StepOff)); return; }
            if (m_Seat != null) { m_Hud.SetContextButtons(("Stand up", StandUp)); return; }

            var near = NearbyInView();
            if (near is StandUpScooter scooter)
                m_Hud.SetContextButtons(("Mount", () => StepOn(scooter)));
            else if (near is Seat seat)
                m_Hud.SetContextButtons(("Sit", () => SitDown(seat)));
            else if (near is MarkerBoard nearBoard && nearBoard.InFront(transform.position) && !MarkerBoard.LocalBusy)
                m_Hud.SetContextButtons(("Write/Draw", () => BoardPanel.Instance.Open()));
            else if (near is MusicSpeaker speaker)
            {
                if (speaker.IsOn) m_Hud.SetContextButtons(("Play / Pause", speaker.TogglePlayPause), ("Skip", speaker.Skip));
                else m_Hud.SetContextButtons(("Play / Pause", speaker.TogglePlayPause));
            }
            else
                m_Hud.SetContextButtons();
        }

        // The closest scooter or speaker that's within reach and on screen, for the phone buttons.
        /// <summary>Testing: what's nearby and why it is or isn't offered.</summary>
        public string DebugNear()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var thing in m_Interactables)
            {
                if (thing == null) continue;
                bool reachable = false;
                foreach (var c in thing.GetComponentsInChildren<Collider>()) if (WithinReach(c)) { reachable = true; break; }
                Vector3 view = m_Camera.WorldToViewportPoint(Centre(thing));
                if (reachable || thing is MarkerBoard) sb.Append(thing.name + " reach " + reachable + " view " + view.ToString("F2") + "; ");
            }
            var near = NearbyInView();
            sb.Append(" nearest " + (near != null ? near.name : "none"));
            return sb.ToString();
        }

        Component NearbyInView()
        {
            Component best = null;
            float bestDistance = float.MaxValue;
            foreach (var thing in m_Interactables)
            {
                if (thing == null || !thing.gameObject.activeInHierarchy) continue;
                bool reachable = false;
                foreach (var c in thing.GetComponentsInChildren<Collider>())
                    if (WithinReach(c)) { reachable = true; break; }
                if (!reachable) continue;

                Vector3 centre = Centre(thing);
                Vector3 view = m_Camera.WorldToViewportPoint(centre);
                // The board only while you're facing it (not with your back to it). Right up
                // against it its middle can be off screen; in front of it and facing it is enough.
                bool boardInFront = false;
                if (thing is MarkerBoard nearBoard)
                {
                    Vector3 look = cameraPivot.forward;
                    look.y = 0f;
                    if (look.sqrMagnitude < 0.0001f || Vector3.Dot(look.normalized, -nearBoard.Facing) < 0.35f) continue;
                    boardInFront = nearBoard.InFront(transform.position);
                }
                if (!boardInFront && (view.z <= 0f || view.x < 0f || view.x > 1f || view.y < 0f || view.y > 1f)) continue;

                float distance = Vector3.Distance(transform.position, centre);
                if (distance < bestDistance) { bestDistance = distance; best = thing; }
            }
            return best;
        }

        static Vector3 Centre(Component thing)
        {
            var renderers = thing.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return thing.transform.position;
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            return b.center;
        }

        void OnApplicationFocus(bool focus)
        {
            if (!focus) SetCursorLocked(false);
        }
    }
}
