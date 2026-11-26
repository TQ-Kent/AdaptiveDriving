using System.Collections.Generic;
using F1Racing.AI.Driving;
using F1Racing.Race;
using F1Racing.Track;
using F1Racing.Vehicle;
using UnityEditor;
using UnityEngine;

/// <summary>Quick checks for VehicleCommand sanitising and the lap-counting rule. Tools > Run Seam Self-Check.</summary>
public static class SeamSelfCheck
{
    [MenuItem("Tools/Run Seam Self-Check")]
    public static void Run()
    {
        var c = new VehicleCommand { Steer = 3f, Throttle = -2f, Brake = float.NaN }.Sanitized();
        Check(c.Steer == 1f && c.Throttle == 0f && c.Brake == 0f, "Sanitized clamps ranges and zeroes NaN");

        const float L = 1000f;
        var lap = new LapCounter();
        Check(!lap.Step(990f, L) && !lap.Step(5f, L), "race-start crossing without halfway does not count");

        lap = new LapCounter();
        int laps = 0;
        for (float d = 0f; d < L; d += 10f) if (lap.Step(d, L)) laps++;
        if (lap.Step(5f, L)) laps++;
        Check(laps == 1, "full forward lap counts once");

        laps = 0;
        foreach (float d in new[] { 995f, 5f, 990f, 3f }) if (lap.Step(d, L)) laps++;
        Check(laps == 0, "rocking back and forth over the line does not count");

        // Handling helpers
        float prev = 99f;
        for (float v = 5f; v <= 95f; v += 5f)
        {
            float lim = F1CarController.GripSteerLimitDeg(v, 3f, 20f + v * 0.3f, 5f, 3.8f, 24f);
            Check(lim >= 3.8f && lim <= 24f && lim <= prev + 1e-4f, "steer limit in bounds and non-increasing with speed");
            prev = lim;
        }
        Check(F1CarController.GripSteerLimitDeg(0f, 3f, 20f, 5f, 3.8f, 24f) == 24f, "steer limit at standstill is full lock");

        float s = 0f;
        for (int i = 0; i < 100; i++) s = F1CarController.ShapeSteerInput(s, 1f, 3f, 5f, 0.02f);
        Check(Mathf.Approximately(s, 1f), "held key ramps to full steer");
        Check(F1CarController.ShapeSteerInput(s, -1f, 3f, 5f, 0.02f) < 0f, "reversing key flips through centre immediately");
        for (int i = 0; i < 100; i++) s = F1CarController.ShapeSteerInput(s, 0f, 3f, 5f, 0.02f);
        Check(s == 0f, "released key returns to centre");

        Check(F1CarController.TargetYawRate(0f, 10f, 3f) == 0f && F1CarController.TargetYawRate(30f, 5f, 3f) > 0f,
            "target yaw rate is zero at rest and follows the steer sign");

        // AI driving helpers: a 1 km loop of straight with one 50 m-radius corner.
        const int n = 200;
        var dist = new float[n];
        var curv = new float[n];
        for (int i = 0; i < n; i++) { dist[i] = i * 5f; curv[i] = i >= 100 && i < 120 ? 0.02f : 0f; }
        const float vMax = 90f, brakeDecel = 12f;
        var profile = SpeedProfile.Build(dist, curv, n * 5f, vMax, 15f, 0.004f, brakeDecel);
        bool bounded = true, reachable = true;
        for (int i = 0; i < n; i++)
        {
            float v = profile.Speeds[i], next = profile.Speeds[(i + 1) % n];
            bounded &= v <= vMax;
            reachable &= v * v <= next * next + 2f * brakeDecel * 5f + 1e-2f;
        }
        Check(bounded, "speed profile never exceeds vMax");
        Check(reachable, "speed profile never asks for more than brakeDecel");
        Check(profile.TargetAt(550f) < vMax && profile.TargetAt(100f) == vMax, "corner is slower than the straight");
        // Friction circle (race 24): at the corner limit only the floor share of brakeDecel is left; on the straight before it, all of it.
        // The corner limit starts at point 98 (curvature is the worst over +-2 points).
        float vEntry = profile.Speeds[98], v97 = profile.Speeds[97], v91 = profile.Speeds[91], v90 = profile.Speeds[90];
        Check(Mathf.Abs(v97 * v97 - (vEntry * vEntry + 2f * brakeDecel * SpeedProfile.BrakeFloorShare * 5f)) < 0.1f,
              "braking into a corner at its grip limit uses only the floor share of brakeDecel");
        Check(v90 < vMax && Mathf.Abs(v90 * v90 - (v91 * v91 + 2f * brakeDecel * 5f)) < 0.05f * v90 * v90,
              "braking on the straight before the corner uses (nearly) the full brakeDecel");

        var roster = new[] { new OpponentIdentity("a", 0.04f, 0.5f, Color.white), new OpponentIdentity("b", -0.03f, 0.5f, Color.white),
                             new OpponentIdentity("c", 0.07f, 0.5f, Color.white) };
        OpponentIdentity[] rel = PaceProfileMapper.RelativeToBest(roster, 2);
        Check(rel.Length == 2 && rel[0].skillOffset == 0f && Mathf.Approximately(rel[1].skillOffset, -0.07f) && roster[0].skillOffset == 0.04f,
              "pace spread: the best driver in the race sits at the session difficulty, the others below; the roster is unchanged");
        Check(PaceProfileMapper.Effective(1f, rel[0]) == 1f && PaceProfileMapper.Effective(1f, rel[1]) < 1f, "at Expert only the best driver is at the top");

        var id = new OpponentIdentity("test", 0f, 0.5f, Color.white);
        PaceSettings lo = PaceProfileMapper.Map(0f, id), mid = PaceProfileMapper.Map(0.5f, id), hi = PaceProfileMapper.Map(1f, id);
        Check(lo.gripUtilization < mid.gripUtilization && mid.gripUtilization < hi.gripUtilization
              && lo.brakeDecel < hi.brakeDecel && lo.throttleDemand <= hi.throttleDemand, "pace mapper is monotonic in difficulty");
        Check(PaceProfileMapper.Map(float.NaN, id).gripUtilization == lo.gripUtilization
              && PaceProfileMapper.Map(5f, id).gripUtilization == hi.gripUtilization, "pace mapper clamps NaN and out-of-range input");

        Check(PathFollower.Steer(new Vector3(2f, 0f, 10f), 3f, 20f) > 0f
              && PathFollower.Steer(new Vector3(-2f, 0f, 10f), 3f, 20f) < 0f
              && PathFollower.Steer(new Vector3(0f, 0f, 10f), 3f, 20f) == 0f, "pure pursuit steers toward the target side");

        var reply = new F1Racing.AI.Integration.PythonAIClient.Reply { ok = true, schema = 1, difficulty = float.NaN, uncertainty = 1f, model_version = "x" };
        Check(!F1Racing.AI.Integration.PythonAIClient.IsValid("recommend_difficulty", reply), "AI bridge rejects NaN difficulty");
        reply.difficulty = 0.5f;
        Check(F1Racing.AI.Integration.PythonAIClient.IsValid("recommend_difficulty", reply), "AI bridge accepts a valid proposal");

        // Race order: a car still behind the line on the grid ranks behind one that just crossed it.
        Check(LapCounter.RaceProgress(0, 990f, L, false) < LapCounter.RaceProgress(0, 5f, L, false)
              && LapCounter.RaceProgress(0, 700f, L, true) > LapCounter.RaceProgress(0, 300f, L, true), "race progress: grid car is behind the line");
        Check(RaceStandings.SortKey(true, 2, L, 2000f) < RaceStandings.SortKey(false, 0, L, 2100f), "lead-lap runner passes a lapped finisher");
        var leaderT = new List<float> { 0f, 5f, 10f };
        Check(Mathf.Approximately(RaceStandings.GapSeconds(leaderT, new List<float> { 0.5f, 6.5f }), 1.5f), "gap uses the last shared marker");
        var lapped = new List<float>();
        for (int i = 0; i < RaceStandings.MarkersPerLap + 3; i++) lapped.Add(i);
        Check(RaceStandings.LapsBehind(lapped, leaderT) == 1, "a full lap of markers behind is +1 lap");

        // Tactics. Slot-2 grid case: player ahead-left, AI ahead-right -> both lanes blocked, keep the current lane.
        var tactics = new ScriptedTacticalDriver();
        Check(tactics.Decide(0f, 6f, false, false, -1f, -1f, 0.5f) == -1f, "both lanes blocked: follow in the current lane");
        tactics = new ScriptedTacticalDriver();
        Check(tactics.Decide(0f, 6f, false, true, -1f, 0f, 0.5f) == 1f, "preferred side blocked: take the free one");
        Check(tactics.Decide(0.5f, 6f, true, false, -1f, 1f, 0.5f) != 1f, "chosen lane closes: abort the move");
        tactics = new ScriptedTacticalDriver();
        Check(tactics.Decide(0f, 100f, true, true, 1f, 0f, 0.5f) == 0f, "clear road: centre line");
        tactics.Commit(1f, 10f);
        Check(tactics.Decide(1f, 100f, true, true, 0f, 0f, 0.5f) == 1f, "a committed dive holds its side");
        tactics.Release();
        Check(tactics.Decide(1f, 100f, true, true, 0f, 1f, 0.5f) == 0f, "a released dive backs out to the line (v1.2 abort path)");

        // Recovery: stuck = no race progress (50 ticks/s).
        var recovery = new RecoveryController();
        bool reset = false;
        for (int i = 0; i < 400; i++) reset |= recovery.Tick(0.02f, 0f, 1f, 0f, 8f, 1f, true, out _);
        Check(!reset, "a car waiting 8 s behind traffic is not reset as stuck");
        for (int i = 0; i < 150 && !reset; i++) reset = recovery.Tick(0.02f, 0f, 1f, 0f, 8f, 1f, true, out string why) && why == "stuck";
        Check(reset, "a car blocked by a wreck for 10 s is reset");
        recovery = new RecoveryController();
        reset = false;
        for (int i = 0; i < 500; i++) reset |= recovery.Tick(0.02f, i * 0.2f, 1f, 0f, 8f, 1f, false, out _); // 10 m/s
        Check(!reset, "a slow car that keeps moving along the track is not reset");
        for (int i = 0; i < 200 && !reset; i++) reset = recovery.Tick(0.02f, 100f, 1f, 0f, 8f, 1f, false, out string why) && why == "stuck";
        Check(reset, "a car making no progress in clear road (stopped or circling) is reset as stuck");

        CheckRacingLine();
        CheckTactics();
        CheckSpeedCap();
        CheckCarPhysics();
        CheckRules();
        CheckTrack();

        // Corner memory: 100 bins of 5 m. Learning at 300 m lowers the approach (260-310 m), floors at MinScale, leaves the rest.
        var corners = new CornerMemory(100, 500f);
        Check(corners.Learn(300f, 0.05f) && Mathf.Approximately(corners.Scale[60], 0.95f) && Mathf.Approximately(corners.Scale[52], 0.95f)
              && corners.Scale[51] == 1f && corners.Scale[63] == 1f, "corner memory lowers the corner and its approach only");
        for (int i = 0; i < 20; i++) corners.Learn(300f, 0.05f);
        Check(Mathf.Approximately(corners.Scale[60], CornerMemory.MinScale) && !corners.Learn(300f, 0.05f), "corner memory stops at its floor");
        for (int i = 0; i < 50; i++) corners.Recover(0.01f);
        Check(corners.Scale[60] == 1f && !corners.Recover(0.01f), "learning recovers lap by lap back to the plan, then stops");
        Check(PathFollower.SteerDemand(new Vector3(8f, 0f, 4f), 3f, 5f) > 1f, "a target beyond full lock reports steer demand above 1 (understeer)");

        // Track-limit guard (white line at 9 m): broke in races 10, 11 and 12, so every case that went wrong is pinned here.
        Check(!AIDriver.AtTrackLimit(4f, 5f, 8f, 75f, 9f), "following a line that sweeps across the track is not a track-limit event");
        Check(AIDriver.AtTrackLimit(8f, 5f, 2f, 75f, 9f) && AIDriver.AtTrackLimit(-8f, -5f, -2f, 75f, 9f), "drifting out past the path near the line triggers (both sides)");
        Check(!AIDriver.AtTrackLimit(9.5f, 5f, -2f, 30f, 9f), "steering back in from the run-off does not trigger (no throttle cut, no cap)");
        Check(!AIDriver.AtTrackLimit(8f, 7.5f, 2f, 75f, 9f), "near the line but on its own path does not trigger");
        Check(!AIDriver.AtTrackLimit(8f, 5f, 2f, 6f, 9f), "below 8 m/s does not trigger");

        // Minimum following gap (race 17 first-chicane pile-up): lift inside it even when not closing, brake when very close.
        var full = new VehicleCommand { Throttle = 1f };
        Check(DrivingSafetyFilter.Apply(full, 50f, 0.2f, 0f).Throttle == 0f, "inside the minimum gap the car lifts even when not closing");
        Check(DrivingSafetyFilter.Apply(full, 50f, 0.05f, 0f).Brake > 0.3f, "very close behind, the car brakes");
        Check(DrivingSafetyFilter.Apply(full, 50f, 0.6f, 0f).Throttle == 1f, "a safe gap and not closing leaves the command alone");
        var drs = new VehicleCommand { Throttle = 1f, Drs = true };
        VehicleCommand tow = DrivingSafetyFilter.Apply(drs, 60f, 0.5f, 3f); // 30 m back closing 3 m/s: 10 s to contact
        Check(tow.Throttle == 1f && tow.Drs, "closing slowly in the tow keeps full throttle and DRS (race 28)");
        Check(DrivingSafetyFilter.Apply(full, 60f, 0.5f, 25f).Throttle <= 0.2f, "1.2 s to contact: the car lifts");
        Check(DrivingSafetyFilter.Apply(full, 60f, 0.4f, 40f).Brake > 0f, "0.6 s to contact: the car brakes");

        // Learned caps (race 23): a share of the corner limit, approach included, floored, lowered by a cap, raised per lap.
        var caps = new CornerMemory(100, 500f);
        Check(caps.CapAt(300f, 0.9f) && caps.Scale[60] == 0.9f && caps.Scale[52] == 0.9f && caps.Scale[51] == 1f,
              "a cap covers the corner and its approach only");
        Check(!caps.CapAt(300f, 0.95f) && caps.CapAt(300f, 0.8f) && caps.Scale[60] == 0.8f && !caps.CapAt(100f, 1.2f), "a cap only ever lowers");
        caps.CapAt(100f, 0.1f);
        Check(caps.Scale[20] == CornerMemory.MinScale, "a cap from a crawling car is floored");
        caps.Recover(0.01f);
        Check(Mathf.Approximately(caps.Scale[60], 0.81f) && caps.Scale[51] == 1f, "caps rise 1% a lap toward the plan; no cap stays none");
        var preload = new CornerMemory(100, 500f);
        preload.Preload(caps.Scale);
        Check(preload.Scale[60] == caps.Scale[60] && preload.Scale[51] == 1f, "caps learned in an earlier race load into a new driver");
        float[] med = TrackLearning.Median(new[] { (IReadOnlyList<float>)new[] { 0.7f, 1f, 0.8f }, new[] { 0.8f, 1f, 1f }, new[] { 0.6f, 0.9f, 1f } });
        Check(med[0] == 0.7f && med[1] == 1f && med[2] == 1f, "saved caps are the per-point median over drivers");

        // Best lap: deleted laps never count as best, but are shown when there is no valid lap.
        var entry = new F1Racing.Race.RaceEntry();
        entry.LapTimes.AddRange(new[] { 40f, 38f }); entry.LapValid.AddRange(new[] { true, false });
        Check(entry.BestLap == 40f && entry.BestAnyLap == 38f, "best lap ignores deleted laps; best-any includes them");

        Debug.Log("[SeamSelfCheck] all checks passed");
    }

