using F1Racing.Track;
using UnityEngine;

namespace F1Racing.Vehicle
{
    public enum ControlMode
    {
        Player1_WASD,
        Player2_Arrows
    }

    /// <summary>
    /// Modular Formula 1 vehicle controller with arcade-sim hybrid physics:
    /// WheelColliders, speed-dependent downforce, speed-sensitive steering,
    /// 8-speed automatic gearbox/RPM simulation, DRS active aero, emissive brake lights,
    /// skid trails, tire smoke, and dynamic engine pitch.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class F1CarController : MonoBehaviour
    {
        [Header("Identity & Control")]
        public string carName = "F1 Car";
        public ControlMode controlMode = ControlMode.Player1_WASD;

        [Header("Wheel Colliders")]
        public WheelCollider frontLeftCollider;
        public WheelCollider frontRightCollider;
        public WheelCollider rearLeftCollider;
        public WheelCollider rearRightCollider;

        [Header("Wheel Visual Meshes")]
        public Transform frontLeftMesh;
        public Transform frontRightMesh;
        public Transform rearLeftMesh;
        public Transform rearRightMesh;

        [Header("Cockpit & Aero Visuals")]
        public Transform steeringWheelMesh;
        public float maxSteeringWheelRotation = 110f;
        public Transform driverHelmetMesh;
        public Transform drsRearWingFlap;
        public float drsClosedAngleX = 0f;
        public float drsOpenAngleX = -26f;

        [Header("Brake Light")]
        public Renderer brakeLightRenderer;
        public Color brakeLightEmissiveColor = new Color(3.5f, 0.05f, 0.05f);

        [Header("Effects & Audio")]
        public TrailRenderer[] skidTrails;
        public ParticleSystem[] tireSmokeParticles;
        public AudioSource engineAudioSource;
        public float minEnginePitch = 0.7f;
        public float maxEnginePitch = 2.35f;

        [Header("Powertrain & Speed")]
        public float motorTorque = 3300f;
        public float drsMotorBoostMultiplier = 1.18f;
        public float brakeTorque = 5400f;
        public float handbrakeTorque = 6500f;
        public float topSpeedKmh = 325f;
        public float drsTopSpeedKmh = 355f;
        public float reverseTopSpeedKmh = 75f;

        [Header("Steering & Downforce")]
        public float maxSteerAngleLowSpeed = 24f;
        public float maxSteerAngleHighSpeed = 3.8f;
        public float steerSpeed = 7.5f;
        public float downforceCoefficient = 4.5f;
        public float baseLinearDamping = 0.04f;
        public float drsLinearDamping = 0.015f;
        public float drsMinSpeedKmh = 120f;

        // Provisional starting values, tune in play. Assists are part of the shared car spec (same for human and AI).
        [Header("Driver Assists")]
        [Tooltip("0 = raw steering lock, 1 = lock limited to what the front tyres can grip at this speed.")]
        [Range(0f, 1f)] public float steerAssist = 1f;
        [Tooltip("Usable lateral grip in g before downforce; sets the speed-sensitive steering limit.")]
        public float lateralGripG = 1.6f;
        [Tooltip("Extra steer angle allowed on top of the geometric angle (front tyre peak slip).")]
        public float peakSlipDeg = 4f;
        [Tooltip("Share of downforce on the rear axle. Above 0.5 keeps the rear planted at speed (less high-speed oversteer).")]
        [Range(0.3f, 0.8f)] public float aeroRearBias = 0.6f;
        [Tooltip("Yaw stability: damps rotation beyond what the steering asks for. 0 = off.")]
        [Range(0f, 1f)] public float stabilityControl = 1f;
        [Tooltip("Cuts drive torque while the rear wheels spin. 0 = off.")]
        [Range(0f, 1f)] public float tractionControl = 0.7f;

        [Header("Keyboard Input Shaping (player only)")]
        public float steerInRateLowSpeed = 3f;   // steer units per second
        public float steerInRateHighSpeed = 1.2f;
        public float steerReturnRate = 5f;
        public float throttleRiseRate = 4f;

        [Header("Track Reference")]
        public F1TrackGenerator trackReference;

        [Tooltip("When true, keyboard input is ignored and Command is written by another driver (e.g. AI).")]
        public bool ExternalControl;

        /// <summary>Driver intent applied every physics tick; set this from an AI driver when ExternalControl is on.</summary>
        public VehicleCommand Command
        {
            get => _command;
            set => _command = value.Sanitized();
        }

        // Runtime telemetry
        public float CurrentSpeedKmh { get; private set; }
        public int CurrentGear { get; private set; } = 1;
        public float CurrentRpm { get; private set; } = 4000f;
        public bool IsDrsActive { get; private set; }
        public bool IsBraking { get; private set; }

        private Rigidbody _rb;
        private VehicleCommand _command;
        private float _currentSteerAngle;
        private float _wheelbase = 3f;
        private float _keySteer, _keyThrottle; // shaped keyboard input
        private Material _brakeLightMat;

        // Procedural engine synth state (used when engineAudioSource has no AudioClip)
        private bool _useProceduralSynth;
        private float _synthPhase;
        private float _synthFreq = 90f;
        private int _sampleRate = 44100;

        private static readonly float[] GearMaxSpeedsKmh = { 75f, 120f, 165f, 205f, 245f, 280f, 315f, 360f };

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.mass = 920f; // Planted race weight + aero ballast (kg)
            _rb.centerOfMass = new Vector3(0f, -0.45f, -0.18f);
            _rb.angularDamping = 2.2f;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            ConfigureWheelColliderPhysics(frontLeftCollider, isFront: true);
            ConfigureWheelColliderPhysics(frontRightCollider, isFront: true);
            ConfigureWheelColliderPhysics(rearLeftCollider, isFront: false);
            ConfigureWheelColliderPhysics(rearRightCollider, isFront: false);
            if (frontLeftCollider != null && rearLeftCollider != null)
                _wheelbase = Mathf.Max(1f, Vector3.Distance(frontLeftCollider.transform.position, rearLeftCollider.transform.position));

            // Fix up FX already baked into the scene (smoke had no material, skids were thick camera-facing ribbons).
            if (skidTrails != null) foreach (var t in skidTrails) ConfigureSkidTrail(t);
            if (tireSmokeParticles != null) foreach (var ps in tireSmokeParticles) ConfigureTireSmoke(ps);

            if (brakeLightRenderer != null)
            {
                _brakeLightMat = brakeLightRenderer.material;
                _brakeLightMat.EnableKeyword("_EMISSION");
                _brakeLightMat.SetColor("_EmissionColor", Color.black);
            }

            _sampleRate = AudioSettings.outputSampleRate;
            if (engineAudioSource != null && engineAudioSource.clip == null)
            {
                _useProceduralSynth = true;
                engineAudioSource.loop = true;
                engineAudioSource.playOnAwake = true;
                engineAudioSource.volume = 0.16f;
                engineAudioSource.Play();
            }
        }

