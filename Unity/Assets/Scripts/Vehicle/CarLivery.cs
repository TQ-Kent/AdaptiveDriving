using UnityEngine;

namespace F1Racing.Vehicle
{
    /// <summary>Car paint colour, read from and written to the body material (named "*_Body_Mat" by the car builder). Visual only.</summary>
    public static class CarLivery
    {
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        /// <summary>Gives the car its own body material in <paramref name="color"/>, so clones stop sharing the template's paint.</summary>
        public static void Apply(F1CarController car, Color color)
        {
            Material own = null;
            foreach (Renderer r in car.GetComponentsInChildren<Renderer>(true))
            {
                Material shared = r.sharedMaterial;
                if (shared == null || !shared.name.EndsWith("_Body_Mat")) continue;
                if (own == null)
                {
                    own = new Material(shared) { name = car.name + "_Body_Mat" };
                    own.SetColor(BaseColor, color);
                }
                r.sharedMaterial = own;
            }
        }

        public static Color ColorOf(F1CarController car)
        {
            foreach (Renderer r in car.GetComponentsInChildren<Renderer>())
            {
                Material m = r.sharedMaterial;
                if (m != null && m.name.EndsWith("_Body_Mat") && m.HasProperty(BaseColor)) return m.GetColor(BaseColor);
            }
            return Color.white;
        }
    }
}
