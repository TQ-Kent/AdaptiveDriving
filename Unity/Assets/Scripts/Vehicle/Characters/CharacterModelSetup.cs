using UnityEngine;

namespace F1Racing.Vehicle.Characters
{
    /// <summary>
    /// Replaces the red car's visuals with PMX character models (speed-driven run
    /// cycle + front camera on F). Press M to cycle models; the last entry is the original car.
    /// Physics, colliders and the controller are untouched.
    /// Applied by GameModeManager in Fun mode. Models live in Resources/Characters/<Name>/.
    /// </summary>
    public static class CharacterModelSetup
    {
        private const string CarName = "F1_Car_1_Red";

        /// <summary>Called by GameModeManager in Fun mode.</summary>
        public static void Apply()
        {
            GameObject car = GameObject.Find(CarName);
            if (car == null || car.GetComponent<CharacterModelSwitcher>() != null) return;

            var camGo = new GameObject("CharacterFrontCamera");
            camGo.AddComponent<Camera>();
            var front = camGo.AddComponent<CharacterFrontCamera>();
            front.carRoot = car.transform;

            var switcher = car.AddComponent<CharacterModelSwitcher>();
            switcher.frontCamera = front;
        }
    }
}