    /// <summary>Rounded square (200 m sides, 20 m-radius corners, 5 m spacing): the line must stay in bounds and open the corners.</summary>
    private static void CheckRacingLine()
    {
        var pts = new System.Collections.Generic.List<Vector2>();
        Vector2[] centres = { new Vector2(200, 20), new Vector2(200, 200), new Vector2(0, 200), new Vector2(0, 20) };
        for (int c = 0; c < 4; c++)
        {
            float a0 = -Mathf.PI / 2f + c * Mathf.PI / 2f;
            Vector2 start = centres[c] + 20f * new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
            Vector2 dir = new Vector2(-Mathf.Sin(a0), Mathf.Cos(a0));
            for (int k = 0; k < 36; k++) pts.Add(start - dir * (180f - k * 5f)); // straight leading into the corner
            for (int k = 0; k < 6; k++) { float a = a0 + k * (Mathf.PI / 2f) / 6f; pts.Add(centres[c] + 20f * new Vector2(Mathf.Cos(a), Mathf.Sin(a))); }
        }
        int m = pts.Count;
        var centre = pts.ToArray();
        var right = new Vector2[m];
        for (int i = 0; i < m; i++)
        {
            Vector2 t = (centre[(i + 1) % m] - centre[(i - 1 + m) % m]).normalized;
            right[i] = new Vector2(t.y, -t.x); // Cross(up, tangent) in (x, z)
        }
        float[] o = RacingLine.Optimize(centre, right, 6f);
        float maxAbsO = 0f, kCentre = 0f, kLine = 0f;
        foreach (float v in o) maxAbsO = Mathf.Max(maxAbsO, Mathf.Abs(v));
        foreach (float k in RacingLine.CurvaturesOf(centre)) kCentre = Mathf.Max(kCentre, Mathf.Abs(k));
        foreach (float k in RacingLine.CurvaturesOf(RacingLine.Points(centre, right, o))) kLine = Mathf.Max(kLine, Mathf.Abs(k));
        Check(maxAbsO <= 6f + 1e-4f, "racing line stays inside the usable width");
        Check(kLine < 0.8f * kCentre, $"racing line opens the corners (max curvature {kLine:F4} vs centre {kCentre:F4})");
    }

