using F1Racing.CameraSystem;
using F1Racing.Race;
using F1Racing.Vehicle;
using UnityEngine;

namespace F1Racing.UI
{
    /// <summary>
    /// Race screens (OnGUI): start menu, start lights, position tower, lap timer, name tags, view button, results.
    /// Displays RaceManager state and sends it commands; owns no race state itself.
    /// </summary>
    public class RaceHUD : MonoBehaviour
    {
        private const float NameTagRangeM = 120f;

        private RaceManager _race;
        private bool _confirmReset; // race 14: the learning was deleted by a misclick, so reset needs a second click
        private F1ChaseCamera _chase;
        private Camera _cam;
        private Texture2D _panel, _rowHighlight, _disc, _white;
        private GUIStyle _title, _text, _small, _big, _button, _bigButton, _tag, _center;

        public static RaceHUD Create(RaceManager race)
        {
            var hud = race.gameObject.AddComponent<RaceHUD>();
            hud._race = race;
            foreach (F1ChaseCamera c in FindObjectsByType<F1ChaseCamera>())
                if (!c.isSecondaryCamera) hud._chase = c;
            hud._cam = hud._chase != null ? hud._chase.GetComponent<Camera>() : Camera.main;
            return hud;
        }

        private void InitStyles()
        {
            if (_title != null) return;
            _panel = Solid(new Color(0.04f, 0.05f, 0.08f, 0.82f));
            _rowHighlight = Solid(new Color(0.9f, 0.15f, 0.1f, 0.45f));
            _white = Solid(Color.white);
            _disc = Disc(64);

            _title = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            _text = new GUIStyle(GUI.skin.label) { fontSize = 15, normal = { textColor = Color.white } };
            _small = new GUIStyle(_text) { fontSize = 12, normal = { textColor = new Color(0.8f, 0.82f, 0.88f) } };
            _center = new GUIStyle(_text) { alignment = TextAnchor.MiddleCenter };
            _big = new GUIStyle(_text) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _button = new GUIStyle(GUI.skin.button) { fontSize = 15, fontStyle = FontStyle.Bold };
            _bigButton = new GUIStyle(_button) { fontSize = 22 };
            _tag = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        }

        private void OnGUI()
        {
            if (_race == null) return;
            InitStyles();
            switch (_race.Phase)
            {
                case RacePhase.Menu: DrawMenu(); break;
                case RacePhase.Countdown: DrawLights(); break;
                case RacePhase.Racing: DrawRace(); break;
                case RacePhase.Finished: DrawRace(); DrawResults(); break;
            }
            if (_race.Phase != RacePhase.Menu) DrawViewButton();
        }

        // ---------------- Menu ----------------

