using Unity.Cinemachine;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace Oxipital
{
    /// <summary>
    /// Spline camera: rides a spline (<see cref="SplineContainer"/>, its first spline) at <see cref="speed"/>, for
    /// repeatable camera moves (the film) or a guided flight (the show). This object is the rig: it carries the pose on
    /// the spline, and <c>CM Spline Camera</c> follows it (CinemachineFollow, LockToTarget) and turns with it
    /// (CinemachineRotateWithFollowTarget, its damping = <see cref="rotationDamping"/>), with the shared Perlin noise
    /// and Cinemachine's blends between camera modes.
    ///
    /// * Travel: speed changes, offsets and the ground are smoothed here (the camera stays exactly on the path at a
    ///   steady speed: no lag); <see cref="distance"/> can be set to jump.
    /// * Rotation (<see cref="rotationMode"/>): along the spline (aimed <see cref="aimAhead"/> metres ahead, level
    ///   horizon unless <see cref="followPitch"/> / <see cref="followRoll"/>, optional bird-like <see cref="bank"/>), the
    ///   gamepad looking around on top (yaw1D / pitch1D / roll1D, same signs as the Spaceship), or the gamepad alone.
    /// * Drift: a brownian wandering of the position and the view, seeded (<see cref="driftSeed"/>), so a film render
    ///   repeats exactly (Cinemachine's Perlin noise reseeds at random on every run).
    /// * Ground: a scene can hand an <see cref="ICameraGround"/> (<see cref="SetGround"/>): the camera then rides at
    ///   <see cref="altitude"/> above it, plus the spline's own height above its container.
    ///
    /// A scene that owns the spline hands it at run time (<see cref="SetSpline"/>): show scenes come and go, the camera
    /// rig stays. Roll keyed with a CinemachineSplineRoll on the spline's object is used by <see cref="followRoll"/>.
    /// Time comes from Time.deltaTime only: a Recorder capture at a fixed frame rate is deterministic.
    /// </summary>
    [OSCQuery.DoNotExposeChildren]
    public class SplineMovement : CameraMovement
    {
        public enum RotationMode { Spline, SplineAndLook, Free }

        [Header("Spline")]
        [Tooltip("The spline to ride (the container's first spline). A show scene usually hands its own at run time (SetSpline).")]
        [OSCQuery.DoNotExpose] public SplineContainer spline;
        [Tooltip("Distance along the spline (m) where the ride starts (ResetRide).")]
        public float startDistance;
        [Tooltip("At the end of the spline, start again from the other end (a cut unless the spline is closed). Off: stop at the end.")]
        public bool loop = true;
        [Tooltip("When the camera switches to Spline, join the spline at the point nearest to the previous camera. Off: carry on from where the ride was.")]
        public bool joinNearestPoint = true;

        [Header("Travel")]
        [Tooltip("Speed along the spline (m/s), negative = backwards.")]
        public float speed = 20f;
        [Tooltip("Seconds to reach a new speed.")]
        [Range(0f, 10f)] public float speedSmoothing = 1f;
        [Tooltip("Where the camera is along the spline (m). Set it to jump there.")]
        public float distance;
        [Tooltip("Offset from the spline (m): x to the right, y up.")]
        public Vector3 offset;
        [Tooltip("Seconds for offset and altitude changes to settle.")]
        [Range(0f, 10f)] public float offsetSmoothing = 1f;

        [Header("Ground (when the scene gives one, SetGround)")]
        [Tooltip("Ride at altitude above the scene's ground, plus the spline's height above its object. Off, or no ground: the spline's own height.")]
        public bool followGround = true;
        [Tooltip("Height above the ground (m).")]
        public float altitude = 100f;
        [Tooltip("How far ahead along the spline (m) the ground is anticipated, so the camera rises before a hill.")]
        public float groundLookAhead = 500f;
        [Tooltip("Seconds for the height to settle on the ground below.")]
        [Range(0.05f, 10f)] public float groundSmoothing = 2.5f;

        [Header("Rotation")]
        [Tooltip("Spline: looks along the spline. Spline And Look: along the spline, the gamepad looks around on top. Free: the gamepad alone turns the camera.")]
        public RotationMode rotationMode = RotationMode.SplineAndLook;
        [Tooltip("Aim at the point this far ahead on the spline (m) instead of along its tangent: softer turns. 0 = tangent.")]
        public float aimAhead = 40f;
        [Tooltip("How much the spline's climbs and dives pitch the camera. 0 = the horizon stays level.")]
        [Range(0f, 1f)] public float followPitch = 0f;
        [Tooltip("How much the spline's own roll (knot rotations, a CinemachineSplineRoll on the spline) rolls the camera.")]
        [Range(0f, 1f)] public float followRoll = 0f;
        [Tooltip("Which way is up: 0 = the world's (level horizon, followPitch and followRoll apply), 1 = the spline's own up (its knot rotations): the camera turns with the spline in every direction, up a wall or round a loop. Offsets follow that up too.")]
        [Range(0f, 1f)] public float splineUp = 0f;
        [Tooltip("Lean into the turns like a bird: degrees of roll per 10 degrees/s of turn.")]
        [Range(0f, 30f)] public float bank = 0f;
        [Tooltip("Seconds the view takes to follow the ride's turns (Cinemachine rotation damping).")]
        [Range(0f, 5f)] public float rotationDamping = 0.5f;

        [Header("Look (gamepad, Spline And Look and Free)")]
        [Tooltip("Right (+) or left (-).")]
        [Range(-1f, 1f)] public float yaw1D;
        [Tooltip("Up (+) or down (-).")]
        [Range(-1f, 1f)] public float pitch1D;
        [Tooltip("Roll right (+) or left (-).")]
        [Range(-1f, 1f)] public float roll1D;
        [Tooltip("Stick values below this count as centred (a gamepad at rest never sends exactly 0).")]
        [Range(0f, 0.5f)] public float stickDeadzone = 0.1f;
        [Tooltip("Seconds for the view to follow the sticks. Spline And Look: a spring, it speeds up and slows down smoothly (no jerk when swinging round).")]
        [Range(0f, 3f)] public float lookSmoothing = 0.6f;
        [Tooltip("Spline And Look: fastest turn of the view (degrees/s), so a swing to the back or back to the path stays slow.")]
        [Range(10f, 720f)] public float maxLookSpeed = 90f;
        [Tooltip("Spline And Look: the stick points the view, like turning the head. Full stick = these angles from the spline's view (degrees); released, the view comes back to the spline.")]
        [Range(0f, 170f)] public float maxLookYaw = 120f;
        [Range(0f, 89f)] public float maxLookPitch = 60f;
        [Range(0f, 90f)] public float maxLookRoll = 30f;
        [Tooltip("Free: the stick turns the camera, at these speeds at full stick (degrees/s).")]
        [Range(0f, 360f)] public float yawSpeed = 60f;
        [Range(0f, 360f)] public float pitchSpeed = 45f;
        [Range(0f, 360f)] public float rollSpeed = 45f;

        [Header("Drift (brownian)")]
        [Tooltip("Wandering of the position around the ride (m).")]
        public float driftPosition = 0f;
        [Tooltip("Wandering of the view (degrees).")]
        public float driftRotation = 0f;
        [Tooltip("How fast it wanders (Hz).")]
        [Range(0.01f, 2f)] public float driftFrequency = 0.1f;
        [Tooltip("Same seed, same wandering: a film render repeats.")]
        public int driftSeed = 0;

        CinemachineFollow _follow;
        CinemachineRotateWithFollowTarget _rotate;
        CinemachineSplineRoll _splineRoll;
        SplineContainer _rollOwner;
        ICameraGround _ground;

        bool _started, _groundReady, _flatReady;
        float _speed;                       // smoothed speed
        Vector3 _offset;                    // smoothed offset
        float _altitude;                    // smoothed altitude
        float _groundHeight;                // smoothed, anticipated ground height
        Vector3 _flat;                      // last level travel direction
        float _turnRate;                    // smoothed, degrees/s
        Vector3 _lookRate;                  // Free: smoothed turn rates (degrees/s): yaw, pitch, roll
        Vector3 _look, _lookVelocity;       // Spline And Look: view angles from the spline's (degrees), their speed
        bool _resetPending;                 // ResetRide while another camera is shown: the next take-over starts there
        Quaternion _free = Quaternion.identity;
        float _driftTime;
        int _driftOffsetsSeed = int.MinValue;
        readonly float[] _driftOffsets = new float[6];

        public override void Init()
        {
            base.Init();
            type = CameraController.CameraMovementType.Spline;
            _follow = virtualCamera.GetComponentInChildren<CinemachineFollow>();
            _rotate = virtualCamera.GetComponentInChildren<CinemachineRotateWithFollowTarget>();
            if (!_started)
            {
                _started = true;
                _free = transform.rotation;
                ResetRide();
            }
        }

        /// <summary>Hand the spline to ride (null = none). Restart: jump to startDistance at full speed. Otherwise the
        /// ride carries on from the point of the new spline nearest to the camera, at the same speed and look (a
        /// scene can chain splines this way: the new one starts where the camera is).</summary>
        public void SetSpline(SplineContainer container, bool restart = true)
        {
            spline = container;
            if (restart)
            {
                ResetRide();
                return;
            }
            _flatReady = false;
            JoinNearest(transform.position);
        }

        // Distance of the point of the spline nearest to a world position
        void JoinNearest(Vector3 position)
        {
            if (!HasSpline()) return;
            Transform t = spline.transform;
            var local = Matrix4x4.TRS(t.position, t.rotation, Vector3.one).inverse.MultiplyPoint3x4(position);
            SplineUtility.GetNearestPoint(spline.Spline, (float3)local, out float3 _, out float n);
            distance = spline.Spline.ConvertIndexUnit(n, PathIndexUnit.Normalized, PathIndexUnit.Distance);
        }
        /// <summary>Hand the scene's ground (null = none): the camera rides at altitude above it.</summary>
        public void SetGround(ICameraGround ground)
        {
            _ground = ground;
            _groundReady = false;
        }

        /// <summary>Back to startDistance, at full speed. Called while another camera is shown, the switch to this
        /// one starts there (instead of joining the nearest point).</summary>
        public void ResetRide()
        {
            _resetPending = !_isActive;
            distance = startDistance;
            _speed = speed;
            _offset = offset;
            _altitude = altitude;
            _groundReady = false;
            _flatReady = false;
            _turnRate = 0f;
            ResetLook();
        }

        /// <summary>Back to the spline's view (Spline And Look) and stop the turns.</summary>
        public void ResetLook()
        {
            _look = Vector3.zero;
            _lookVelocity = Vector3.zero;
            _lookRate = Vector3.zero;
        }

        public override void Reset(float duration)
        {
            ResetLook();
        }

        public override void UpdateZOffset(float offset)
        {
            if (_follow != null) _follow.FollowOffset = new Vector3(0f, 0f, -offset);
        }

        public override void SetCameraTransform(Vector3 position, Quaternion rotation)
        {
            // Called when this camera takes over: carry on from the previous camera's view
            _free = rotation;
            ResetLook();
            _flatReady = false;
            _groundReady = false;
            if (_resetPending)
            {
                // ResetRide was asked while another camera was shown: start the ride there, at full speed
                _resetPending = false;
                return;
            }
            _speed = 0f;
            if (joinNearestPoint) JoinNearest(position);
        }

        void Update()
        {
            if (!_isActive) return;         // the ride waits while another camera is shown
            Ride(Time.deltaTime);
        }

        bool HasSpline() => spline != null && spline.Splines.Count > 0 && spline.Spline.Count >= 2;

        static float Damp(float seconds, float dt) => seconds <= 0f ? 1f : 1f - Mathf.Exp(-dt / seconds);

        // Stick value past the deadzone, rescaled to -1..1
        float Stick(float value)
        {
            float a = Mathf.Abs(Mathf.Clamp(value, -1f, 1f));
            return a <= stickDeadzone ? 0f : Mathf.Sign(value) * (a - stickDeadzone) / Mathf.Max(1f - stickDeadzone, 1e-3f);
        }

        // Distance along the spline, wrapped or clamped
        float Wrap(float d, float length) => loop ? Mathf.Repeat(d, length) : Mathf.Clamp(d, 0f, length);

        // Pose on the spline (world), at a distance in metres
        void Sample(float d, float length, out Vector3 position, out Quaternion rotation)
        {
            Spline s = spline.Spline;
            float t = s.ConvertIndexUnit(Wrap(d, length), PathIndexUnit.Distance, PathIndexUnit.Normalized);
            SplineUtility.Evaluate(s, t, out float3 p, out float3 tangent, out float3 up);
            Vector3 fwd = tangent, upv = up;
            if (Vector3.Cross(fwd, upv).sqrMagnitude < 1e-10f)
            {
                fwd = Vector3.forward;
                upv = Vector3.up;
            }
            rotation = Quaternion.LookRotation(fwd, upv);
            // Extra roll keyed on the spline (CinemachineSplineRoll), as Cinemachine's own spline cameras use it
            if (_splineRoll != null && _splineRoll.enabled && _splineRoll.Roll != null)
            {
                float roll = _splineRoll.Easing
                    ? _splineRoll.Roll.Evaluate(s, t, PathIndexUnit.Normalized, new CinemachineSplineRoll.LerpRollDataWithEasing())
                    : _splineRoll.Roll.Evaluate(s, t, PathIndexUnit.Normalized, new CinemachineSplineRoll.LerpRollData());
                rotation *= Quaternion.AngleAxis(roll, Vector3.forward);
            }
            // The container's position and rotation (not its scale, like Cinemachine's spline cameras)
            Transform c = spline.transform;
            position = c.position + c.rotation * (Vector3)p;
            rotation = c.rotation * rotation;
        }

        void Ride(float dt)
        {
            if (_rotate != null && _rotate.Damping != rotationDamping) _rotate.Damping = rotationDamping;
            if (!HasSpline()) return;
            if (_rollOwner != spline)
            {
                _rollOwner = spline;
                _splineRoll = spline.GetComponent<CinemachineSplineRoll>();
            }

            float length = spline.Spline.GetLength();
            if (length <= 0f) return;

            // ---- travel
            _speed = Mathf.Lerp(_speed, speed, Damp(speedSmoothing, dt));
            distance = Wrap(distance + _speed * dt, length);
            float k = Damp(offsetSmoothing, dt);
            _offset = Vector3.Lerp(_offset, offset, k);
            _altitude = Mathf.Lerp(_altitude, altitude, k);
            float direction = _speed < 0f ? -1f : 1f;

            Sample(distance, length, out Vector3 onSpline, out Quaternion splineRotation);

            // Travel direction: towards the point aimAhead metres further, or along the tangent
            Vector3 forward = splineRotation * Vector3.forward * direction;
            if (aimAhead > 0.01f)
            {
                Sample(distance + direction * aimAhead, length, out Vector3 aim, out _);
                if ((aim - onSpline).sqrMagnitude > 1e-4f) forward = aim - onSpline;
            }
            forward.Normalize();
            // Up of the ride: the world's (level horizon) or the spline's own (walls, loops), blended by splineUp
            Vector3 up = splineUp <= 0f ? Vector3.up : Vector3.Slerp(Vector3.up, splineRotation * Vector3.up, splineUp).normalized;
            Vector3 flat = Vector3.ProjectOnPlane(forward, up);
            if (flat.sqrMagnitude < 1e-8f) flat = Vector3.ProjectOnPlane(_flatReady ? _flat : Vector3.forward, up);
            if (flat.sqrMagnitude < 1e-8f) flat = Vector3.Cross(Vector3.right, up);
            flat.Normalize();
            Vector3 right = Vector3.Cross(up, flat);

            // ---- position: spline + offset (right, up), or altitude above the ground
            float splineHeight = onSpline.y - spline.transform.position.y;
            Vector3 position = onSpline + right * _offset.x + up * _offset.y;
            if (followGround && _ground != null)
            {
                float target = _ground.GetGroundHeight(position);
                for (int i = 1; i <= 4; i++)
                {
                    Sample(distance + direction * groundLookAhead * i / 4f, length, out Vector3 ahead, out _);
                    target = Mathf.Max(target, _ground.GetGroundHeight(ahead + right * _offset.x) - _altitude * 0.25f * i);
                }
                _groundHeight = _groundReady ? Mathf.Lerp(_groundHeight, target, Damp(groundSmoothing, dt)) : target;
                _groundReady = true;
                position.y = _groundHeight + _altitude + splineHeight + _offset.y;
            }

            // ---- rotation along the spline: heading around the up, then pitch, roll and bank
            float climb = Vector3.SignedAngle(flat, forward, right);   // positive = nose down
            Quaternion path = Quaternion.LookRotation(flat, up) * Quaternion.AngleAxis(climb * followPitch, Vector3.right);

            float turn = _flatReady && dt > 0f ? Vector3.SignedAngle(Vector3.ProjectOnPlane(_flat, up), flat, up) / dt : 0f;
            _turnRate = Mathf.Lerp(_turnRate, turn, Damp(0.5f, dt));
            _flat = flat;
            _flatReady = true;

            float roll = -Mathf.Clamp(_turnRate / 10f * bank, -60f, 60f);
            if (followRoll > 0f)
            {
                Vector3 pathForward = path * Vector3.forward;
                Vector3 splineUpVector = Vector3.ProjectOnPlane(splineRotation * Vector3.up, pathForward);
                if (splineUpVector.sqrMagnitude > 1e-6f)
                    roll += Vector3.SignedAngle(path * Vector3.up, splineUpVector, pathForward) * followRoll;
            }
            path *= Quaternion.AngleAxis(roll, Vector3.forward);

            // ---- gamepad (deadzone, then smoothed)
            Vector3 stick = new Vector3(Stick(yaw1D), Stick(pitch1D), Stick(roll1D));
            float lookK = Damp(lookSmoothing, dt);

            Quaternion rotation;
            switch (rotationMode)
            {
                case RotationMode.Free:
                    // The stick turns the camera (rates)
                    _lookRate = Vector3.Lerp(_lookRate, Vector3.Scale(stick, new Vector3(yawSpeed, pitchSpeed, rollSpeed)), lookK);
                    _free = (_free * Quaternion.AngleAxis(_lookRate.x * dt, Vector3.up)
                                   * Quaternion.AngleAxis(-_lookRate.y * dt, Vector3.right)
                                   * Quaternion.AngleAxis(-_lookRate.z * dt, Vector3.forward)).normalized;
                    rotation = _free;
                    break;
                case RotationMode.SplineAndLook:
                    // The stick points the view from the spline's (angles): released, it comes back
                    // A spring (critically damped, speed capped): the view eases in and out of a swing, never jumps
                    Vector3 target = Vector3.Scale(stick, new Vector3(maxLookYaw, maxLookPitch, maxLookRoll));
                    _look = Vector3.SmoothDamp(_look, target, ref _lookVelocity, Mathf.Max(lookSmoothing, 0.01f), maxLookSpeed, dt);
                    rotation = path * Quaternion.AngleAxis(_look.x, Vector3.up)
                                    * Quaternion.AngleAxis(-_look.y, Vector3.right)
                                    * Quaternion.AngleAxis(-_look.z, Vector3.forward);
                    _free = rotation;
                    break;
                default:
                    rotation = path;
                    _free = rotation;
                    break;
            }

            // ---- brownian drift
            if (driftPosition > 0f || driftRotation > 0f)
            {
                _driftTime += dt * driftFrequency;
                if (_driftOffsetsSeed != driftSeed)
                {
                    _driftOffsetsSeed = driftSeed;
                    for (int i = 0; i < _driftOffsets.Length; i++)
                        _driftOffsets[i] = (math.hash(new int2(driftSeed, i)) % 100000u) * 0.01f;
                }
                position += path * new Vector3(Fbm(0), Fbm(1), Fbm(2)) * driftPosition;
                rotation *= Quaternion.Euler(Fbm(3) * driftRotation, Fbm(4) * driftRotation, Fbm(5) * driftRotation * 0.5f);
            }

            transform.SetPositionAndRotation(position, rotation);
        }

        // Fractal (brownian) noise in about -1..1, one channel per offset
        float Fbm(int channel)
        {
            float o = _driftOffsets[channel];
            float t = _driftTime;
            return (Unity.Mathematics.noise.snoise(new float2(t, o))
                  + 0.5f * Unity.Mathematics.noise.snoise(new float2(t * 2.03f, o + 17.1f))
                  + 0.25f * Unity.Mathematics.noise.snoise(new float2(t * 4.07f, o + 43.7f))) / 1.75f;
        }

        /// <summary>Pose on the spline at a distance (m), without offset, ground, look nor drift. False without a spline.</summary>
        public bool EvaluatePose(float atDistance, out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = Quaternion.identity;
            if (!HasSpline()) return false;
            float length = spline.Spline.GetLength();
            if (length <= 0f) return false;
            Sample(atDistance, length, out position, out rotation);
            return true;
        }
    }
}
