using System;

namespace Racing.Contracts
{
    /// <summary>
    /// Driver output consumed by VehicleMotor (spec A4).
    /// Steering in [-1, 1], throttle and brake in [0, 1]; non-finite inputs become 0, ranges are clamped.
    /// </summary>
    public readonly struct VehicleCommand
    {
        public readonly float Steering;
        public readonly float Throttle;
        public readonly float Brake;

        public VehicleCommand(float steering, float throttle, float brake)
        {
            Steering = Clamp(steering, -1f, 1f);
            Throttle = Clamp(throttle, 0f, 1f);
            Brake = Clamp(brake, 0f, 1f);
        }

        public static readonly VehicleCommand Idle = new VehicleCommand(0f, 0f, 0f);

        static float Clamp(float value, float min, float max)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return Math.Min(max, Math.Max(min, value));
        }
    }
}