        private void DrawMenu()
        {
            float w = 460f, h = GameModeManager.AIOpponents ? 672f : 406f;
            Rect r = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            GUI.DrawTexture(r, _panel);
            float y = r.y + 18f, x = r.x + 30f, cw = w - 60f;

            GUI.Label(new Rect(r.x, y, w, 44f), "ADAPTIVE F1", _title); y += 48f;
            GUI.Label(new Rect(r.x, y, w, 22f), $"{RaceSettings.Laps}-lap race", _center); y += 36f;
            if (GameModeManager.TrackMeshStale)
                GUI.Label(new Rect(r.x, r.y - 30f, w, 26f), "Track meshes are out of date: Tools > Track > Rebuild Monza Track", _center);

            GUI.Label(new Rect(x, y, cw, 22f), "Difficulty (your choice, locked for the whole race)", _text); y += 26f;
            int d = GUI.Toolbar(new Rect(x, y, cw, 34f), RaceSettings.DifficultyIndex, RaceSettings.DifficultyNames, _button);
            if (d != RaceSettings.DifficultyIndex) { RaceSettings.DifficultyIndex = d; GameModeManager.ReloadScene(); } // the grid is built with it
            y += 46f;

            if (GameModeManager.AIOpponents)
            {
                GUI.Label(new Rect(x, y, cw, 22f), $"AI opponents: {RaceSettings.Opponents}", _text); y += 26f;
                int o = RaceSettings.Opponents;
                float bw = (cw - 30f) / 4f;
                if (GUI.Button(new Rect(x, y, bw, 34f), "-5", _button)) o -= 5;
                if (GUI.Button(new Rect(x + bw + 10f, y, bw, 34f), "-1", _button)) o -= 1;
                if (GUI.Button(new Rect(x + 2f * (bw + 10f), y, bw, 34f), "+1", _button)) o += 1;
                if (GUI.Button(new Rect(x + 3f * (bw + 10f), y, bw, 34f), "+5", _button)) o += 5;
                o = Mathf.Clamp(o, 1, RaceSettings.MaxOpponents);
                if (o != RaceSettings.Opponents) { RaceSettings.Opponents = o; GameModeManager.ReloadScene(); } // the grid is built with it
                y += 46f;
                if (GUI.Button(new Rect(x, y, cw, 32f), RaceSettings.HumanPlayer ? "Human driver: on (you race)" : "Human driver: off (watch an AI-only race)", _button))
                {
                    RaceSettings.HumanPlayer = !RaceSettings.HumanPlayer;
                    GameModeManager.ReloadScene();
                }
                y += 46f;
            }

            float half = (cw - 10f) * 0.5f;
            if (GUI.Button(new Rect(x, y, half, 32f), GameModeManager.FunMode ? "Fun mode: on" : "Fun mode: off", _button)) GameModeManager.SwitchMode();
            if (GUI.Button(new Rect(x + half + 10f, y, half, 32f), GameModeManager.AIOpponents ? "Opponents: AI" : "Opponents: 2P", _button)) GameModeManager.ToggleAIOpponents();
            y += 46f;
            if (GUI.Button(new Rect(x, y, half, 32f), RaceSettings.Ghost ? "Ghost cars: on" : "Ghost cars: off", _button))
                RaceSettings.Ghost = !RaceSettings.Ghost;
            if (GameModeManager.AIOpponents && GUI.Button(new Rect(x + half + 10f, y, half, 32f), RaceSettings.ShowRacingLine ? "AI racing line: on (L)" : "AI racing line: off (L)", _button))
                RaceSettings.ShowRacingLine = !RaceSettings.ShowRacingLine;
            y += 46f;
            if (GameModeManager.AIOpponents)
            {
                // v1.2: compare the D04 variants at the same difficulty and physics. The grid is built with it, so reload.
                if (GUI.Button(new Rect(x, y, cw, 32f), RaceSettings.LookaheadTactics ? "AI tactics: lookahead (plans attacks and defence)"
                                                                                    : "AI tactics: scripted (baseline)", _button))
                {
                    RaceSettings.LookaheadTactics = !RaceSettings.LookaheadTactics;
                    GameModeManager.ReloadScene();
                }
                y += 46f;
                // The AI keeps the corner speed limits it learned on this track between races (TrackLearning).
                bool learned = System.IO.File.Exists(F1Racing.AI.Driving.TrackLearning.FilePath);
                string label = !learned ? "AI track learning: none saved yet"
                    : _confirmReset ? "Click again to delete the AI track learning"
                    : "Reset AI track learning (AI keeps corner limits between races)";
                if (GUI.Button(new Rect(x, y, cw, 28f), label, _button) && learned)
                {
                    if (!_confirmReset) _confirmReset = true;
                    else
                    {
                        _confirmReset = false;
                        F1Racing.AI.Driving.TrackLearning.Reset();
                        GameModeManager.ReloadScene(); // the grid loaded the old caps
                    }
                }
                y += 58f; // gap so a click aimed at START RACE can't land here
            }
            Color prev = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.3f, 1f, 0.45f);
            if (GUI.Button(new Rect(x, y, cw, 50f), "START RACE", _bigButton)) { _confirmReset = false; _race.StartCountdown(); }
            GUI.backgroundColor = prev;
            y += 60f;

