using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;
#endif

namespace F1Racing.Vehicle
{
    /// <summary>
    /// Keyboard reads for the whole game through the Input System package (the Input Manager is deprecated). Takes a
    /// KeyCode so serialized key fields in scenes keep their values. Falls back to the Input Manager only if the project
    /// is switched back to it (Player Settings > Active Input Handling).
    /// </summary>
    public static class GameInput
    {
        public static bool Held(KeyCode key)
        {
#if ENABLE_INPUT_SYSTEM
            Key k = ToKey(key);
            return Keyboard.current != null && k != Key.None && Keyboard.current[k].isPressed;
#else
            return Input.GetKey(key);
#endif
        }

        public static bool Down(KeyCode key)
        {
#if ENABLE_INPUT_SYSTEM
            Key k = ToKey(key);
            return Keyboard.current != null && k != Key.None && Keyboard.current[k].wasPressedThisFrame;
#else
            return Input.GetKeyDown(key);
#endif
        }

#if ENABLE_INPUT_SYSTEM
        private static readonly Dictionary<KeyCode, Key> Map = new Dictionary<KeyCode, Key>();

        /// <summary>Most names match; these are the ones the two enums spell differently. Unknown keys map to None.</summary>
        public static Key ToKey(KeyCode code)
        {
            if (Map.TryGetValue(code, out Key key)) return key;
            string name = code.ToString();
            if (name.StartsWith("Alpha")) name = "Digit" + name.Substring(5);
            else if (name.StartsWith("Keypad")) name = "Numpad" + name.Substring(6);
            else if (name == "Return") name = "Enter";
            else if (name == "LeftControl") name = "LeftCtrl";
            else if (name == "RightControl") name = "RightCtrl";
            if (!Enum.TryParse(name, out key)) key = Key.None;
            Map[code] = key;
            return key;
        }
#endif
    }
}
