using UnityEngine;

public class OneEuroFilter
{
    float freq, minCutoff, beta, dCutoff;
    Vector3 xPrev, dxPrev;
    bool hasPrev;
    float Alpha(float cutoff) => 1f / (1f + (freq / (2f * Mathf.PI * cutoff)));
    public OneEuroFilter(float freq = 90f, float minCutoff = 1.0f, float beta = 0.0f, float dCutoff = 1.0f)
    {
        this.freq = freq; this.minCutoff = minCutoff; this.beta = beta; this.dCutoff = dCutoff;
    }
    public Vector3 Filter(Vector3 x, float dt)
    {
        freq = 1f / Mathf.Max(1e-6f, dt);
        if (!hasPrev) { xPrev = x; dxPrev = Vector3.zero; hasPrev = true; return x; }
        var dx = (x - xPrev) * freq;
        var aD = Alpha(dCutoff);
        dxPrev = Vector3.Lerp(dxPrev, dx, aD);
        var cutoff = minCutoff + beta * dxPrev.magnitude;
        var a = Alpha(cutoff);
        xPrev = Vector3.Lerp(xPrev, x, a);
        return xPrev;
    }
}