        private void Update()
        {
            ReadInputs();
            UpdateGearAndRpm();
            UpdateVisuals();
            UpdateEffectsAndAudio();
        }

        private void FixedUpdate()
        {
            CurrentSpeedKmh = _rb.linearVelocity.magnitude * 3.6f;

            ApplyDownforce();
            ApplySteering();
            ApplyDriveAndBraking();
            ApplyStabilityControl();
        }

        private void ReadInputs()
        {
            if (ExternalControl) return;
            bool p1 = controlMode == ControlMode.Player1_WASD;
            float steerKeys = (Input.GetKey(p1 ? KeyCode.D : KeyCode.RightArrow) ? 1f : 0f)
                            - (Input.GetKey(p1 ? KeyCode.A : KeyCode.LeftArrow) ? 1f : 0f);
            float throttleKey = Input.GetKey(p1 ? KeyCode.W : KeyCode.UpArrow) ? 1f : 0f;

            // Digital keys -> smooth analog-like input; AI commands bypass this.
            float speedFactor = Mathf.Clamp01(CurrentSpeedKmh / topSpeedKmh);
            float steerInRate = Mathf.Lerp(steerInRateLowSpeed, steerInRateHighSpeed, speedFactor);
            _keySteer = ShapeSteerInput(_keySteer, steerKeys, steerInRate, steerReturnRate, Time.deltaTime);
            _keyThrottle = throttleKey > _keyThrottle
                ? Mathf.MoveTowards(_keyThrottle, throttleKey, throttleRiseRate * Time.deltaTime)
                : throttleKey; // lifting off is instant

            _command = new VehicleCommand
            {
                Throttle = _keyThrottle,
                Brake = Input.GetKey(p1 ? KeyCode.S : KeyCode.DownArrow) ? 1f : 0f,
                Steer = _keySteer,
                Handbrake = Input.GetKey(p1 ? KeyCode.Space : KeyCode.RightControl),
                Drs = Input.GetKey(p1 ? KeyCode.LeftShift : KeyCode.RightShift),
            };
            if (Input.GetKeyDown(p1 ? KeyCode.R : KeyCode.Backspace)) ResetToTrack();
        }

