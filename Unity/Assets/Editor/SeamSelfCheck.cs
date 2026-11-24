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

        Debug.Log("[SeamSelfCheck] all checks passed");
    }

    private static void Check(bool ok, string what)
    {
        if (!ok) throw new System.Exception("[SeamSelfCheck] FAILED: " + what);
    }
}