    /// <summary>A steering-lock speed cap lowers that point of the AI's own speed profile, and the braking pass reaches it.</summary>
    /// <summary>v1.2 D04 lookahead duel cases. Pure: the forecasts are fixed numbers, no scene.</summary>
    private static void CheckTactics()
    {
        float t = TacticalWorldModel.TimeTo(d => 50f, v => 0f, d => 1f, 0f, 50f, 100f, 1000f);
        Check(Mathf.Abs(t - 2f) < 0.06f, "world model: 100 m at a steady 50 m/s takes 2 s");
        Check(Mathf.Abs(TacticalWorldModel.TimeTo(d => 50f, v => 0f, d => 1f, 990f, 50f, 40f, 1000f) - 1f) < 0.06f, "world model wraps the start line");
        Check(TacticalWorldModel.TimeTo(d => 50f, v => 0f, d => 0.5f, 0f, 50f, 100f, 1000f) < t - 0.5f, "a shorter lane (stretch < 1) is quicker");

        var id = new OpponentIdentity("duel", 0f, 0.5f, Color.white, 0.5f, 1f, 0f); // patience 0: attacks on any predicted gain
        TacticalContext Duel() => new TacticalContext
        {
            Now = 10f, SpeedMs = 60f, SplineM = 100f, LoopM = 2000f, CurrentSide = 0f, BrakeDecelMs2 = 28f, Lap = 1,
            LeftFree = true, LineFree = false, RightFree = true, LeftClear = true, LineClear = false, RightClear = true,
            HasAhead = true, GapAheadM = 8f, AheadSpeedMs = 55f, AheadSide = 0f, CornerSide = 1f, BrakeInM = 80f, CornerExitM = 300f,
            SelfTime = (side, late, to) => side > 0f && late ? 3.0f : 3.5f, // down the inside, braking later, it gets to the exit first
            AheadTime = to => 3.3f,
            BehindTime = (side, late, to) => 9f,
        };
        var attacker = new LookaheadTacticalDriver(id);
        TacticalDecision d1 = attacker.Decide(Duel());
        Check(d1.Started && d1.Maneuver == Maneuver.AttackInside && d1.Side == 1f && d1.Late, $"faster into the corner with the inside free: attack the inside ({d1.Reason})");
        TacticalContext held = Duel();
        held.Now = 10.1f;
        Check(attacker.Decide(held).Maneuver == Maneuver.AttackInside, "a committed attack is held");
        held.RightClear = false;
        Check(attacker.Decide(held).Maneuver == Maneuver.BackOut, "a committed attack backs out when the inside closes");

        TacticalContext noRoom = Duel();
        noRoom.RightFree = false;
        Check(new LookaheadTacticalDriver(id).Decide(noRoom).Maneuver != Maneuver.AttackInside, "no room on the inside: no inside attack");
        TacticalContext apex = Duel(); // race 30: at the apex no braking zone is ahead; it read as a straight
        apex.CornerSide = 0f;
        apex.InCorner = true;
        Check(!new LookaheadTacticalDriver(id).Decide(apex).Started, "in a corner past its braking zone: no new maneuver");
        TacticalContext lapOne = Duel(); // races 31-34: lap-1 pile-ups after the first chicane
        lapOne.Calm = true;
        Check(!new LookaheadTacticalDriver(id).Decide(lapOne).Started, "opening-lap calm zone: no new maneuver");
        TacticalContext noAbort = Duel(); // closing 40 m/s needs 1600 / (2 * 0.8 * 28) = 36 m of the 25 m there is
        noAbort.SpeedMs = 80f;
        noAbort.AheadSpeedMs = 40f;
        noAbort.GapAheadM = 30f;
        Check(new LookaheadTacticalDriver(id).Decide(noAbort).Maneuver != Maneuver.AttackInside, "a late inside attack with no way to back out is rejected");
        TacticalContext small = Duel();
        small.AheadTime = to => 3.1f; // 0.1 s predicted gain
        var patient = new LookaheadTacticalDriver(new OpponentIdentity("calm", 0f, 0.5f, Color.white, 0.5f, 0.5f, 1f));
        Check(patient.Decide(small).Maneuver != Maneuver.AttackInside, "a patient driver needs a bigger predicted gain");

        // Defence: the car 10 m behind would reach the exit first down the inside; covering it leaves it no better way.
        TacticalContext Chased() => new TacticalContext
        {
            Now = 20f, SpeedMs = 60f, SplineM = 100f, LoopM = 2000f, CurrentSide = 0f, BrakeDecelMs2 = 28f, Lap = 1,
            LeftFree = true, LineFree = true, RightFree = true, LeftClear = true, LineClear = true, RightClear = true, LineMergeFree = true,
            HasBehind = true, GapBehindM = 10f, BehindSpeedMs = 62f, CornerSide = 1f, BrakeInM = 100f, CornerExitM = 300f,
            SelfTime = (side, late, to) => side > 0f ? 3.1f : 3.0f,
            AheadTime = to => 9f,
            BehindTime = (side, late, to) => side > 0f && late ? 2.9f : 3.4f,
        };
        var defender = new LookaheadTacticalDriver(id);
        TacticalDecision dd = defender.Decide(Chased());
        Check(dd.Started && dd.Maneuver == Maneuver.DefendInside && dd.Side == 1f, $"a threatened car covers the inside ({dd.Reason})");
        TacticalContext exit = Chased();
        exit.Now = 22f;
        exit.SplineM = 320f;
        exit.CurrentSide = 1f;
        Check(defender.Decide(exit).Ended, "the defence ends at the corner exit");
        TacticalContext again = Chased();
        again.Now = 23f;
        again.SplineM = 120f;
        Check(defender.Decide(again).Maneuver == Maneuver.HoldLine, "one defensive move per corner");
        TacticalContext calm = Chased();
        calm.BehindTime = (side, late, to) => 3.5f; // no threat
        Check(new LookaheadTacticalDriver(id).Decide(calm).Maneuver == Maneuver.HoldLine, "no threat behind: no defence");
    }