        private void ApplyDownforce()
        {
            float speed = _rb.linearVelocity.magnitude;
            // Split downforce between the axles (rear-biased) instead of pushing at the centre of mass.
            float downforce = downforceCoefficient * speed * speed;
            Vector3 down = -transform.up * downforce;
            Vector3 front = AxleCentre(frontLeftCollider, frontRightCollider);
            Vector3 rear = AxleCentre(rearLeftCollider, rearRightCollider);
            _rb.AddForceAtPosition(down * (1f - aeroRearBias), front, ForceMode.Force);
            _rb.AddForceAtPosition(down * aeroRearBias, rear, ForceMode.Force);

            IsDrsActive = _command.Drs && CurrentSpeedKmh >= drsMinSpeedKmh && _command.Throttle > 0.1f;
            _rb.linearDamping = IsDrsActive ? drsLinearDamping : baseLinearDamping;
        }

        private void ApplySteering()
        {
            // Speed-sensitive lock: at speed, only as much angle as the front tyres can turn into grip.
            float speed = _rb.linearVelocity.magnitude;
            float latAccel = lateralGripG * Physics.gravity.magnitude
                           * (1f + downforceCoefficient * speed * speed / (_rb.mass * Physics.gravity.magnitude));
            float gripLimit = GripSteerLimitDeg(speed, _wheelbase, latAccel, peakSlipDeg, maxSteerAngleHighSpeed, maxSteerAngleLowSpeed);
            float maxAngle = Mathf.Lerp(maxSteerAngleLowSpeed, gripLimit, steerAssist);

            // Steering rack slew.
            float t = 1f - Mathf.Exp(-steerSpeed * Time.fixedDeltaTime);
            _currentSteerAngle = Mathf.Lerp(_currentSteerAngle, _command.Steer * maxAngle, t);

            if (frontLeftCollider != null) frontLeftCollider.steerAngle = _currentSteerAngle;
            if (frontRightCollider != null) frontRightCollider.steerAngle = _currentSteerAngle;
        }

        /// <summary>
        /// Mild yaw stability: only resists rotation beyond what the steering asks for, so the car
        /// still turns fully and small slides stay possible (replaces the old hard yaw clamp).
        /// </summary>
        private void ApplyStabilityControl()
        {
            if (stabilityControl <= 0f || _command.Handbrake || CurrentSpeedKmh < 20f) return;
            if ((rearLeftCollider == null || !rearLeftCollider.isGrounded) &&
                (rearRightCollider == null || !rearRightCollider.isGrounded)) return;

            float forwardSpeed = Vector3.Dot(_rb.linearVelocity, transform.forward);
            float targetYaw = TargetYawRate(forwardSpeed, _currentSteerAngle, _wheelbase);
            float actualYaw = transform.InverseTransformDirection(_rb.angularVelocity).y;

            // Free over-rotation (rad/s): playful at low speed, tight at high speed where a slide is hard to catch.
            float allowedExcess = Mathf.Lerp(0.15f, 0.04f, Mathf.Clamp01(CurrentSpeedKmh / topSpeedKmh));
            float excess = Mathf.Sign(actualYaw) == Mathf.Sign(targetYaw) || Mathf.Abs(targetYaw) < 0.01f
                ? Mathf.Abs(actualYaw) - Mathf.Abs(targetYaw) - allowedExcess
                : Mathf.Abs(actualYaw) - allowedExcess; // rotating against the steering = spinning
            if (excess <= 0f) return;

            float correction = Mathf.Min(excess * 12f, 10f) * stabilityControl; // rad/s^2, capped
            _rb.AddRelativeTorque(Vector3.up * (-Mathf.Sign(actualYaw) * correction), ForceMode.Acceleration);
        }

