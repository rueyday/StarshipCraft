using UnityEngine;

// First-order intercept math for the lead reticle: where to aim so a bolt of
// the given speed meets a moving target. Work in the shooter's frame — bolts
// inherit the shooter's velocity, so only relative motion matters.
public static class Targeting
{
    // relPos = target - shooter, relVel = targetVel - shooterVel.
    // On success, time is the flight time; aim at target + relVel * time.
    public static bool Lead(Vector3 relPos, Vector3 relVel, float boltSpeed, out float time)
    {
        float a = Vector3.Dot(relVel, relVel) - boltSpeed * boltSpeed;
        float b = 2f * Vector3.Dot(relPos, relVel);
        float c = Vector3.Dot(relPos, relPos);
        time = 0f;

        if (Mathf.Abs(a) < 0.001f) // bolt barely faster than target: linear case
        {
            if (Mathf.Abs(b) < 0.001f) return false;
            time = -c / b;
            return time > 0f;
        }

        float disc = b * b - 4f * a * c;
        if (disc < 0f) return false;
        float sq = Mathf.Sqrt(disc);
        float t1 = (-b - sq) / (2f * a);
        float t2 = (-b + sq) / (2f * a);
        time = Mathf.Min(t1, t2);
        if (time < 0f) time = Mathf.Max(t1, t2);
        return time > 0f;
    }
}
