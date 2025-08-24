using UnityEngine;
using System.Collections.Generic;

public class HandPoseDriver : MonoBehaviour
{
    [Header("Bones (Left)")]
    public Transform leftHandRoot;
    public Transform l_thumb1, l_thumb2, l_thumb3;
    public Transform l_index1, l_index2, l_index3;
    public Transform l_middle1, l_middle2, l_middle3;
    public Transform l_ring1, l_ring2, l_ring3;
    public Transform l_pinky1, l_pinky2, l_pinky3;

    [Header("Bones (Right)")]
    public Transform rightHandRoot;
    public Transform r_thumb1, r_thumb2, r_thumb3;
    public Transform r_index1, r_index2, r_index3;
    public Transform r_middle1, r_middle2, r_middle3;
    public Transform r_ring1, r_ring2, r_ring3;
    public Transform r_pinky1, r_pinky2, r_pinky3;

    public enum AimAxis { Z, X, Y }
    [Tooltip("본의 '전방축'(세그먼트가 향하는 축)")]
    public AimAxis aimAxis = AimAxis.Z;

    [Header("Temporal smoothing")]
    [Tooltip("간단 보간(값이 클수록 더 부드럽지만 느림)")]
    [Range(0, 1)] public float simpleSmoothing = 0.15f;

    [Tooltip("속도 적응 저역통과(1€ 필터) 사용")]
    public bool useOneEuro = true;
    [Tooltip("1€: 낮을수록 더 부드럽게(지연↑)")]
    public float euroMinCutoff = 1.0f;
    [Tooltip("1€: 속도 민감도")]
    public float euroBeta = 0.30f;
    [Tooltip("1€: 미분용 컷오프")]
    public float euroDCutoff = 1.0f;

    [Header("Speed limiting")]
    [Tooltip("손가락 한 본이 초당 회전할 수 있는 최대 각도(°/s)")]
    public float maxDegPerSec = 360f; // 180~540 권장. 낮추면 더 부드럽게 느림.

    [Header("Limits (deg) — Flexion=+X, Ab/Ad=±Y, Twist=Z")]
    public bool enableAngleLimits = true;
    [Tooltip("클램프를 경계로 서서히 수렴(0=즉시, 1=매우 느리게)")]
    [Range(0f, 1f)] public float clampSoftness = 0.35f;

    // 사람 손 기본 ROM (권장 시작점)
    // 검지~소지
    public float mcpFlexMin = -10f, mcpFlexMax = 90f;
    public float pipFlexMin = 0f, pipFlexMax = 110f;
    public float dipFlexMin = 0f, dipFlexMax = 80f;
    public float mcpAbAdMin = -20f, mcpAbAdMax = 20f;
    public float twistMin = -25f, twistMax = 25f;
    // 엄지
    public float thumbMcpMin = 0f, thumbMcpMax = 55f;
    public float thumbIpMin = 0f, thumbIpMax = 80f;
    public float thumbTwistMin = -20f, thumbTwistMax = 20f;
    public float thumbAbAdMin = -25f, thumbAbAdMax = 25f;

    [Header("Cohesion (neighbor fingers)")]
    public bool enableCohesion = true;
    [Range(0f, 1f)] public float cohesion = 0.45f;

    // ───────── internal state ─────────
    OneEuroFilter _euro; // Vector3용(프로젝트에 이미 존재)
    readonly Dictionary<Transform, Quaternion> restRotL = new();
    readonly Dictionary<Transform, Quaternion> restRotR = new();
    readonly Dictionary<Transform, Vector3> lastDir = new();
    float _lastDt = 1f / 60f;

    Vector3 FromAxis() =>
        aimAxis == AimAxis.Z ? Vector3.forward :
        aimAxis == AimAxis.X ? Vector3.right :
                               Vector3.up;

    void Awake() => RebuildRestMaps();

    public void RebuildRestMaps()
    {
        restRotL.Clear(); restRotR.Clear(); lastDir.Clear();
        foreach (var t in new[]{ l_thumb1,l_thumb2,l_thumb3, l_index1,l_index2,l_index3,
                                 l_middle1,l_middle2,l_middle3, l_ring1,l_ring2,l_ring3,
                                 l_pinky1,l_pinky2,l_pinky3 }) CacheRest(t, restRotL);
        foreach (var t in new[]{ r_thumb1,r_thumb2,r_thumb3, r_index1,r_index2,r_index3,
                                 r_middle1,r_middle2,r_middle3, r_ring1,r_ring2,r_ring3,
                                 r_pinky1,r_pinky2,r_pinky3 }) CacheRest(t, restRotR);
        _euro = new OneEuroFilter(90f, euroMinCutoff, euroBeta, euroDCutoff); // CHI 2012 1€ :contentReference[oaicite:1]{index=1}
    }

