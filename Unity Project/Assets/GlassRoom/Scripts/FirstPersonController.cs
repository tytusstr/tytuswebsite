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
        public float walkSpeed = 2.85f;
        public float runSpeed = 5.65f;
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
        public float walkStride = 1.2f;
        public float runStride = 1.75f;
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
            if (cameraPivot == null)
                cameraPivot = GetComponentInChildren<Camera>().transform;
            m_Camera = cameraPivot.GetComponent<Camera>();
            m_EyeHeight = cameraPivot.localPosition.y;
            m_Yaw = transform.eulerAngles.y;
            m_Pitch = cameraPivot.localEulerAngles.x;
            if (m_Pitch > 180f) m_Pitch -= 360f;

            m_Hud = ControlsHUD.Create();
            m_Interactables.AddRange(FindObjectsByType<StandUpScooter>(FindObjectsSortMode.None));
            m_Interactables.AddRange(FindObjectsByType<MusicSpeaker>(FindObjectsSortMode.None));
            m_Interactables.AddRange(FindObjectsByType<Seat>(FindObjectsSortMode.None));
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
                // Seated: look around freely; walking or jumping stands you up.
                ApplyLook(look);
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
                ApplyLook(look);
                ApplyMove(move, run);
            }
            UpdatePrompts();
        }

        void ReadInput(out Vector2 move, out Vector2 look, out bool run)
        {
            move = Vector2.zero;
            look = Vector2.zero;
            run = false;
            // Read every frame so a press while the menu is open doesn't jump later.
            bool phoneJump = m_Hud.ConsumeJump();
            m_JumpPressed = false;

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
                // The stick sets the direction and how far it's pushed sets the speed, up to the
                // same walking speed as on a computer; there's no running on touch.
                move = m_Hud.Joystick.Value;
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
                run = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
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

            // Standing on something steeper than you can stand on (like the side of the lamp
            // shade) doesn't count as ground: you slide off it, and can't jump from it.
            bool steep = m_Controller.isGrounded && Vector3.Angle(m_GroundNormal, Vector3.up) > m_Controller.slopeLimit + 1f;
            bool grounded = m_Controller.isGrounded && !steep;
            if (steep)
            {
                var away = new Vector3(m_GroundNormal.x, 0f, m_GroundNormal.z);
                if (away.sqrMagnitude < 0.0001f) away = -transform.forward;
                horizontal += away.normalized * SlideSpeed;
            }
            float vertical = grounded ? -2f : Mathf.Min(m_Velocity.y, steep ? 0f : m_Velocity.y) - gravity * dt;
            m_GroundedTime = grounded ? m_GroundedTime + dt : 0f;
            if (m_JumpPressed && grounded) { vertical = jumpSpeed; m_GroundedTime = 0f; }
            m_Velocity = new Vector3(horizontal.x, vertical, horizontal.z);
            m_GroundNormal = Vector3.up;
            m_Controller.Move(m_Velocity * dt);

            // Use the actual distance moved so walking into a wall makes no steps.
            Vector3 actual = m_Controller.velocity;
            float groundSpeed = new Vector2(actual.x, actual.z).magnitude;
            UpdateSteps(groundSpeed, dt);
        }

        // The surface under your feet, from the last move (see ApplyMove).
        Vector3 m_GroundNormal = Vector3.up;
        const float SlideSpeed = 4f;

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            // Something below the middle of you, i.e. what you're standing on.
            if (hit.point.y < transform.position.y + m_Controller.radius)
                m_GroundNormal = hit.normal;
        }

        void UpdateSteps(float groundSpeed, float dt)
        {
            bool moving = m_Controller.isGrounded && groundSpeed > 0.25f;
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
            Vector3 nearestPoint = target.ClosestPoint(feet);
            Vector2 apart = new Vector2(nearestPoint.x - feet.x, nearestPoint.z - feet.z);
            return apart.magnitude <= interactRange;
        }

        // Computer clicks from the centre plus. Scooter: left click steps on, and while riding
        // left click steps off (E works too).
        // Speaker: left click plays or pauses, right click skips to the next song.
        void ClickAt(Vector2 screenPosition, bool rightButton)
        {
            var thing = InteractableAt(screenPosition);
            // Sitting: a click stands you back up (the speaker still works from the chair).
            if (m_Seat != null && !(thing is MusicSpeaker))
            {
                if (!rightButton) StandUp();
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
        const float SeatedEyeAboveSeat = 0.74f;

        // Sits in the chair, facing out of it. You can look around but not walk; moving,
        // jumping or clicking stands you back up.
        void SitDown(Seat seat)
        {
            m_Seat = seat;
            m_Velocity = Vector3.zero;
            m_Controller.enabled = false;
            transform.position = seat.FloorPoint;
            m_Yaw = Quaternion.LookRotation(seat.Facing).eulerAngles.y;
            transform.rotation = Quaternion.Euler(0f, m_Yaw, 0f);
            m_Pitch = 0f;
            cameraPivot.localRotation = Quaternion.identity;
            cameraPivot.localPosition = new Vector3(0f, seat.seatHeight + SeatedEyeAboveSeat, 0f);
            seat.PlaySitSound();
        }

        void StandUp()
        {
            var seat = m_Seat;
            if (seat == null) return;
            m_Seat = null;
            // Step out in front of the chair, or stay put if that's blocked.
            Vector3 front = seat.FloorPoint + seat.Facing * 0.75f;
            float r = m_Controller.radius;
            if (!Physics.CheckCapsule(front + Vector3.up * (r + 0.05f), front + Vector3.up * (m_Controller.height - r), r, ~0, QueryTriggerInteraction.Ignore))
                transform.position = front + Vector3.up * 0.02f;
            cameraPivot.localPosition = new Vector3(0f, m_EyeHeight, 0f);
            m_Controller.enabled = true;
            m_Velocity = Vector3.zero;
        }

        void StepOn(StandUpScooter scooter)
        {
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
            m_Yaw = scooter.transform.eulerAngles.y;
            transform.rotation = Quaternion.Euler(0f, m_Yaw, 0f);
            m_Controller.radius = scooter.footprintRadius;
            m_Controller.stepOffset = 0.05f;
            m_Controller.enabled = true;

            cameraPivot.localPosition = new Vector3(0f, m_EyeHeight + scooter.deckHeight, scooter.riderOffsetZ);
            m_RideLookYaw = 0f;
            m_RideTurn = 0f;
            m_Pitch = scooter.ridePitch;
            scooter.SetRidden(true);
        }

        void StepOff()
        {
            var scooter = m_Ride;
            if (scooter == null) return;
            m_Ride = null;
            scooter.SetRidden(false);

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
            m_Controller.Move(m_Velocity * dt);

            // Bumping into something stops the scooter instead of grinding against it.
            Vector3 actual = m_Controller.velocity;
            float along = Vector3.Dot(new Vector3(actual.x, 0f, actual.z), transform.forward);
            // Only judged once it's properly moving, so pulling away from rest isn't mistaken for a bump.
            if (Mathf.Abs(m_RideSpeed) > 0.5f && Mathf.Abs(along) < Mathf.Abs(m_RideSpeed) * 0.5f) m_RideSpeed = along;

            scooter.transform.SetPositionAndRotation(transform.position, transform.rotation);
            scooter.UpdateMotion(m_RideSpeed);
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
                if (menu || !HintSettings.Enabled) m_Hud.SetKeyHints();
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
                else if (m_Ride != null) m_Hud.SetKeyHints((Click, "to get off"));
                else if (target is StandUpScooter) m_Hud.SetKeyHints((Click, "to mount"));
                else if (m_JumpHintShown < JumpHintSeconds + JumpHintFade)
                    m_Hud.SetKeyHints(1f - Mathf.Clamp01((m_JumpHintShown - JumpHintSeconds) / JumpHintFade), ("Space", "to jump"));
                else m_Hud.SetKeyHints();
                return;
            }

            m_Hud.SetCrosshair(false, false);
            m_Hud.SetKeyHints();
            // You can't jump on the scooter or from a chair, so the Jump button goes away then;
            // and seated, there's no walking either, so the joystick goes too.
            m_Hud.SetJumpVisible(m_Ride == null && m_Seat == null);
            m_Hud.SetJoystickVisible(m_Seat == null);
            if (menu) { m_Hud.SetContextButtons(); return; }
            if (m_Ride != null) { m_Hud.SetContextButtons(("Unmount", StepOff)); return; }
            if (m_Seat != null) { m_Hud.SetContextButtons(("Stand up", StandUp)); return; }

            var near = NearbyInView();
            if (near is StandUpScooter scooter)
                m_Hud.SetContextButtons(("Mount", () => StepOn(scooter)));
            else if (near is Seat seat)
                m_Hud.SetContextButtons(("Sit", () => SitDown(seat)));
            else if (near is MusicSpeaker speaker)
            {
                if (speaker.IsOn) m_Hud.SetContextButtons(("Play / Pause", speaker.TogglePlayPause), ("Skip", speaker.Skip));
                else m_Hud.SetContextButtons(("Play / Pause", speaker.TogglePlayPause));
            }
            else
                m_Hud.SetContextButtons();
        }

        // The closest scooter or speaker that's within reach and on screen, for the phone buttons.
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
                if (view.z <= 0f || view.x < 0f || view.x > 1f || view.y < 0f || view.y > 1f) continue;

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