        private void ApplyDriveAndBraking()
        {
            float forwardSpeed = Vector3.Dot(_rb.linearVelocity, transform.forward);
            float effectiveTopSpeed = IsDrsActive ? drsTopSpeedKmh : topSpeedKmh;

            bool handbrake = _command.Handbrake;
            bool wantsReverse = _command.Brake > 0.05f && forwardSpeed < 2f;
            bool wantsBrake = (_command.Brake > 0.05f && forwardSpeed >= 2f) || handbrake;
            IsBraking = wantsBrake;

            float appliedMotor = 0f;
            float appliedBrake = 0f;

            if (handbrake)
            {
                appliedBrake = handbrakeTorque;
            }
            else if (wantsBrake)
            {
                appliedBrake = brakeTorque * _command.Brake;
            }
            else if (_command.Throttle > 0.05f && CurrentSpeedKmh < effectiveTopSpeed)
            {
                float torqueBoost = IsDrsActive ? drsMotorBoostMultiplier : 1f;
                appliedMotor = _command.Throttle * motorTorque * torqueBoost;

                // Traction control: back off torque while the driven wheels spin up.
                float spin = Mathf.Max(ForwardSlip(rearLeftCollider), ForwardSlip(rearRightCollider));
                float cut = Mathf.Clamp01((spin - 0.3f) * 3f);
                appliedMotor *= 1f - cut * tractionControl;
            }
            else if (wantsReverse && CurrentSpeedKmh < reverseTopSpeedKmh)
            {
                appliedMotor = -_command.Brake * motorTorque * 0.55f;
            }

            // Rear-wheel drive (F1 spec)
            if (rearLeftCollider != null) rearLeftCollider.motorTorque = appliedMotor;
            if (rearRightCollider != null) rearRightCollider.motorTorque = appliedMotor;

            // Front-biased braking
            if (frontLeftCollider != null) frontLeftCollider.brakeTorque = appliedBrake * 1.15f;
            if (frontRightCollider != null) frontRightCollider.brakeTorque = appliedBrake * 1.15f;
            if (rearLeftCollider != null) rearLeftCollider.brakeTorque = handbrake ? handbrakeTorque : appliedBrake * 0.85f;
            if (rearRightCollider != null) rearRightCollider.brakeTorque = handbrake ? handbrakeTorque : appliedBrake * 0.85f;
        }

        private void UpdateGearAndRpm()
        {
            int gear = 1;
            for (int i = 0; i < GearMaxSpeedsKmh.Length; i++)
            {
                if (CurrentSpeedKmh > GearMaxSpeedsKmh[i] * 0.92f && i < GearMaxSpeedsKmh.Length - 1)
                    gear = i + 2;
                else
                    break;
            }
            CurrentGear = gear;

            float prevGearMax = gear == 1 ? 0f : GearMaxSpeedsKmh[gear - 2] * 0.85f;
            float currGearMax = GearMaxSpeedsKmh[gear - 1];
            float gearRatio = Mathf.Clamp01((CurrentSpeedKmh - prevGearMax) / Mathf.Max(1f, currGearMax - prevGearMax));

            float targetRpm = Mathf.Lerp(4200f, 13200f, gearRatio);
            if (_command.Throttle > 0.1f) targetRpm += 650f;
            CurrentRpm = Mathf.Lerp(CurrentRpm, Mathf.Clamp(targetRpm, 3800f, 13800f), Time.deltaTime * 12f);
        }

        private void UpdateVisuals()
        {
            SyncWheelPose(frontLeftCollider, frontLeftMesh);
            SyncWheelPose(frontRightCollider, frontRightMesh);
            SyncWheelPose(rearLeftCollider, rearLeftMesh);
            SyncWheelPose(rearRightCollider, rearRightMesh);

            if (steeringWheelMesh != null)
            {
                float wheelAngle = -_command.Steer * maxSteeringWheelRotation;
                steeringWheelMesh.localRotation = Quaternion.Euler(0f, 0f, wheelAngle);
            }

            if (driverHelmetMesh != null)
            {
                float headLean = _command.Steer * 10f;
                driverHelmetMesh.localRotation = Quaternion.Lerp(
                    driverHelmetMesh.localRotation,
                    Quaternion.Euler(0f, headLean * 0.5f, -headLean),
                    Time.deltaTime * 8f);
            }

            if (drsRearWingFlap != null)
            {
                float targetX = IsDrsActive ? drsOpenAngleX : drsClosedAngleX;
                Quaternion targetRot = Quaternion.Euler(targetX, 0f, 0f);
                drsRearWingFlap.localRotation = Quaternion.Lerp(drsRearWingFlap.localRotation, targetRot, Time.deltaTime * 14f);
            }

            if (_brakeLightMat != null)
            {
                _brakeLightMat.SetColor("_EmissionColor", IsBraking ? brakeLightEmissiveColor : Color.black);
            }
        }

