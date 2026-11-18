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

        Debug.Log("[SeamSelfCheck] all checks passed");
    }

    private static void Check(bool ok, string what)
    {
        if (!ok) throw new System.Exception("[SeamSelfCheck] FAILED: " + what);
    }
}