            GUI.Label(new Rect(x, y, cw, 40f),
                "WASD drive · Shift DRS · Space handbrake · R reset\nClick a name in the tower to watch that car · P first person · N map" + (GameModeManager.FunMode ? " · M model · F front cam" : ""), _small);
        }

        // ---------------- Start lights ----------------

        private void DrawLights()
        {
            const float d = 54f, gap = 14f;
            float w = 5 * d + 4 * gap + 40f;
            Rect r = new Rect((Screen.width - w) * 0.5f, 60f, w, d + 40f);
            GUI.DrawTexture(r, _panel);
            Color prev = GUI.color;
            for (int i = 0; i < 5; i++)
            {
                GUI.color = i < _race.LightsOn ? new Color(1f, 0.08f, 0.05f) : new Color(0.18f, 0.05f, 0.05f);
                GUI.DrawTexture(new Rect(r.x + 20f + i * (d + gap), r.y + 20f, d, d), _disc);
            }
            GUI.color = prev;
        }

        // ---------------- Racing ----------------

        private void DrawRace()
        {
            DrawTower();
            DrawPlayerTiming();
            DrawRuleStatus();
            DrawNameTags();
            if (_race.Phase == RacePhase.Racing && _race.RaceTime < 2f)
                GUI.Label(new Rect(0f, 70f, Screen.width, 40f), "LIGHTS OUT!", _big);
        }

        private void DrawTower()
        {
            var standings = _race.Standings;
            float rowH = standings.Count > 12 ? 18f : 24f; const float w = 250f; // up to 23 cars fit a 720p screen
            RaceEntry leader = standings[0];
            int lap = Mathf.Clamp(leader.Progress.Laps + 1, 1, RaceSettings.Laps);
            Rect r = new Rect(16f, 122f, w, 30f + rowH * standings.Count);
            GUI.DrawTexture(r, _panel);
            GUI.Label(new Rect(r.x + 10f, r.y + 4f, w, 22f), _race.FlagOut ? "CHEQUERED FLAG" : $"LAP {lap} / {RaceSettings.Laps}", _text);
            if (_chase != null) GUI.Label(new Rect(r.x + 120f, r.y + 6f, w - 126f, 20f), "click a name: watch", _small);
            F1CarController watched = _chase != null ? _chase.ActiveTarget : null;

            Color prev = GUI.color;
            for (int i = 0; i < standings.Count; i++)
            {
                RaceEntry e = standings[i];
                float y = r.y + 28f + i * rowH;
                if (e == _race.Player) GUI.DrawTexture(new Rect(r.x + 2f, y, w - 4f, rowH - 2f), _rowHighlight);
                GUI.Label(new Rect(r.x + 8f, y + 1f, 26f, rowH), (i + 1).ToString(), _text);
                GUI.color = CarLivery.ColorOf(e.Car);
                GUI.DrawTexture(new Rect(r.x + 34f, y + 4f, 5f, rowH - 10f), _white);
                GUI.color = prev;
                GUI.Label(new Rect(r.x + 46f, y + 1f, 110f, rowH), e.Name, _text);
                GUI.Label(new Rect(r.x + 150f, y + 1f, 95f, rowH), _race.GapText(e) + (e.PenaltyS > 0f ? $" +{e.PenaltyS:F0}s" : ""), _text);
                Rect row = new Rect(r.x + 2f, y, w - 4f, rowH - 2f);
                if (e.Car == watched) Outline(row);
                // Choosing the camera car (replaces the old C key); the View button then picks chase / far / cockpit POV.
                if (_chase != null && GUI.Button(row, GUIContent.none, GUIStyle.none)) _chase.Follow(e.Car);
            }
        }