        private static void SyncWheelPose(WheelCollider col, Transform mesh)
        {
            if (col == null || mesh == null) return;
            col.GetWorldPose(out Vector3 pos, out Quaternion rot);
            mesh.SetPositionAndRotation(pos, rot);
        }

        private void UpdateEffectsAndAudio()
        {
            // 1 = at the slip threshold; marks only when the tyres really slide or lock.
            float slip = Mathf.Max(SlipRatio(rearLeftCollider), SlipRatio(rearRightCollider));
            bool emitSkid = CurrentSpeedKmh > 8f && (slip > 1f || _command.Handbrake);

            if (skidTrails != null)
            {
                foreach (var trail in skidTrails)
                {
                    if (trail == null) continue;
                    // Pin the mark to the tyre contact patch, just above the road surface.
                    var wc = trail.GetComponentInParent<WheelCollider>();
                    WheelHit hit = default;
                    bool grounded = wc != null && wc.GetGroundHit(out hit);
                    if (grounded) trail.transform.position = hit.point + hit.normal * 0.02f;
                    trail.emitting = emitSkid && grounded;
                }
            }

            if (tireSmokeParticles != null)
            {
                foreach (var ps in tireSmokeParticles)
                {
                    if (ps == null) continue;
                    var emission = ps.emission;
                    emission.rateOverTimeMultiplier = Mathf.Lerp(12f, 45f, Mathf.Clamp01(slip - 1f));
                    if (emitSkid && !ps.isPlaying) ps.Play();
                    else if (!emitSkid && ps.isPlaying) ps.Stop();
                }
            }

            float rpmNorm = Mathf.InverseLerp(3800f, 13800f, CurrentRpm);
            if (engineAudioSource != null)
            {
                if (_useProceduralSynth)
                {
                    _synthFreq = Mathf.Lerp(75f, 295f, rpmNorm);
                }
                else
                {
                    engineAudioSource.pitch = Mathf.Lerp(minEnginePitch, maxEnginePitch, rpmNorm);
                }
            }
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (!_useProceduralSynth) return;

            float increment = _synthFreq * 2f * Mathf.PI / _sampleRate;
            for (int i = 0; i < data.Length; i += channels)
            {
                _synthPhase += increment;
                if (_synthPhase > 2f * Mathf.PI) _synthPhase -= 2f * Mathf.PI;

                // Harmonic-rich F1 V6-style tone (fundamental + 2nd + 3rd harmonic)
                float sample = (Mathf.Sin(_synthPhase) * 0.55f +
                                Mathf.Sin(_synthPhase * 2f) * 0.30f +
                                Mathf.Sin(_synthPhase * 3f) * 0.15f) * 0.28f;

                for (int c = 0; c < channels; c++)
                    data[i + c] = sample;
            }
        }

        /// <summary>
        /// Resets the vehicle upright onto the nearest track waypoint.
        /// </summary>
        public void ResetToTrack()
        {
            if (trackReference == null) return;
            CatmullRomSpline spline = trackReference.Spline;
            if (spline == null || spline.Samples.Count == 0) return;

            var sample = spline.Samples[spline.FindNearestSampleIndex(transform.position)];
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            transform.SetPositionAndRotation(sample.Position + Vector3.up * 0.8f, Quaternion.LookRotation(sample.Tangent, Vector3.up));
        }

        /// <summary>
        /// Applies tuned F1 suspension and high-grip friction curves to a WheelCollider.
        /// </summary>
        public static void ConfigureWheelColliderPhysics(WheelCollider wc, bool isFront)
        {
            if (wc == null) return;

            wc.mass = 22f;
            wc.radius = 0.36f;
            wc.wheelDampingRate = 0.25f;
            wc.suspensionDistance = 0.14f;
            wc.forceAppPointDistance = 0.05f;

            wc.suspensionSpring = new JointSpring
            {
                spring = isFront ? 65000f : 72000f,
                damper = isFront ? 6200f : 6800f,
                targetPosition = 0.45f
            };

            wc.forwardFriction = new WheelFrictionCurve
            {
                extremumSlip = 0.4f,
                extremumValue = 1.8f,
                asymptoteSlip = 0.9f,
                asymptoteValue = 1.4f,
                stiffness = 1.6f
            };

            // Rear ~20% grippier than front: stable at speed without the old ~40% push-wide understeer.
            // Gentle drop after the peak so a slide builds progressively instead of snapping.
            // (WheelColliders ignore the road's PhysicsMaterial; "track grip" is set here.)
            wc.sidewaysFriction = new WheelFrictionCurve
            {
                extremumSlip = 0.35f,
                extremumValue = isFront ? 2.0f : 2.2f,
                asymptoteSlip = 0.8f,
                asymptoteValue = isFront ? 1.75f : 1.95f,
                stiffness = isFront ? 1.9f : 2.1f
            };
        }

