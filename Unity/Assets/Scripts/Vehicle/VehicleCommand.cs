using System;

namespace F1Racing.Vehicle
{
    /// <summary>
    /// One tick of driver intent, from the player or an AI driver (spec A4 VehicleCommand).
    /// Brake held at low speed means reverse, as with the keyboard.
    /// </summary>
    public struct VehicleCommand
    {
        public float Steer;    // -1 (left) .. 1 (right)
        public float Throttle; //  0 .. 1
        public float Brake;    //  0 .. 1
        public bool Handbrake;
        public bool Drs;

        /// <summary>Clamps ranges and replaces non-finite values with 0.</summary>
        public VehicleCommand Sanitized() => new VehicleCommand
        {
            Steer = Clean(Steer, -1f),
            Throttle = Clean(Throttle, 0f),
            Brake = Clean(Brake, 0f),
            Handbrake = Handbrake,
            Drs = Drs,
        };

        private static float Clean(float v, float min) => float.IsFinite(v) ? Math.Clamp(v, min, 1f) : 0f;
    }
}
