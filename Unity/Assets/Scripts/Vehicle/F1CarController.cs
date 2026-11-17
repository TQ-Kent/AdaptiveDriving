using System.Collections.Generic;
using F1Racing.Track;
using UnityEngine;

namespace F1Racing.Vehicle
{
    public enum ControlMode
    {
        Player1_WASD,
        Player2_Arrows,
        AI_Waypoint
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
        [Tooltip("Allow pressing T at runtime to toggle Car 2 between Player 2 Arrows and AI.")]
        public bool allowRuntimeAIToggle = false;

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
        public float motorTorque = 2800f;
        public float drsMotorBoostMultiplier = 1.18f;
        public float brakeTorque = 5200f;
        public float handbrakeTorque = 6500f;
        public float topSpeedKmh = 325f;
        public float drsTopSpeedKmh = 355f;
        public float reverseTopSpeedKmh = 75f;

        [Header("Steering & Downforce")]
        public float maxSteerAngleLowSpeed = 28f;
        public float maxSteerAngleHighSpeed = 8.5f;
        public float steerSpeed = 10f;
        public float downforceCoefficient = 2.6f;
        public float baseLinearDamping = 0.04f;
        public float drsLinearDamping = 0.015f;
        public float drsMinSpeedKmh = 120f;

        [Header("AI Settings")]
        public F1TrackGenerator trackReference;
        public float aiLookaheadDistance = 22f;
        public float aiCornerBrakingSensitivity = 1.6f;

        // Runtime telemetry
        public float CurrentSpeedKmh { get; private set; }
        public int CurrentGear { get; private set; } = 1;
        public float CurrentRpm { get; private set; } = 4000f;
        public bool IsDrsActive { get; private set; }
        public bool IsBraking { get; private set; }

        private Rigidbody _rb;
        private float _throttleInput;
        private float _steerInput;
        private float _currentSteerAngle;
        private bool _handbrakeInput;
        private bool _drsInput;
        private Material _brakeLightMat;
        private int _aiWaypointIndex;

        // Procedural engine synth state (used when engineAudioSource has no AudioClip)
        private bool _useProceduralSynth;
        private float _synthPhase;
        private float _synthFreq = 90f;
        private int _sampleRate = 44100;

        private static readonly float[] GearMaxSpeedsKmh = { 75f, 120f, 165f, 205f, 245f, 280f, 315f, 360f };

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.mass = 798f; // Official F1 minimum weight (kg)
            _rb.centerOfMass = new Vector3(0f, -0.35f, -0.15f);
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
            if (allowRuntimeAIToggle && Input.GetKeyDown(KeyCode.T))
            {
                controlMode = (controlMode == ControlMode.AI_Waypoint)
                    ? ControlMode.Player2_Arrows
                    : ControlMode.AI_Waypoint;
            }

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
        }