    void CacheRest(Transform t, Dictionary<Transform, Quaternion> map)
    {
        if (!t) return;
        map[t] = t.localRotation;
        lastDir[t] = FromAxis(); // 초기 방향
    }

    // IKFromMediapipe에서 LateUpdate에 호출
    public void DriveFromMediapipe(Vector3[] left21, Vector3[] right21, float dt)
    {
        _lastDt = dt <= 0 ? 1f / 60f : dt;
        if (left21 != null && left21.Length >= 21) ApplyOneHand(true, left21, _lastDt);
        if (right21 != null && right21.Length >= 21) ApplyOneHand(false, right21, _lastDt);
    }
    public void ApplyLeft(Vector3[] lm21, float dt) => DriveFromMediapipe(lm21, null, dt);
    public void ApplyRight(Vector3[] lm21, float dt) => DriveFromMediapipe(null, lm21, dt);

    void ApplyOneHand(bool isLeft, Vector3[] lm, float dt)
    {
        // MediaPipe: 0 WRIST, 1-4 THUMB, 5-8 INDEX, 9-12 MIDDLE, 13-16 RING, 17-20 PINKY
        var rest = isLeft ? restRotL : restRotR;

        void DriveSeg(Transform bone, int a, int b)
        {
            if (!bone || bone.parent == null) return;

            // 세그먼트 방향(월드) -> 부모공간 방향
            var worldDir = (lm[b] - lm[a]);
            var parentDir = bone.parent.InverseTransformDirection(worldDir).normalized;

            // 입력 방향 스무딩(1€ or 간단 보간)
            var key = bone;
            Vector3 targetDir;
            if (useOneEuro && _euro != null) targetDir = _euro.Filter(parentDir, dt);
            else targetDir = Vector3.Slerp(lastDir[key], parentDir, 1f - simpleSmoothing);
            lastDir[key] = targetDir;
            // ❶ 1° 미만 변화는 무시(데드존)
            const float smallAngleDeadzone = 1.0f;
            if (Vector3.Angle(lastDir[key], parentDir) < smallAngleDeadzone)
                parentDir = lastDir[key];

            // 목표 회전(로컬) = (축 정렬 회전) * rest
            var fromAxis = FromAxis();
            var targetLocal = Quaternion.FromToRotation(fromAxis, targetDir) * rest[bone];

            // ★ 속도 제한: 현재 → 목표 로컬회전을 maxDegPerSec로만 접근
            var current = bone.localRotation;
            float maxStep = Mathf.Max(1f, maxDegPerSec) * dt; // °/s → °/frame
            bone.localRotation = Quaternion.RotateTowards(current, targetLocal, maxStep); // 오버슈트 없음 :contentReference[oaicite:2]{index=2}
        }

        // Thumb
        DriveSeg(isLeft ? l_thumb1 : r_thumb1, 1, 2);
        DriveSeg(isLeft ? l_thumb2 : r_thumb2, 2, 3);
        DriveSeg(isLeft ? l_thumb3 : r_thumb3, 3, 4);
        // Index
        DriveSeg(isLeft ? l_index1 : r_index1, 5, 6);
        DriveSeg(isLeft ? l_index2 : r_index2, 6, 7);
        DriveSeg(isLeft ? l_index3 : r_index3, 7, 8);
        // Middle
        DriveSeg(isLeft ? l_middle1 : r_middle1, 9, 10);
        DriveSeg(isLeft ? l_middle2 : r_middle2, 10, 11);
        DriveSeg(isLeft ? l_middle3 : r_middle3, 11, 12);
        // Ring
        DriveSeg(isLeft ? l_ring1 : r_ring1, 13, 14);
        DriveSeg(isLeft ? l_ring2 : r_ring2, 14, 15);
        DriveSeg(isLeft ? l_ring3 : r_ring3, 15, 16);
        // Pinky
        DriveSeg(isLeft ? l_pinky1 : r_pinky1, 17, 18);
        DriveSeg(isLeft ? l_pinky2 : r_pinky2, 18, 19);
        DriveSeg(isLeft ? l_pinky3 : r_pinky3, 19, 20);

        // 최종 후처리: 코히전 + 각도 제한(부드러운 클램프)
        if (enableAngleLimits) PostProcessFingers(isLeft);
    }