    private static void CheckSpeedCap()
    {
        const float length = 200f;
        var dist = new float[20];
        var straight = new float[20]; // zero curvature: every point plans vMax
        var caps = new float[20];
        for (int i = 0; i < 20; i++) { dist[i] = i * 10f; caps[i] = i == 5 ? 30f : 100f; }
        SpeedProfile capped = SpeedProfile.Build(dist, straight, length, 50f, 15f, 0.002f, 15f, null, caps);
        Check(Mathf.Approximately(capped.TargetAt(150f), 50f), "a straight plans vMax");
        Check(Mathf.Approximately(capped.TargetAt(50f), 30f) && capped.TargetAt(40f) < 50f, "a steering-lock speed cap lowers that point and is braked for");
        var share = new float[20];
        for (int i = 0; i < 20; i++) share[i] = i == 10 ? 0.5f : 1f;
        SpeedProfile learned = SpeedProfile.Build(dist, straight, length, 50f, 15f, 0.002f, 15f, share, caps);
        Check(Mathf.Approximately(learned.TargetAt(100f), 25f) && Mathf.Approximately(learned.LimitAt(100f), 50f),
              "a learned share lowers the target, not the corner limit it is measured against");
    }

    /// <summary>Power-limited drive, drag top speed, ABS, and one shared physical spec for every car in the open scene.</summary>
    private static void CheckCarPhysics()
    {
        Check(F1CarController.DriveForce(750000f, 0f, 9000f) == 9000f, "drive force is traction-limited from a standstill");
        Check(Mathf.Approximately(F1CarController.DriveForce(750000f, 75f, 1e6f), 10000f), "drive force is P/v at speed");
        float top = F1CarController.PowerLimitedTopSpeed(750000f, 0.5f * 1.225f * 1.4f, 118f) * 3.6f;
        Check(top > 330f && top < 360f, $"750 kW with CdA 1.4 tops out near 345 km/h (got {top:F0})");
        Check(F1CarController.PowerLimitedTopSpeed(750000f, 0.5f * 1.225f * 1.26f, 118f) * 3.6f > top, "less drag (DRS) gives a higher top speed");
        Check(F1CarController.AbsRelease(0.1f) == 0f && F1CarController.AbsRelease(-0.2f) == 0f && F1CarController.AbsRelease(-0.6f) == 1f,
              "ABS leaves a rolling wheel alone and releases a locked one");

        string spec = null;
        foreach (F1CarController car in Object.FindObjectsByType<F1CarController>())
        {
            string key = car.PhysicsSpecKey();
            Check(spec == null || key == spec, $"{car.name} has the same physical spec as the other cars (fairness)");
            spec = key;
        }
    }