        // ---------------- Pure handling helpers (checked in Editor/SeamSelfCheck) ----------------

        /// <summary>Moves keyboard steer toward the key target; returns to centre faster, flips through 0 instantly.</summary>
        public static float ShapeSteerInput(float current, float target, float inRate, float returnRate, float dt)
        {
            if (current * target < 0f) current = 0f; // reversing direction: drop straight to centre
            float rate = Mathf.Abs(target) < Mathf.Abs(current) ? returnRate : inRate;
            return Mathf.MoveTowards(current, target, rate * dt);
        }

        /// <summary>
        /// Largest useful steer angle (deg) at <paramref name="speed"/> m/s: the kinematic angle for a turn
        /// at <paramref name="latAccel"/> m/s^2 plus the tyre's peak slip, clamped to [minDeg, maxDeg].
        /// </summary>
        public static float GripSteerLimitDeg(float speed, float wheelbase, float latAccel, float peakSlipDeg, float minDeg, float maxDeg)
        {
            if (speed < 1f) return maxDeg;
            float geometric = Mathf.Atan(wheelbase * latAccel / (speed * speed)) * Mathf.Rad2Deg;
            return Mathf.Clamp(geometric + peakSlipDeg, minDeg, maxDeg);
        }

        /// <summary>Yaw rate (rad/s) a car following its steered wheels would have.</summary>
        public static float TargetYawRate(float forwardSpeed, float steerDeg, float wheelbase) =>
            forwardSpeed * Mathf.Tan(steerDeg * Mathf.Deg2Rad) / Mathf.Max(0.1f, wheelbase);

        private static float SlipRatio(WheelCollider wc) =>
            wc != null && wc.GetGroundHit(out WheelHit h)
                ? Mathf.Max(Mathf.Abs(h.sidewaysSlip) / 0.35f, Mathf.Abs(h.forwardSlip) / 0.45f)
                : 0f;

        private Vector3 AxleCentre(WheelCollider l, WheelCollider r) =>
            l != null && r != null ? (l.transform.position + r.transform.position) * 0.5f : _rb.worldCenterOfMass;

        private static float ForwardSlip(WheelCollider wc) =>
            wc != null && wc.GetGroundHit(out WheelHit h) ? h.forwardSlip : 0f;

        // ---------------- Tyre FX setup (also used by Editor/Builders/F1CarBuilder) ----------------

        private static Material _skidMat;

        /// <summary>Thin, flat, semi-transparent tyre mark lying on the road.</summary>
        public static void ConfigureSkidTrail(TrailRenderer trail)
        {
            if (trail == null) return;
            trail.alignment = LineAlignment.TransformZ;
            trail.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // local Z points down => ribbon lies flat
            trail.widthMultiplier = 1f;
            trail.startWidth = trail.endWidth = 0.17f;
            trail.time = 3f;
            trail.minVertexDistance = 0.1f;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;

            if (_skidMat == null)
            {
                Shader sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
                _skidMat = new Material(sh) { name = "Skid_Mat" };
                _skidMat.SetColor("_BaseColor", new Color(0.04f, 0.04f, 0.04f, 0.5f));
                _skidMat.SetFloat("_Surface", 1f); // transparent
                _skidMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _skidMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                _skidMat.SetFloat("_ZWrite", 0f);
                _skidMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                _skidMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            trail.sharedMaterial = _skidMat;
            trail.emitting = false;
        }

        /// <summary>Soft grey smoke puffs that grow and fade.</summary>
        public static void ConfigureTireSmoke(ParticleSystem ps)
        {
            if (ps == null) return;
            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = BoxExplosionFx.SmokeMaterial;

            var main = ps.main;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startColor = new Color(0.82f, 0.82f, 0.84f, 0.5f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.2f));

            var color = ps.colorOverLifetime;
            color.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.6f, 0f), new GradientAlphaKey(0f, 1f) });
            color.color = fade;
        }
    }
}