    // ───── 코히전 + 각도 제한(소프트 클램프) ─────
    void PostProcessFingers(bool isLeft)
    {
        // MCP 벌어짐(Y) 코히전
        if (enableCohesion)
        {
            var mcp = new Transform[]{
                isLeft ? l_index1 : r_index1,
                isLeft ? l_middle1: r_middle1,
                isLeft ? l_ring1  : r_ring1,
                isLeft ? l_pinky1 : r_pinky1
            };
            if (AllValid(mcp))
            {
                float avgY = 0f; foreach (var t in mcp) avgY += Mathf.DeltaAngle(0, t.localEulerAngles.y);
                avgY /= mcp.Length;
                foreach (var t in mcp)
                {
                    var e = t.localEulerAngles;
                    float y = Mathf.DeltaAngle(0, e.y);
                    float tgt = Mathf.Lerp(y, Mathf.Clamp(avgY, mcpAbAdMin, mcpAbAdMax), cohesion);
                    e.y = Wrap180To360(tgt);
                    t.localEulerAngles = e;
                }
            }
        }

        // ROM 제한(엄지)
        SoftClampAll(isLeft ? l_thumb1 : r_thumb1, thumbMcpMin, thumbMcpMax, thumbAbAdMin, thumbAbAdMax, thumbTwistMin, thumbTwistMax);
        SoftClampAll(isLeft ? l_thumb2 : r_thumb2, thumbIpMin, thumbIpMax, -10f, 10f, thumbTwistMin, thumbTwistMax);
        SoftClampAll(isLeft ? l_thumb3 : r_thumb3, 0f, 0f, -5f, 5f, -10f, 10f);

        // ROM 제한(검지~소지)
        SoftClampAll(isLeft ? l_index1 : r_index1, mcpFlexMin, mcpFlexMax, mcpAbAdMin, mcpAbAdMax, twistMin, twistMax);
        SoftClampAll(isLeft ? l_index2 : r_index2, pipFlexMin, pipFlexMax, -5f, 5f, twistMin, twistMax);
        SoftClampAll(isLeft ? l_index3 : r_index3, dipFlexMin, dipFlexMax, -5f, 5f, twistMin, twistMax);

        SoftClampAll(isLeft ? l_middle1 : r_middle1, mcpFlexMin, mcpFlexMax, mcpAbAdMin, mcpAbAdMax, twistMin, twistMax);
        SoftClampAll(isLeft ? l_middle2 : r_middle2, pipFlexMin, pipFlexMax, -5f, 5f, twistMin, twistMax);
        SoftClampAll(isLeft ? l_middle3 : r_middle3, dipFlexMin, dipFlexMax, -5f, 5f, twistMin, twistMax);

        SoftClampAll(isLeft ? l_ring1 : r_ring1, mcpFlexMin, mcpFlexMax, mcpAbAdMin, mcpAbAdMax, twistMin, twistMax);
        SoftClampAll(isLeft ? l_ring2 : r_ring2, pipFlexMin, pipFlexMax, -5f, 5f, twistMin, twistMax);
        SoftClampAll(isLeft ? l_ring3 : r_ring3, dipFlexMin, dipFlexMax, -5f, 5f, twistMin, twistMax);

        SoftClampAll(isLeft ? l_pinky1 : r_pinky1, mcpFlexMin, mcpFlexMax, mcpAbAdMin, mcpAbAdMax, twistMin, twistMax);
        SoftClampAll(isLeft ? l_pinky2 : r_pinky2, pipFlexMin, pipFlexMax, -5f, 5f, twistMin, twistMax);
        SoftClampAll(isLeft ? l_pinky3 : r_pinky3, dipFlexMin, dipFlexMax, -5f, 5f, twistMin, twistMax);
    }

    // ───── helpers ─────
    static bool AllValid(Transform[] arr) { foreach (var t in arr) if (!t) return false; return true; }

    // 부드러운 클램프: 각도를 [min,max]로 시간기반 수렴
    void SoftClampAll(Transform t, float xMin, float xMax, float yMin, float yMax, float zMin, float zMax)
    {
        if (!t) return;
        var e = t.localEulerAngles;
        float x = Mathf.DeltaAngle(0, e.x);
        float y = Mathf.DeltaAngle(0, e.y);
        float z = Mathf.DeltaAngle(0, e.z);

        x = SoftClampDeg(x, xMin, xMax, clampSoftness, _lastDt);
        y = SoftClampDeg(y, yMin, yMax, clampSoftness, _lastDt);
        z = SoftClampDeg(z, zMin, zMax, clampSoftness, _lastDt);

        e.x = Wrap180To360(x); e.y = Wrap180To360(y); e.z = Wrap180To360(z);
        t.localEulerAngles = e;
    }
    static float SoftClampDeg(float v, float min, float max, float softness, float dt)
    {
        if (v < min) return Mathf.Lerp(v, min, 1f - Mathf.Pow(1f - Mathf.Clamp01(1f - softness), Mathf.Clamp01(dt * 60f)));
        if (v > max) return Mathf.Lerp(v, max, 1f - Mathf.Pow(1f - Mathf.Clamp01(1f - softness), Mathf.Clamp01(dt * 60f)));
        return v;
    }
    static float Wrap180To360(float a) => Mathf.Repeat(a + 360f, 360f);
}