    /// <summary>Simplified F1 rules: track-limit strikes, DRS eligibility and zones, jump start, points.</summary>
    private static void CheckRules()
    {
        var limits = new TrackLimitMonitor();
        int strikes = 0;
        foreach (float lat in new[] { 0f, 9.5f, 10.5f, 11f, 10.2f, 9.5f, 8f, 10.6f, -3f, -10.4f })
            if (limits.Step(lat, 9f)) strikes++;
        Check(strikes == 3, $"each excursion past the limit counts once, and only with all four wheels off (got {strikes})");
        Check(RaceRules.PenaltyForStrike(1) == 0f && RaceRules.PenaltyForStrike(3) == 0f && RaceRules.PenaltyForStrike(4) == 5f && RaceRules.PenaltyForStrike(6) == 5f,
              "strikes 1-3 are warnings (3 = black-and-white flag), 4 and later are +5 s each");

        Check(!RaceRules.DrsEligible(1, 0.3f), "no DRS on lap 1");
        Check(RaceRules.DrsEligible(2, 0.9f) && !RaceRules.DrsEligible(2, 1.1f), "DRS when under 1.0 s behind at detection");
        Check(!RaceRules.DrsEligible(3, float.PositiveInfinity) && !RaceRules.DrsEligible(3, float.NaN), "no car ahead: no DRS");
        Check(RaceRules.Crossed(1330f, 1345f, 1338f, 2484f) && !RaceRules.Crossed(1340f, 1345f, 1338f, 2484f), "detection line crossing");
        Check(RaceRules.Crossed(2480f, 10f, 5f, 2484f), "detection line crossing over the lap wrap");
        Check(!RaceRules.Crossed(1345f, 1330f, 1338f, 2484f) && !RaceRules.Crossed(100f, 1500f, 1338f, 2484f), "reversing or a reset jump is not a crossing");
        var main = new DrsZone(5f, 113f, 470f);
        Check(RaceRules.InZone(main, 300f, 2484f) && !RaceRules.InZone(main, 600f, 2484f) && !RaceRules.InZone(main, 50f, 2484f), "DRS zone membership");

        Check(RaceRules.PointsFor(1) == 25 && RaceRules.PointsFor(10) == 1 && RaceRules.PointsFor(11) == 0 && RaceRules.PointsFor(0) == 0, "points table");
        Check(RaceRules.JumpStartM > 0f && RaceRules.JumpStartM < 1f, "jump start threshold is under a metre");
        Check(Mathf.Approximately(RaceRules.SlipstreamFactor(0.25f, 0.5f), 0.75f) && RaceRules.SlipstreamFactor(1.2f, 0f) == 0f
              && RaceRules.SlipstreamFactor(0.3f, 3f) == 0f && RaceRules.SlipstreamFactor(float.PositiveInfinity, 0f) == 0f,
              "slipstream: full right behind, none beyond 1 s or 2 m to the side");
    }

