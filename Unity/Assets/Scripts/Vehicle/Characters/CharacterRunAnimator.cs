using UnityEngine;

namespace F1Racing.Vehicle.Characters
{
    /// <summary>
    /// Procedural run cycle for an MMD-skeleton model, driven by the car's speed.
    /// Bone rotations are relative to the rest pose (model space: Y up, facing -Z).
    /// Positive X rotation swings a downward-pointing limb forward.
    /// </summary>
    public class CharacterRunAnimator : MonoBehaviour
    {
        public F1CarController car;

        [Header("Cycle")]
        public float minCadenceHz = 1.6f;    // strides per second when barely moving
        public float maxCadenceHz = 3.6f;    // strides per second at full tilt
        public float speedForMaxCadence = 60f; // m/s
        public float speedForFullRun = 3f;     // m/s at which the run fully blends in

        [Header("Amplitudes (degrees)")]
        public float thighSwing = 48f;
        public float kneeBendMin = 15f;
        public float kneeBendMax = 95f;
        public float armSwing = 45f;
        public float armLower = 48f;   // brings MMD A-pose arms down to the sides
        public float elbowBend = 80f;
        public float forwardLean = 14f;
        public float torsoTwist = 9f;
        public float bounce = 0.35f;   // model units

        private Transform _center, _upper, _lower, _head;
        private Transform[] _thighL, _thighR, _kneeL, _kneeR;
        private Transform _armL, _armR, _elbowL, _elbowR;
        private Vector3 _centerRest, _forearmDirL, _forearmDirR;
        private float _phase, _run;

        public void Bind(PmxModel m)
        {
            _center = m.Bone("センター");
            _upper = m.Bone("上半身");
            _lower = m.Bone("下半身");
            _head = m.Bone("頭");
            _thighL = Pair(m, "左足", "左足D");
            _thighR = Pair(m, "右足", "右足D");
            _kneeL = Pair(m, "左ひざ", "左ひざD");
            _kneeR = Pair(m, "右ひざ", "右ひざD");
            _armL = m.Bone("左腕");
            _armR = m.Bone("右腕");
            _elbowL = m.Bone("左ひじ");
            _elbowR = m.Bone("右ひじ");
            if (_center != null) _centerRest = _center.localPosition;

            Transform wristL = m.Bone("左手首"), wristR = m.Bone("右手首");
            // Rest directions in model space (all rest rotations are identity, so this is also the elbow's parent frame).
            Transform root = m.Root.transform;
            _forearmDirL = (_elbowL && wristL) ? root.InverseTransformDirection(wristL.position - _elbowL.position).normalized : Vector3.down;
            _forearmDirR = (_elbowR && wristR) ? root.InverseTransformDirection(wristR.position - _elbowR.position).normalized : Vector3.down;
        }

        private static Transform[] Pair(PmxModel m, string a, string b)
        {
            Transform ta = m.Bone(a), tb = m.Bone(b);
            if (ta && tb) return new[] { ta, tb };
            if (ta) return new[] { ta };
            return tb ? new[] { tb } : new Transform[0];
        }

        private void LateUpdate()
        {
            float speed = car != null ? car.CurrentSpeedKmh / 3.6f : 0f;
            float targetRun = Mathf.SmoothStep(0f, 1f, speed / speedForFullRun);
            _run = Mathf.MoveTowards(_run, targetRun, Time.deltaTime * 3f);

            float cadence = Mathf.Lerp(minCadenceHz, maxCadenceHz, Mathf.Clamp01(speed / speedForMaxCadence));
            if (_run > 0.001f) _phase = Mathf.Repeat(_phase + Time.deltaTime * cadence * Mathf.PI * 2f, Mathf.PI * 2f);

            float s = Mathf.Sin(_phase), c = Mathf.Cos(_phase);

            // Legs: left leg forward when s > 0. Knee folds most while the leg recovers forward.
            Set(_thighL, Quaternion.Euler(thighSwing * s * _run, 0f, 0f));
            Set(_thighR, Quaternion.Euler(-thighSwing * s * _run, 0f, 0f));
            Set(_kneeL, Quaternion.Euler(-Mathf.Lerp(kneeBendMin, kneeBendMax, Mathf.Max(0f, c)) * _run, 0f, 0f));
            Set(_kneeR, Quaternion.Euler(-Mathf.Lerp(kneeBendMin, kneeBendMax, Mathf.Max(0f, -c)) * _run, 0f, 0f));

            // Arms: lowered from the A-pose always, swinging opposite to the legs while running.
            if (_armL) _armL.localRotation = Quaternion.Euler(-armSwing * s * _run, 0f, 0f) * Quaternion.Euler(0f, 0f, -armLower);
            if (_armR) _armR.localRotation = Quaternion.Euler(armSwing * s * _run, 0f, 0f) * Quaternion.Euler(0f, 0f, armLower);
            if (_elbowL) _elbowL.localRotation = Bend(_forearmDirL, (elbowBend * 0.6f + elbowBend * 0.4f * Mathf.Max(0f, -s)) * _run);
            if (_elbowR) _elbowR.localRotation = Bend(_forearmDirR, (elbowBend * 0.6f + elbowBend * 0.4f * Mathf.Max(0f, s)) * _run);

            // Torso lean + counter twist, head kept looking ahead.
            if (_upper) _upper.localRotation = Quaternion.Euler(-forwardLean * _run, torsoTwist * s * _run, 0f);
            if (_lower) _lower.localRotation = Quaternion.Euler(0f, -torsoTwist * 0.6f * s * _run, 0f);
            if (_head) _head.localRotation = Quaternion.Euler(forwardLean * 0.6f * _run, -torsoTwist * s * _run, 0f);

            // Vertical bounce, twice per stride.
            if (_center) _center.localPosition = _centerRest + Vector3.up * (bounce * (Mathf.Abs(s) - 0.5f) * _run);
        }

        // Rotates a forearm (rest direction dir) toward the front (-Z) by angle degrees.
        private static Quaternion Bend(Vector3 dir, float angle)
        {
            Vector3 axis = Vector3.Cross(dir, Vector3.back);
            if (axis.sqrMagnitude < 1e-6f) axis = Vector3.right;
            return Quaternion.AngleAxis(angle, axis.normalized);
        }

        private static void Set(Transform[] ts, Quaternion q)
        {
            foreach (Transform t in ts) t.localRotation = q;
        }
    }
}
