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
            _command = new VehicleCommand
            {
                Throttle = Input.GetKey(p1 ? KeyCode.W : KeyCode.UpArrow) ? 1f : 0f,
                Brake = Input.GetKey(p1 ? KeyCode.S : KeyCode.DownArrow) ? 1f : 0f,
                Steer = (Input.GetKey(p1 ? KeyCode.D : KeyCode.RightArrow) ? 1f : 0f)
                      - (Input.GetKey(p1 ? KeyCode.A : KeyCode.LeftArrow) ? 1f : 0f),
                Handbrake = Input.GetKey(p1 ? KeyCode.Space : KeyCode.RightControl),
                Drs = Input.GetKey(p1 ? KeyCode.LeftShift : KeyCode.RightShift),
            };
            if (Input.GetKeyDown(p1 ? KeyCode.R : KeyCode.Backspace)) ResetToTrack();
        }

        private void ApplyDownforce()
        {
            float speed = _rb.linearVelocity.magnitude;
            float downforce = downforceCoefficient * speed * speed;
            _rb.AddForce(-transform.up * downforce, ForceMode.Force);

            IsDrsActive = _command.Drs && CurrentSpeedKmh >= drsMinSpeedKmh && _command.Throttle > 0.1f;
            _rb.linearDamping = IsDrsActive ? drsLinearDamping : baseLinearDamping;
        }

        private void ApplySteering()
        {
            float speedFactor = Mathf.Clamp01(CurrentSpeedKmh / topSpeedKmh);
            float maxAngle = Mathf.Lerp(maxSteerAngleLowSpeed, maxSteerAngleHighSpeed, Mathf.Pow(speedFactor, 0.75f));
            float targetAngle = _command.Steer * maxAngle;

            float effectiveSteerSpeed = Mathf.Lerp(steerSpeed, steerSpeed * 0.65f, speedFactor);
            _currentSteerAngle = Mathf.Lerp(_currentSteerAngle, targetAngle, Time.fixedDeltaTime * effectiveSteerSpeed);

            if (frontLeftCollider != null) frontLeftCollider.steerAngle = _currentSteerAngle;
            if (frontRightCollider != null) frontRightCollider.steerAngle = _currentSteerAngle;
        }

        private void ApplyStabilityControl()
        {
            if (_command.Handbrake || CurrentSpeedKmh < 50f) return;

            bool rearGrounded = (rearLeftCollider != null && rearLeftCollider.isGrounded) ||
                                (rearRightCollider != null && rearRightCollider.isGrounded);
            if (!rearGrounded) return;

            float speedFactor = Mathf.Clamp01(CurrentSpeedKmh / topSpeedKmh);

            // Damp sideways velocity slide so high-speed turns hold the line without scrubbing momentum
            Vector3 localVel = transform.InverseTransformDirection(_rb.linearVelocity);
            float lateralCancel = Mathf.Lerp(1.5f, 4.5f, speedFactor) * Time.fixedDeltaTime;
            localVel.x = Mathf.Lerp(localVel.x, 0f, Mathf.Clamp01(lateralCancel));
            _rb.linearVelocity = transform.TransformDirection(localVel);

            // Clamp excessive yaw angular velocity at high speed to prevent snap-spins
            Vector3 angVel = _rb.angularVelocity;
            float maxYawRate = Mathf.Lerp(2.2f, 1.05f, speedFactor);
            angVel.y = Mathf.Clamp(angVel.y, -maxYawRate, maxYawRate);
            _rb.angularVelocity = angVel;
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
            bool isSlipping = false;
            if (rearLeftCollider != null && rearLeftCollider.GetGroundHit(out WheelHit hitL))
            {
                if (Mathf.Abs(hitL.sidewaysSlip) > 0.35f || Mathf.Abs(hitL.forwardSlip) > 0.45f)
                    isSlipping = true;
            }
            if (rearRightCollider != null && rearRightCollider.GetGroundHit(out WheelHit hitR))
            {
                if (Mathf.Abs(hitR.sidewaysSlip) > 0.35f || Mathf.Abs(hitR.forwardSlip) > 0.45f)
                    isSlipping = true;
            }

            bool emitSkid = _command.Handbrake || (IsBraking && CurrentSpeedKmh > 65f) || isSlipping;

            if (skidTrails != null)
            {
                foreach (var trail in skidTrails)
                {
                    if (trail != null) trail.emitting = emitSkid;
                }
            }

            if (tireSmokeParticles != null)
            {
                foreach (var ps in tireSmokeParticles)
                {
                    if (ps == null) continue;
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
            if (trackReference.Spline == null) trackReference.RebuildSplineOnly();
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

            wc.sidewaysFriction = new WheelFrictionCurve
            {
                extremumSlip = 0.35f,
                extremumValue = isFront ? 1.95f : 2.25f,
                asymptoteSlip = 0.8f,
                asymptoteValue = isFront ? 1.65f : 1.95f,
                stiffness = isFront ? 1.75f : 2.15f
            };
        }
    }
}