    /// <summary>The scene's track: Monza-sized, no corner tighter than the half-width, sections far enough apart for the run-off.</summary>
    private static void CheckTrack()
    {
        var track = Object.FindAnyObjectByType<F1TrackGenerator>();
        if (track == null) { Debug.LogWarning("[SeamSelfCheck] no F1TrackGenerator in the open scene; track checks skipped"); return; }
        var samples = track.Spline.Samples;
        float length = track.Spline.TotalLength;
        Check(length > 2200f && length < 2800f, $"track is a scaled Monza of about 2.5 km (got {length:F0} m)");

        float maxK = 0f;
        foreach (var s in samples) maxK = Mathf.Max(maxK, Mathf.Abs(s.SignedCurvature));
        Check(1f / maxK > track.trackWidth * 0.5f + track.kerbWidth, $"tightest corner radius {1f / maxK:F1} m is wider than the track's half-width plus kerb");

        float barrier = track.trackWidth * 0.5f + track.barrierOffset + track.barrierThickness, closest = float.MaxValue;
        for (int i = 0; i < samples.Count; i += 2)
            for (int j = i + 1; j < samples.Count; j += 2)
            {
                float arc = Mathf.Abs(samples[i].Distance - samples[j].Distance);
                if (Mathf.Min(arc, length - arc) < 150f) continue;
                closest = Mathf.Min(closest, Vector3.Distance(samples[i].Position, samples[j].Position));
            }
        Check(closest > 2f * barrier, $"separate parts of the track are {closest:F0} m apart, more than two barrier distances ({2f * barrier:F0} m)");

        float startM = F1TrackGenerator.StartLineDistance;
        foreach (DrsZone z in RaceRules.Zones)
        {
            Check(z.DetectionM >= 0f && z.EndM < length && z.StartM < z.EndM, "DRS zones lie on the track");
            for (float d = z.StartM; d <= z.EndM; d += 10f)
                Check(Mathf.Abs(track.Spline.GetSampleAtDistance(d).SignedCurvature) < 0.02f, $"DRS zone at {d:F0} m is on a straight, not in a corner");
        }
        // Slot 0 (player) to slot MaxOpponents, 6 m apart (GetStartingGridSlot): every slot on the main straight.
        for (int slot = 0; slot <= RaceSettings.MaxOpponents; slot++)
        {
            float d = startM - 10f - 6f * slot;
            Check(d > 0f && Mathf.Abs(track.Spline.GetSampleAtDistance(d).SignedCurvature) < 0.01f, $"grid slot {slot} ({d:F0} m) is on the main straight");
        }
        var probe = new GameObject("SeamSelfCheck_AIGrid").AddComponent<F1Racing.AI.AIGrid>();
        int ids = probe.identities.Length;
        Object.DestroyImmediate(probe.gameObject);
        Check(ids >= RaceSettings.MaxOpponents, $"{ids} AI identities cover the {RaceSettings.MaxOpponents}-opponent menu cap");
    }

    private static void Check(bool ok, string what)
    {
        if (!ok) throw new System.Exception("[SeamSelfCheck] FAILED: " + what);
    }
}