        private void ReadInputs()
        {
            switch (controlMode)
            {
                case ControlMode.Player1_WASD:
                    _throttleInput = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
                    _steerInput = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
                    _handbrakeInput = Input.GetKey(KeyCode.Space);
                    _drsInput = Input.GetKey(KeyCode.LeftShift);
                    if (Input.GetKeyDown(KeyCode.R)) ResetToTrack();
                    break;

                case ControlMode.Player2_Arrows:
                    _throttleInput = (Input.GetKey(KeyCode.UpArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);
                    _steerInput = (Input.GetKey(KeyCode.RightArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
                    _handbrakeInput = Input.GetKey(KeyCode.RightControl);
                    _drsInput = Input.GetKey(KeyCode.RightShift);
                    if (Input.GetKeyDown(KeyCode.Backspace)) ResetToTrack();
                    break;

                case ControlMode.AI_Waypoint:
                    ComputeAIInputs();
                    break;
            }
        }

        private void ComputeAIInputs()
        {
            if (trackReference == null || trackReference.Waypoints.Count == 0)
            {
                _throttleInput = 0f;
                _steerInput = 0f;
                _handbrakeInput = false;
                _drsInput = false;
                return;
            }

            IReadOnlyList<Vector3> waypoints = trackReference.Waypoints;
            int count = waypoints.Count;

            // Advance waypoint index to closest point ahead
            float closestDist = float.MaxValue;
            int bestIdx = _aiWaypointIndex;
            for (int offset = -5; offset <= 25; offset++)
            {
                int idx = ( _aiWaypointIndex + offset + count ) % count;
                float d = (waypoints[idx] - transform.position).sqrMagnitude;
                if (d < closestDist)
                {
                    closestDist = d;
                    bestIdx = idx;
                }
            }
            _aiWaypointIndex = bestIdx;

            // Find lookahead target
            Vector3 targetPos = waypoints[_aiWaypointIndex];
            float accumulated = 0f;
            int scan = _aiWaypointIndex;
            while (accumulated < aiLookaheadDistance)
            {
                int next = (scan + 1) % count;
                accumulated += Vector3.Distance(waypoints[scan], waypoints[next]);
                scan = next;
                targetPos = waypoints[scan];
            }

            Vector3 localTarget = transform.InverseTransformPoint(targetPos);
            float steerAngle = Mathf.Atan2(localTarget.x, Mathf.Max(1f, localTarget.z)) * Mathf.Rad2Deg;
            _steerInput = Mathf.Clamp(steerAngle / 24f, -1f, 1f);

            // Look further ahead for cornering speed control
            int farIdx = (_aiWaypointIndex + 22) % count;
            Vector3 toFar = (waypoints[farIdx] - transform.position).normalized;
            float cornerAngle = Vector3.Angle(transform.forward, toFar);
            float targetSpeedKmh = Mathf.Clamp(310f - cornerAngle * aiCornerBrakingSensitivity * 3.5f, 85f, 320f);

            if (CurrentSpeedKmh > targetSpeedKmh + 12f)
                _throttleInput = -0.8f;
            else if (CurrentSpeedKmh < targetSpeedKmh)
                _throttleInput = 1f;
            else
                _throttleInput = 0.25f;

            _handbrakeInput = false;
            _drsInput = (cornerAngle < 8f && CurrentSpeedKmh > drsMinSpeedKmh);
        }

        private void ApplyDownforce()
        {
            float speed = _rb.linearVelocity.magnitude;
            float downforce = downforceCoefficient * speed * speed;
            _rb.AddForce(-transform.up * downforce, ForceMode.Force);

            IsDrsActive = _drsInput && CurrentSpeedKmh >= drsMinSpeedKmh && _throttleInput > 0.1f;
            _rb.linearDamping = IsDrsActive ? drsLinearDamping : baseLinearDamping;
        }

        private void ApplySteering()
        {
            float speedFactor = Mathf.Clamp01(CurrentSpeedKmh / topSpeedKmh);
            float maxAngle = Mathf.Lerp(maxSteerAngleLowSpeed, maxSteerAngleHighSpeed, speedFactor);
            float targetAngle = _steerInput * maxAngle;

            _currentSteerAngle = Mathf.Lerp(_currentSteerAngle, targetAngle, Time.fixedDeltaTime * steerSpeed);

            if (frontLeftCollider != null) frontLeftCollider.steerAngle = _currentSteerAngle;
            if (frontRightCollider != null) frontRightCollider.steerAngle = _currentSteerAngle;
        }

        private void ApplyDriveAndBraking()
        {
            float forwardSpeed = Vector3.Dot(_rb.linearVelocity, transform.forward);
            float effectiveTopSpeed = IsDrsActive ? drsTopSpeedKmh : topSpeedKmh;

            bool wantsReverse = _throttleInput < -0.05f && forwardSpeed < 2f;
            bool wantsBrake = (_throttleInput < -0.05f && forwardSpeed >= 2f) || _handbrakeInput;
            IsBraking = wantsBrake;

            float appliedMotor = 0f;
            float appliedBrake = 0f;

            if (_handbrakeInput)
            {
                appliedBrake = handbrakeTorque;
            }
            else if (wantsBrake)
            {
                appliedBrake = brakeTorque * Mathf.Abs(_throttleInput);
            }
            else if (_throttleInput > 0.05f && CurrentSpeedKmh < effectiveTopSpeed)
            {
                float torqueBoost = IsDrsActive ? drsMotorBoostMultiplier : 1f;
                appliedMotor = _throttleInput * motorTorque * torqueBoost;
            }
            else if (wantsReverse && CurrentSpeedKmh < reverseTopSpeedKmh)
            {
                appliedMotor = _throttleInput * motorTorque * 0.55f;
            }

            // Rear-wheel drive (F1 spec)
            if (rearLeftCollider != null) rearLeftCollider.motorTorque = appliedMotor;
            if (rearRightCollider != null) rearRightCollider.motorTorque = appliedMotor;

            // Front-biased braking
            if (frontLeftCollider != null) frontLeftCollider.brakeTorque = appliedBrake * 1.15f;
            if (frontRightCollider != null) frontRightCollider.brakeTorque = appliedBrake * 1.15f;
            if (rearLeftCollider != null) rearLeftCollider.brakeTorque = _handbrakeInput ? handbrakeTorque : appliedBrake * 0.85f;
            if (rearRightCollider != null) rearRightCollider.brakeTorque = _handbrakeInput ? handbrakeTorque : appliedBrake * 0.85f;
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
            if (_throttleInput > 0.1f) targetRpm += 650f;
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
                float wheelAngle = -_steerInput * maxSteeringWheelRotation;
                steeringWheelMesh.localRotation = Quaternion.Euler(0f, 0f, wheelAngle);
            }

            if (driverHelmetMesh != null)
            {
                float headLean = _steerInput * 10f;
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

            bool emitSkid = _handbrakeInput || (IsBraking && CurrentSpeedKmh > 65f) || isSlipping;

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
            if (trackReference == null || trackReference.Waypoints.Count == 0) return;

            IReadOnlyList<Vector3> waypoints = trackReference.Waypoints;
            float bestDist = float.MaxValue;
            int bestIdx = 0;
            for (int i = 0; i < waypoints.Count; i++)
            {
                float d = (waypoints[i] - transform.position).sqrMagnitude;
                if (d < bestDist)
                {
                    bestDist = d;
                    bestIdx = i;
                }
            }

            int nextIdx = (bestIdx + 1) % waypoints.Count;
            Vector3 forward = (waypoints[nextIdx] - waypoints[bestIdx]).normalized;

            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            transform.SetPositionAndRotation(waypoints[bestIdx] + Vector3.up * 0.8f, Quaternion.LookRotation(forward, Vector3.up));
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
                spring = isFront ? 58000f : 64000f,
                damper = isFront ? 5200f : 5800f,
                targetPosition = 0.45f
            };

            wc.forwardFriction = new WheelFrictionCurve
            {
                extremumSlip = 0.35f,
                extremumValue = 1.65f,
                asymptoteSlip = 0.85f,
                asymptoteValue = 1.15f,
                stiffness = 1.45f
            };

            wc.sidewaysFriction = new WheelFrictionCurve
            {
                extremumSlip = 0.22f,
                extremumValue = isFront ? 1.85f : 1.95f,
                asymptoteSlip = 0.65f,
                asymptoteValue = 1.25f,
                stiffness = isFront ? 1.55f : 1.65f
            };
        }
    }
}
