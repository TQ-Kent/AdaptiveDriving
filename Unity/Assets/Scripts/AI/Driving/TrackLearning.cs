using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace F1Racing.AI.Driving
{
    /// <summary>
    /// The AI's learned corner speed shares (CornerMemory), kept per track between races (user choice, 2026-11-25). Driving calibration
    /// for the shared car, not difficulty: one file for all AI drivers. Local only, never uploaded. The key holds the
    /// track length, start line and car physics spec, so a changed track or car starts fresh. Reset from the menu.
    /// </summary>
    public static class TrackLearning
    {
        [Serializable]
        private class Data
        {
            public string key;
            public float[] scale; // share of the corner limit per racing-line point, 1 = none
        }

        public static string FilePath => Path.Combine(Application.persistentDataPath, "ai_track_learning.json");

        /// <summary>Bump to discard saved learning made under old rules (v4, race 25: v3 shares were learned with race 24's
        /// braking-while-turning bug; 62 points sat at the floor and recover only 1% a lap).</summary>
        private const string Version = "v4";

        public static string Key(float trackLengthM, float startLineM, string physicsSpec) =>
            $"{Version}|{trackLengthM:F1}|{startLineM:F1}|{physicsSpec}";

        /// <summary>Saved shares for <paramref name="key"/> (1 = none), or null when missing, unreadable or for another track/car.</summary>
        public static float[] Load(string key)
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                var data = JsonUtility.FromJson<Data>(File.ReadAllText(FilePath));
                if (data == null || data.key != key || data.scale == null) return null;
                var scale = new float[data.scale.Length];
                for (int i = 0; i < scale.Length; i++) scale[i] = data.scale[i] > 0f && data.scale[i] < 1f ? data.scale[i] : 1f; // bad values = none
                return scale;
            }
            catch (Exception ex) { Debug.LogWarning($"[AI] track learning not loaded: {ex.Message}"); return null; }
        }

        /// <summary>Saves the per-point median of every driver's shares (one car's freak crash does not set it). Returns lowered points.</summary>
        public static int Save(string key, IReadOnlyList<IReadOnlyList<float>> perDriverScale)
        {
            float[] median = Median(perDriverScale);
            if (median == null) return 0;
            int capped = 0;
            foreach (float s in median) if (s < 1f) capped++;
            var data = new Data { key = key, scale = median };
            try { File.WriteAllText(FilePath, JsonUtility.ToJson(data)); }
            catch (Exception ex) { Debug.LogWarning($"[AI] track learning not saved: {ex.Message}"); return 0; }
            return capped;
        }

        public static void Reset()
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); }
            catch (Exception ex) { Debug.LogWarning($"[AI] track learning not reset: {ex.Message}"); }
        }

        /// <summary>Pure core (checked in SeamSelfCheck): per-index median. Null if the lists differ in length.</summary>
        public static float[] Median(IReadOnlyList<IReadOnlyList<float>> lists)
        {
            if (lists == null || lists.Count == 0) return null;
            int n = lists[0].Count;
            foreach (var l in lists) if (l.Count != n) return null;
            var result = new float[n];
            var column = new float[lists.Count];
            for (int i = 0; i < n; i++)
            {
                for (int c = 0; c < lists.Count; c++) column[c] = lists[c][i];
                Array.Sort(column);
                result[i] = column[(column.Length - 1) / 2]; // lower median: with 2 drivers the safer (lower) cap
            }
            return result;
        }
    }
}
