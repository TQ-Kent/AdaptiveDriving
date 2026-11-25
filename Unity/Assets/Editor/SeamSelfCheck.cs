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
        var tactics = new TacticalDriver();
        Check(tactics.Decide(0f, 6f, false, false, -1f, -1f, 0.5f) == -1f, "both lanes blocked: follow in the current lane");
        tactics = new TacticalDriver();
        Check(tactics.Decide(0f, 6f, false, true, -1f, 0f, 0.5f) == 1f, "preferred side blocked: take the free one");
        Check(tactics.Decide(0.5f, 6f, true, false, -1f, 1f, 0.5f) != 1f, "chosen lane closes: abort the move");
        tactics = new TacticalDriver();
        Check(tactics.Decide(0f, 100f, true, true, 1f, 0f, 0.5f) == 0f, "clear road: centre line");

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
        CheckSpeedCap();
        CheckCarPhysics();
        CheckRules();
        CheckTrack();

        // Corner memory: 100 bins of 5 m. Learning at 300 m trims the approach (260-310 m), floors at MinTrim, leaves the rest.
        var corners = new CornerMemory(100, 500f);
        Check(corners.Learn(300f, 0.05f) && Mathf.Approximately(corners.Trim[60], 0.95f) && Mathf.Approximately(corners.Trim[52], 0.95f)
              && corners.Trim[51] == 1f && corners.Trim[63] == 1f, "corner memory trims the corner and its approach only");
        for (int i = 0; i < 20; i++) corners.Learn(300f, 0.05f);
        Check(Mathf.Approximately(corners.Trim[60], CornerMemory.MinTrim) && !corners.Learn(300f, 0.05f), "corner trim stops at its floor");
        for (int i = 0; i < 30; i++) corners.Recover(0.01f);
        Check(corners.Trim[60] == 1f && !corners.Recover(0.01f), "trims recover lap by lap back to the plan, then stop");
        Check(PathFollower.SteerDemand(new Vector3(8f, 0f, 4f), 3f, 5f) > 1f, "a target beyond full lock reports steer demand above 1 (understeer)");

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