        private void Outline(Rect r)
        {
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, 1f), _white);
            GUI.DrawTexture(new Rect(r.x, r.yMax - 1f, r.width, 1f), _white);
            GUI.DrawTexture(new Rect(r.x, r.y, 1f, r.height), _white);
            GUI.DrawTexture(new Rect(r.xMax - 1f, r.y, 1f, r.height), _white);
        }

        /// <summary>Player's DRS state, flags and the latest steward note (track limits, jump start).</summary>
        private void DrawRuleStatus()
        {
            RaceEntry p = _race.Player;
            if (p == null || p.Finished) return;
            const float w = 380f;
            float x = (Screen.width - w) * 0.5f, y = 96f;
            Color prev = GUI.color;

            string drs = p.Car.IsDrsActive ? "DRS OPEN" : p.Car.DrsAllowed ? "DRS AVAILABLE (Shift)" : p.DrsEligible ? "DRS: wait for the zone" : "DRS: not enabled";
            GUI.color = p.Car.IsDrsActive || p.Car.DrsAllowed ? new Color(0.3f, 1f, 0.4f) : new Color(0.75f, 0.75f, 0.75f);
            GUI.Label(new Rect(x, y, w, 22f), drs + (p.Strikes > 0 ? $"     Track limits {p.Strikes}" : ""), _center);
            y += 24f;

            string flag = p.Strikes == RaceRules.BlackWhiteFlagStrike ? "BLACK AND WHITE FLAG" : p.BlueFlag ? "BLUE FLAG" : p.YellowAhead ? "YELLOW FLAG" : null;
            if (flag != null)
            {
                GUI.color = p.BlueFlag ? new Color(0.3f, 0.6f, 1f) : p.YellowAhead && flag == "YELLOW FLAG" ? new Color(1f, 0.85f, 0.1f) : Color.white;
                GUI.DrawTexture(new Rect(x + 90f, y, w - 180f, 26f), _panel);
                GUI.Label(new Rect(x, y + 2f, w, 22f), flag, _center);
                y += 30f;
            }
            GUI.color = prev;

            if (Time.time - p.MessageTime < 4f)
            {
                GUI.DrawTexture(new Rect(x, y, w, 28f), _panel);
                GUI.Label(new Rect(x, y + 3f, w, 22f), p.Message, _center);
            }
        }

        private void DrawPlayerTiming()
        {
            RaceEntry p = _race.Player;
            if (p == null) return;
            float w = 380f;
            Rect r = new Rect((Screen.width - w) * 0.5f, 12f, w, 78f);
            GUI.DrawTexture(r, _panel);
            int lap = Mathf.Clamp(p.Progress.Laps + 1, 1, RaceSettings.Laps);
            string head = p.Finished
                ? $"FINISHED  P{_race.PositionOf(p)} / {_race.Standings.Count}"
                : $"P{_race.PositionOf(p)} / {_race.Standings.Count}     LAP {lap} / {RaceSettings.Laps}";
            GUI.Label(new Rect(r.x, r.y + 4f, w, 36f), head, _big);
            float current = p.Finished ? float.NaN : _race.RaceTime - p.LapStartTime;
            float last = p.LapTimes.Count > 0 ? p.LapTimes[p.LapTimes.Count - 1] : float.NaN;
            GUI.Label(new Rect(r.x, r.y + 44f, w, 26f),
                $"Lap {RaceStandings.FormatTime(current)}   Last {RaceStandings.FormatTime(last)}   Best {RaceStandings.FormatTime(p.BestLap)}", _center);
        }

        private void DrawNameTags()
        {
            if (_cam == null) return;
            F1CarController followed = _chase != null ? _chase.ActiveTarget : null;
            Color prev = GUI.contentColor;
            foreach (RaceEntry e in _race.Standings)
            {
                if (e.Car == followed) continue;
                Vector3 world = e.Car.transform.position + Vector3.up * 1.6f;
                Vector3 s = _cam.WorldToScreenPoint(world);
                if (s.z <= 0f || s.z > NameTagRangeM) continue;
                Rect r = new Rect(s.x - 60f, Screen.height - s.y - 12f, 120f, 22f);
                GUI.DrawTexture(new Rect(r.x + 20f, r.y + 2f, 80f, 18f), _panel);
                GUI.contentColor = CarLivery.ColorOf(e.Car);
                GUI.Label(r, $"P{_race.PositionOf(e)} {e.Name}", _tag);
            }
            GUI.contentColor = prev;
        }

        private void DrawViewButton()
        {
            if (_chase == null) return;
            Rect r = new Rect(Screen.width - 186f, Screen.height - 96f, 170f, 36f);
            if (GUI.Button(r, "View: " + F1ChaseCamera.ViewNames[_chase.ViewIndex], _button)) _chase.CycleView();
        }

        // ---------------- Results ----------------

        private void DrawResults()
        {
            var standings = _race.Standings;
            float rowH = standings.Count > 12 ? 20f : 28f; const float w = 560f;
            float h = 120f + rowH * standings.Count + 60f;
            Rect r = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            GUI.DrawTexture(r, _panel);
            GUI.Label(new Rect(r.x, r.y + 12f, w, 44f), "RESULTS", _title);

            float y = r.y + 70f;
            GUI.Label(new Rect(r.x + 24f, y, 60f, rowH), "Pos", _small);
            GUI.Label(new Rect(r.x + 80f, y, 160f, rowH), "Driver", _small);
            GUI.Label(new Rect(r.x + 250f, y, 150f, rowH), "Time / gap", _small);
            GUI.Label(new Rect(r.x + 380f, y, 120f, rowH), "Best lap", _small);
            GUI.Label(new Rect(r.x + 500f, y, 50f, rowH), "Pts", _small);
            y += rowH;

            Color prev = GUI.color;
            for (int i = 0; i < standings.Count; i++)
            {
                RaceEntry e = standings[i];
                if (e == _race.Player) GUI.DrawTexture(new Rect(r.x + 12f, y, w - 24f, rowH - 2f), _rowHighlight);
                GUI.Label(new Rect(r.x + 24f, y + 2f, 60f, rowH), $"P{i + 1}", _text);
                GUI.color = CarLivery.ColorOf(e.Car);
                GUI.DrawTexture(new Rect(r.x + 68f, y + 5f, 5f, rowH - 12f), _white);
                GUI.color = prev;
                GUI.Label(new Rect(r.x + 80f, y + 2f, 160f, rowH), e.Name + (e.Finished ? "" : "  (running)"), _text);
                GUI.Label(new Rect(r.x + 250f, y + 2f, 150f, rowH), _race.ResultText(e) + (e.PenaltyS > 0f ? $" (+{e.PenaltyS:F0}s)" : ""), _text);
                if (e == _race.FastestLap) GUI.color = new Color(0.75f, 0.35f, 1f); // purple: fastest lap
                else if (float.IsNaN(e.BestLap)) GUI.color = new Color(0.6f, 0.6f, 0.6f); // grey: only deleted laps
                GUI.Label(new Rect(r.x + 380f, y + 2f, 120f, rowH), RaceManager.BestLapText(e), _text);
                GUI.color = prev;
                GUI.Label(new Rect(r.x + 500f, y + 2f, 50f, rowH), e.Finished ? RaceRules.PointsFor(i + 1).ToString() : "-", _text);
                y += rowH;
            }

            y += 14f;
            float bw = (w - 60f) * 0.5f;
            if (GUI.Button(new Rect(r.x + 20f, y, bw, 42f), "RESTART", _bigButton))
            {
                RaceSettings.SkipMenu = true;
                GameModeManager.ReloadScene();
            }
            if (GUI.Button(new Rect(r.x + 40f + bw, y, bw, 42f), "MENU", _bigButton)) GameModeManager.ReloadScene();
        }

        // ---------------- Textures ----------------

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        private static Texture2D Disc(int size)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(r - dist)));
                }
            t.Apply();
            return t;
        }
    }
}
