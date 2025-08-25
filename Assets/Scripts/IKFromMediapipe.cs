using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Animations.Rigging;
#if UNITY_EDITOR
using UnityEditor;
#endif

[System.Serializable] public class LM { public string name; public float x, y, z; public float visibility; }
[System.Serializable] public class Frame { public LM[] left_hand; public LM[] right_hand; public LM[] pose; }
[System.Serializable] public class SegmentRoot { public string video, gloss_id, source; public int occurrence; public float start, end, fps_sampled; public List<Frame> frames; }

public class IKFromMediapipe : MonoBehaviour
{
    [Header("JSON (Drag & Drop here)")]
    [Tooltip("여기에 .json(TextAsset)을 드래그하면 그 파일을 우선 사용합니다.")]
    public TextAsset jsonAsset;

#if UNITY_EDITOR
    [SerializeField, Tooltip("StreamingAssets 안의 파일을 드래그(에디터 전용). 드래그하면 jsonFileName이 자동 설정됩니다.")]
    private UnityEngine.Object streamingJson;
#endif

    [Header("Inputs (fallback when no TextAsset)")]
    [Tooltip("TextAsset이 없을 때 사용할 StreamingAssets 상대 파일명(서브폴더 포함 가능). 예) \"Sub/clip.json\"")]
    public string jsonFileName = "VXPAKOKS240592420_공부1.json";
    public Camera drivingCamera;

    [Header("IK Targets (from your Rig)")]
    public Transform leftTarget, leftHint;
    public Transform rightTarget, rightHint;

    [Header("TwoBoneIK (optional)")]
    public TwoBoneIKConstraint leftIK, rightIK;

    [Header("Depth (m from camera)")]
    [Min(0.01f)] public float wristDepth = 1.8f;
    [Min(0.01f)] public float elbowDepth = 1.8f;
    [Min(0f)] public float elbowOutOfPlane = 0.07f;

    [Header("Playback")]
    [Min(0.1f)] public float fps = 30f;
    [Tooltip("재생 속도 배율 (1=실시간)")] public float playbackSpeed = 1.0f;

    [Header("Frame interpolation")]
    [Tooltip("프레임 사이 좌표를 선형 보간해 부드럽게 움직임")]
    public bool interpolateFrames = true;

    [Header("Hands (optional)")]
    public HandPoseDriver handDriver;
    public bool driveLeftHand = true, driveRightHand = true;

    [Header("Wrist mapping")]
    [Tooltip("타깃 위치는 pose의 LEFT/RIGHT_WRIST 우선, 없으면 hands의 WRIST 사용")]
    public bool preferPoseWristForTargets = true;
    public Vector3 leftWristEulerOffset = Vector3.zero, rightWristEulerOffset = Vector3.zero;

    [Header("Palm / Back-of-hand")]
    [Tooltip("체크하면 손등이 보이도록(손바닥 법선을 180° 반전)")]
    public bool leftShowsBackOfHand = true;
    public bool rightShowsBackOfHand = true;

    [Header("Wrist roll (deg, about local forward)")]
    [Tooltip("손목 로컬 forward 축을 기준으로 추가 롤(°). 손등을 위로: 180")]
    public float leftWristRollDeg = 180f;
    public float rightWristRollDeg = 180f;

    [Header("Extra rotation smoothing")]
    [Tooltip("손목 회전에 추가 스무딩(Slerp) 적용")]
    public bool smoothWristRotation = true;
    [Range(0f, 1f)] public float wristRotSlerp = 0.2f;

    [Header("Arm separation (shoulder-to-shoulder axis)")]
    [Tooltip("어깨-어깨 축을 기준으로 손목 타깃을 좌우로 밀어 팔 간격을 조절")]
    public bool applyArmSeparation = true;
    [Tooltip("각 팔을 바깥쪽으로 이동시키는 오프셋(미터). 왼팔 +offset, 오른팔 -offset")]
    [Min(0f)] public float separationOffset = 0.05f;
    [Tooltip("원한다면 목표 손목 간격(미터)을 지정해 자동으로 맞춤")]
    public bool useDesiredHandGap = false;
    [Min(0f)] public float desiredHandGap = 0.40f;

    [Header("Stabilize (One Euro Filter, Vector3용)")]
    [Tooltip("왼팔 타깃/힌트도 필터링")]
    public bool stabilizeLeftArm = false;
    [Tooltip("오른팔 타깃/힌트 필터링")]
    public bool stabilizeRightArm = true;
    [Range(0.1f, 10f)] public float posMinCutoff = 1.5f;
    [Range(0f, 1f)] public float posBeta = 0.03f;
    [Range(0.1f, 10f)] public float hintMinCutoff = 1.5f;
    [Range(0f, 1f)] public float hintBeta = 0.03f;

    [Header("Mapping stability")]
    [Tooltip("창 크기/줌 변화와 무관하게 동일한 투영으로 좌표를 만들기")]
    public bool lockMappingToStartCamera = true;

    [Header("Debug")]
    public bool logTargetMotion = false;
    public bool drawGizmos = true;

    // 내부 참조값(시작 시 복사)
    float _refFovDeg, _refAspect;

    // MediaPipe Hands 21개 표준 이름
    static readonly string[] HAND21 = {
        "WRIST",
        "THUMB_CMC","THUMB_MCP","THUMB_IP","THUMB_TIP",
        "INDEX_FINGER_MCP","INDEX_FINGER_PIP","INDEX_FINGER_DIP","INDEX_FINGER_TIP",
        "MIDDLE_FINGER_MCP","MIDDLE_FINGER_PIP","MIDDLE_FINGER_DIP","MIDDLE_FINGER_TIP",
        "RING_FINGER_MCP","RING_FINGER_PIP","RING_FINGER_DIP","RING_FINGER_TIP",
        "PINKY_MCP","PINKY_PIP","PINKY_DIP","PINKY_TIP"
    };

    List<Frame> frames; int fi = 0; float t = 0;
    Vector3 prevLT, prevRT;

    // 손가락은 LateUpdate에서 덮어쓰기 → 캐시
    Vector3[] _cachedLeft21, _cachedRight21;

    // Vector3 전용 OneEuroFilter
    OneEuroFilter leftPosF, rightPosF, leftHintF, rightHintF;

    // 엘보 노멀 연속성
    Vector3 _prevLeftN = Vector3.zero, _prevRightN = Vector3.zero;

    // 손바닥 법선 연속성
    Vector3 _prevPalmN_L = Vector3.zero, _prevPalmN_R = Vector3.zero;

    // 회전 스무딩 캐시
    Quaternion _prevLeftRot, _prevRightRot;
    bool _hasPrevLeftRot = false, _hasPrevRightRot = false;

    IEnumerator Start()
    {
        if (!drivingCamera) drivingCamera = Camera.main;
        AutoFixAudioListener();
        SyncIKWithTargets(); // IK 세팅/오프셋 해제

        // 시작 시 카메라의 FOV/Aspect를 고정값으로 저장
        if (lockMappingToStartCamera && drivingCamera)
        {
            _refFovDeg = drivingCamera.fieldOfView;
            _refAspect = drivingCamera.aspect;
        }

        // 1) TextAsset → 2) ?json= URL → 3) StreamingAssets/jsonFileName
        string sourceUrl = GetQuery("json");
        string json = null;

        if (jsonAsset != null)
        {
            json = jsonAsset.text;
        }
        else if (!string.IsNullOrEmpty(sourceUrl))
        {
            using var req = UnityWebRequest.Get(sourceUrl);
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success) { Debug.LogError(req.error); yield break; }
            json = req.downloadHandler.text;
        }
        else
        {
            // StreamingAssets 폴백
            string local = System.IO.Path.Combine(Application.streamingAssetsPath, jsonFileName);
#if !UNITY_ANDROID || UNITY_EDITOR
            if (!local.StartsWith("file://")) local = "file://" + local; // 데스크톱/에디터: file:// 보장
#endif
            using var req = UnityWebRequest.Get(local);
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success) { Debug.LogError(req.error); yield break; }
            json = req.downloadHandler.text;
        }

        // 파싱
        if (!ParseAndApply(json)) yield break;

        // 필터 준비
        if (stabilizeLeftArm)
        {
            leftPosF = new OneEuroFilter(90f, posMinCutoff, posBeta, 1f);
            leftHintF = new OneEuroFilter(90f, hintMinCutoff, hintBeta, 1f);
        }
        if (stabilizeRightArm)
        {
            rightPosF = new OneEuroFilter(90f, posMinCutoff, posBeta, 1f);
            rightHintF = new OneEuroFilter(90f, hintMinCutoff, hintBeta, 1f);
        }

        if (handDriver) handDriver.RebuildRestMaps();

        prevLT = leftTarget ? leftTarget.position : Vector3.zero;
        prevRT = rightTarget ? rightTarget.position : Vector3.zero;
    }

    void Update()
    {
        if (frames == null || frames.Count == 0 || !drivingCamera) return;

        // 재생 인덱스 (속도조절)
        t += Time.deltaTime * Mathf.Max(0.01f, playbackSpeed);
        float step = 1f / Mathf.Max(1f, fps);
        while (t >= step) { t -= step; fi = (fi + 1) % frames.Count; }

        // 이웃 프레임 & 보간계수
        int iA = fi;
        int iB = (iA + 1) % frames.Count;
        float alpha = interpolateFrames ? Mathf.Clamp01(t / step) : 0f;

        var fA = frames[iA];
        var fB = frames[iB];

        // 보간된 입력 만들기
        LM[] leftHand  = BlendLMsByName(fA.left_hand,  fB.left_hand,  alpha);
        LM[] rightHand = BlendLMsByName(fA.right_hand, fB.right_hand, alpha);
        LM[] pose      = BlendLMsByName(fA.pose,       fB.pose,       alpha);

        var LmL = ToMapCI(leftHand);
        var LmR = ToMapCI(rightHand);
        var Pose = ToMapCI(pose);

        // 21점 캐시 (손목 회전/손가락용) — 보간된 핸드 사용
        _cachedLeft21  = driveLeftHand  ? Build21ByNames(leftHand,  wristDepth) : null;
        _cachedRight21 = driveRightHand ? Build21ByNames(rightHand, wristDepth) : null;

        float dt = Time.deltaTime;

        // ── 손목 타깃 "원본" 위치(필터 적용 전)
        Vector3? baseL = TryGetWristWorld(Pose, LmL, true);
        Vector3? baseR = TryGetWristWorld(Pose, LmR, false);

        // ── 팔 간격 보정(어깨-어깨 축 기준) — 양손 hand가 실제 있을 때만
        if (applyArmSeparation &&
            baseL.HasValue && baseR.HasValue &&
            _cachedLeft21 != null && _cachedRight21 != null &&
            Pose.TryGetValue("LEFT_SHOULDER", out var ls) &&
            Pose.TryGetValue("RIGHT_SHOULDER", out var rs))
        {
            Vector3 shoulderL = ToWorld(drivingCamera, ls.x, ls.y, elbowDepth);
            Vector3 shoulderR = ToWorld(drivingCamera, rs.x, rs.y, elbowDepth);
            Vector3 axisLR = (shoulderL - shoulderR).normalized;

            if (separationOffset > 0f) {
                baseL = baseL.Value + axisLR * separationOffset;
                baseR = baseR.Value - axisLR * separationOffset;
            }
            if (useDesiredHandGap) {
                float currGap = Mathf.Abs(Vector3.Dot((baseL.Value - baseR.Value), axisLR));
                float delta = desiredHandGap - currGap;
                if (Mathf.Abs(delta) > 1e-4f) {
                    baseL = baseL.Value + axisLR * (0.5f * delta);
                    baseR = baseR.Value - axisLR * (0.5f * delta);
                }
            }
        }

        // ── 위치 필터링 + 적용
        if (leftTarget && baseL.HasValue)
        {
            var p = baseL.Value;
            if (stabilizeLeftArm && leftPosF != null) p = leftPosF.Filter(p, dt);
            leftTarget.position = p;
        }
        if (rightTarget && baseR.HasValue)
        {
            var p = baseR.Value;
            if (stabilizeRightArm && rightPosF != null) p = rightPosF.Filter(p, dt);
            rightTarget.position = p;
        }

        // ── 손목 타깃 회전 (보간된 21점 + 선택적 스무딩)
        if (leftTarget && _cachedLeft21 != null)
        {
            var rotL = ComputeWristRotationFrom21(_cachedLeft21,  true,  leftShowsBackOfHand,  ref _prevPalmN_L)
         * Quaternion.Euler(leftWristEulerOffset);
            if (Mathf.Abs(leftWristRollDeg) > 0.001f)
                rotL = Quaternion.AngleAxis(leftWristRollDeg, rotL * Vector3.forward) * rotL;

            if (smoothWristRotation)
            {
                if (!_hasPrevLeftRot) { _prevLeftRot = rotL; _hasPrevLeftRot = true; }
                else rotL = Quaternion.Slerp(_prevLeftRot, rotL, wristRotSlerp);
                _prevLeftRot = rotL;
            }
            leftTarget.rotation = rotL;
        }
        if (rightTarget && _cachedRight21 != null)
        {
            var rotR = ComputeWristRotationFrom21(_cachedRight21, false, rightShowsBackOfHand, ref _prevPalmN_R)
         * Quaternion.Euler(rightWristEulerOffset);
            if (Mathf.Abs(rightWristRollDeg) > 0.001f)
                rotR = Quaternion.AngleAxis(rightWristRollDeg, rotR * Vector3.forward) * rotR;

            if (smoothWristRotation)
            {
                if (!_hasPrevRightRot) { _prevRightRot = rotR; _hasPrevRightRot = true; }
                else rotR = Quaternion.Slerp(_prevRightRot, rotR, wristRotSlerp);
                _prevRightRot = rotR;
            }
            rightTarget.rotation = rotR;
        }

        // ── 팔꿈치 힌트 (필터링 + 플립 방지)
        if (leftHint && Pose.TryGetValue("LEFT_SHOULDER", out var ls2) &&
                        Pose.TryGetValue("LEFT_ELBOW", out var le) &&
                        Pose.TryGetValue("LEFT_WRIST", out var lwPose))
        {
            var hint = ComputeElbowHintStable(true, new Vector2(ls2.x, ls2.y), new Vector2(le.x, le.y), new Vector2(lwPose.x, lwPose.y));
            if (stabilizeLeftArm && leftHintF != null) hint = leftHintF.Filter(hint, dt);
            leftHint.position = hint;
        }
        if (rightHint && Pose.TryGetValue("RIGHT_SHOULDER", out var rs2) &&
                         Pose.TryGetValue("RIGHT_ELBOW", out var re) &&
                         Pose.TryGetValue("RIGHT_WRIST", out var rwPose))
        {
            var hint = ComputeElbowHintStable(false, new Vector2(rs2.x, rs2.y), new Vector2(re.x, re.y), new Vector2(rwPose.x, rwPose.y));
            if (stabilizeRightArm && rightHintF != null) hint = rightHintF.Filter(hint, dt);
            rightHint.position = hint;
        }

        // ── 디버그
        if (logTargetMotion)
        {
            if (leftTarget) { float d = Vector3.Distance(prevLT, leftTarget.position); if (d > 1e-4f) Debug.Log($"[IK] LeftTarget {d:F3} m"); prevLT = leftTarget.position; }
            if (rightTarget) { float d = Vector3.Distance(prevRT, rightTarget.position); if (d > 1e-4f) Debug.Log($"[IK] RightTarget {d:F3} m"); prevRT = rightTarget.position; }
        }
    }

    // 손가락은 애니메이터/리그 평가 이후에 덮어쓰기
    void LateUpdate()
    {
        if (!handDriver) return;
        if (_cachedLeft21 != null || _cachedRight21 != null)
            handDriver.DriveFromMediapipe(_cachedLeft21, _cachedRight21, Time.deltaTime);
    }

    // WebGL 템플릿/JS/웹소켓에서 문자열 JSON을 바로 넣고 싶을 때 사용
    public void LoadFromJsonText(string json)
    {
        if (!ParseAndApply(json)) return;
        if (handDriver) handDriver.RebuildRestMaps();
        fi = 0; t = 0;
        Debug.Log($"[IK] JSON loaded via string: {frames.Count} frames");
    }
    public void LoadFromJsonTextJS(string json) => LoadFromJsonText(json); // SendMessage용

    // ───────────────── Wrist rotation from 21 points ─────────────────
    Quaternion ComputeWristRotationFrom21(Vector3[] mpWorld, bool isLeft, bool showBackOfHand, ref Vector3 prevN)
    {
        // MediaPipe: 0 WRIST, 5 INDEX_MCP, 9 MIDDLE_MCP, 17 PINKY_MCP
        Vector3 wrist = mpWorld[0];
        Vector3 idx   = mpWorld[5];
        Vector3 mid   = mpWorld[9];
        Vector3 pnk   = mpWorld[17];

        Vector3 across  = (idx - pnk).normalized;       // 손바닥 가로축
        if (isLeft) across = -across;                   // 좌/우 보정
        Vector3 forward = (mid - wrist).normalized;     // 손바닥 앞축
        Vector3 normal  = Vector3.Cross(across, forward);

        // 퇴화(손 모아지거나 직선에 가까움) → 이전 법선 사용
        if (normal.sqrMagnitude < 1e-8f)
            normal = (prevN.sqrMagnitude > 0.0001f) ? prevN : Vector3.up;
        else
            normal.Normalize();

        if (showBackOfHand) normal = -normal;

        // ★ 법선 부호 연속성(플립 방지)
        if (prevN.sqrMagnitude > 0.0001f && Vector3.Dot(prevN, normal) < 0f)
            normal = -normal;

        // 약간의 관성(부드럽게 따라옴)
        prevN = (prevN.sqrMagnitude < 0.0001f) ? normal : Vector3.Slerp(prevN, normal, 0.5f);

        return Quaternion.LookRotation(forward, normal); // Z=forward, Y=normal
    }

    // ───────────────── 보간 유틸 ─────────────────
    LM[] BlendLMsByName(LM[] A, LM[] B, float alpha)
    {
        if (A == null && B == null) return null;

        var a = new Dictionary<string, LM>(System.StringComparer.OrdinalIgnoreCase);
        var b = new Dictionary<string, LM>(System.StringComparer.OrdinalIgnoreCase);
        if (A != null) foreach (var lm in A) if (lm != null && !string.IsNullOrEmpty(lm.name) && !a.ContainsKey(lm.name)) a[lm.name] = lm;
        if (B != null) foreach (var lm in B) if (lm != null && !string.IsNullOrEmpty(lm.name) && !b.ContainsKey(lm.name)) b[lm.name] = lm;

        // 키 합집합으로 결과 구성
        var keys = new HashSet<string>(a.Keys, System.StringComparer.OrdinalIgnoreCase);
        keys.UnionWith(b.Keys);

        var outList = new List<LM>(keys.Count);
        foreach (var k in keys)
        {
            a.TryGetValue(k, out var la);
            b.TryGetValue(k, out var lb);

            // 둘 다 있으면 보간, 한쪽만 있으면 그 값 사용
            float x = (la != null && lb != null) ? Mathf.Lerp(la.x, lb.x, alpha) : (la != null ? la.x : lb.x);
            float y = (la != null && lb != null) ? Mathf.Lerp(la.y, lb.y, alpha) : (la != null ? la.y : lb.y);
            float z = (la != null && lb != null) ? Mathf.Lerp(la.z, lb.z, alpha) : (la != null ? la.z : lb.z);
            float vis = (la != null && lb != null) ? Mathf.Lerp(la.visibility, lb.visibility, alpha) : (la != null ? la.visibility : lb.visibility);

            outList.Add(new LM { name = k, x = x, y = y, z = z, visibility = vis });
        }
        return outList.ToArray();
    }

    Vector3? TryGetWristWorld(Dictionary<string, LM> pose, Dictionary<string, LM> hand, bool left)
    {
        LM hWrist = null; // 미리 선언
        bool hasHand = (hand != null) && hand.TryGetValue("WRIST", out hWrist);

        // hand 데이터가 있을 때만 pose 손목을 우선 사용
        if (preferPoseWristForTargets && hasHand)
        {
            string k = left ? "LEFT_WRIST" : "RIGHT_WRIST";
            if (pose != null && pose.TryGetValue(k, out var p))
                return ToWorld(drivingCamera, p.x, p.y, wristDepth);
        }

        if (hasHand) return ToWorld(drivingCamera, hWrist.x, hWrist.y, wristDepth);

        // hand도 없고 pose만 있으면 해당 팔은 움직이지 않음
        return null;
    }

    // ───────────────── Helpers ─────────────────
    bool ParseAndApply(string json)
    {
        if (string.IsNullOrEmpty(json)) { Debug.LogError("Empty JSON"); return false; }

        try
        {
            char first = json.SkipWhile(char.IsWhiteSpace).FirstOrDefault();
            if (first == '[')
            {
                frames = JsonConvert.DeserializeObject<List<Frame>>(json);
            }
            else if (first == '{')
            {
                var r = JsonConvert.DeserializeObject<SegmentRoot>(json);
                frames = r?.frames ?? new List<Frame>();
                if (r != null && r.fps_sampled > 0) fps = r.fps_sampled;
            }
            else
            {
                var jo = JObject.Parse(json);
                frames = jo["frames"]?.ToObject<List<Frame>>() ?? new List<Frame>();
                float f = jo.Value<float?>("fps_sampled") ?? 0f;
                if (f > 0) fps = f;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"JSON parse failed: {e.Message}");
            return false;
        }

        if (frames == null || frames.Count == 0) { Debug.LogError("No frames in JSON"); return false; }
        return true;
    }

    Dictionary<string, LM> ToMapCI(LM[] lms)
    {
        var d = new Dictionary<string, LM>(System.StringComparer.OrdinalIgnoreCase);
        if (lms == null) return d;
        foreach (var p in lms) { if (p == null) continue; var k = p.name ?? ""; if (!d.ContainsKey(k)) d[k] = p; }
        return d;
    }

    Vector3 ToWorld(Camera cam, float nx, float ny, float z)
    {
        // 입력은 [0,1] 기준, (0,0)=좌상 가정 → y 뒤집기
        nx = Mathf.Clamp01(nx);
        ny = Mathf.Clamp01(ny);

        // NDC(-1~1)로 변환
        float u =  2f * nx - 1f;
        float v =  1f - 2f * ny;  // 위가 +로 가도록 뒤집음

        // 사용할 FOV/Aspect 결정: 잠금이면 참조값, 아니면 현재 카메라
        float fovDeg = (lockMappingToStartCamera ? _refFovDeg : cam.fieldOfView);
        float aspect = (lockMappingToStartCamera ? _refAspect : cam.aspect);

        // 퍼스펙티브 가정
        float fovRad = fovDeg * Mathf.Deg2Rad;
        float tanHalf = Mathf.Tan(fovRad * 0.5f);

        // 카메라(뷰) 공간의 방향 벡터 구성
        Vector3 dirCam = new Vector3(u * aspect * tanHalf, v * tanHalf, 1f);

        // z(카메라 전방 거리) 평면과의 교차점
        float dist = Mathf.Max(0.01f, z);
        float t = dist / Mathf.Max(1e-6f, dirCam.z);
        Vector3 pCam = dirCam * t;

        // 월드로 변환
        return cam.transform.TransformPoint(pCam);
    }

    Vector3[] Build21ByNames(LM[] hand, float z)
    {
        if (hand == null) return null;
        var map = new Dictionary<string, LM>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var lm in hand) if (lm != null && !string.IsNullOrEmpty(lm.name) && !map.ContainsKey(lm.name)) map[lm.name] = lm;
        var out21 = new Vector3[21];
        for (int i = 0; i < 21; i++) { if (!map.TryGetValue(HAND21[i], out var lm)) return null; out21[i] = ToWorld(drivingCamera, lm.x, lm.y, z); }
        return out21;
    }

    Vector3 ComputeElbowHintStable(bool isLeft, Vector2 nS, Vector2 nE, Vector2 nW)
    {
        Vector3 S = ToWorld(drivingCamera, nS.x, nS.y, elbowDepth);
        Vector3 E = ToWorld(drivingCamera, nE.x, nE.y, elbowDepth);
        Vector3 W = ToWorld(drivingCamera, nW.x, nW.y, elbowDepth);

        Vector3 up = (E - S).normalized;
        Vector3 low = (W - E).normalized;
        float ang = Vector3.Angle(up, low);

        if (ang < 8f)
        { // 거의 일직선이면 직전 노멀 유지(플립 방지)
            var prevN = isLeft ? _prevLeftN : _prevRightN;
            if (prevN.sqrMagnitude > 0.0001f) return E + prevN.normalized * elbowOutOfPlane;
        }
        Vector3 n = Vector3.Cross(up, low).normalized;
        var prev = isLeft ? _prevLeftN : _prevRightN;
        if (prev.sqrMagnitude > 0.0001f && Vector3.Dot(prev, n) < 0f) n = -n;
        if (isLeft) _prevLeftN = n; else _prevRightN = n;

        return E + n * elbowOutOfPlane;
    }

    void SyncIKWithTargets()
    {
        if (leftIK)
        {
            var d = leftIK.data; if (leftTarget) d.target = leftTarget; if (leftHint) d.hint = leftHint;
            //d.maintainTargetPositionOffset = false; d.maintainTargetRotationOffset = false;
            //d.targetPositionWeight = 1; d.targetRotationWeight = 1; d.hintWeight = 1;
            leftIK.data = d;
        }
        if (rightIK)
        {
            var d = rightIK.data; if (rightTarget) d.target = rightTarget; if (rightHint) d.hint = rightHint;
            //d.maintainTargetPositionOffset = false; d.maintainTargetRotationOffset = false;
            //d.targetPositionWeight = 1; d.targetRotationWeight = 1; d.hintWeight = 1;
            rightIK.data = d;
        }
    }

    string GetQuery(string key)
    {
        var url = Application.absoluteURL;
        int q = url.IndexOf('?'); if (q < 0) return null;
        foreach (var kv in url.Substring(q + 1).Split('&'))
        {
            var p = kv.Split('=');
            if (p.Length == 2 && p[0] == key) return UnityWebRequest.UnEscapeURL(p[1]);
        }
        return null;
    }

    void AutoFixAudioListener()
    {
        // Unity 6(6000.x) 이상: 새로운 API
#if UNITY_6000_0_OR_NEWER
        var listeners = FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
#else
        // 구버전 호환(비활성 포함 검색)
        var listeners = FindObjectsOfType<AudioListener>(true);
#endif

        if (listeners == null || listeners.Length <= 1) return;

        // drivingCamera의 AudioListener를 우선 보존
        AudioListener keep = null;
        if (drivingCamera) keep = drivingCamera.GetComponent<AudioListener>();
        if (!keep) keep = listeners[0];

        int disabled = 0;
        foreach (var al in listeners)
        {
            if (al == keep) continue;
            if (al.enabled) { al.enabled = false; disabled++; }
        }

        Debug.Log($"[IKFromMediapipe] Disabled {disabled} extra AudioListener(s). Keeping: {(keep ? keep.name : "first")}");
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // StreamingAssets 안의 파일을 드래그하면 jsonFileName을 자동 갱신
        if (streamingJson == null) return;

        var path = AssetDatabase.GetAssetPath(streamingJson).Replace("\\", "/");
        const string SA = "Assets/StreamingAssets/";
        if (path.StartsWith(SA))
        {
            jsonFileName = path.Substring(SA.Length); // 서브폴더 포함 상대경로
            // 드래그 후 TextAsset 슬롯과 동시 사용 시, TextAsset이 우선됩니다.
            // 필요하면 jsonAsset=null로 두고 StreamingAssets 폴백을 사용하세요.
            Debug.Log($"[IK] StreamingAssets file set: {jsonFileName}");
        }
        else
        {
            Debug.LogWarning("StreamingAssets 폴더 안의 파일만 드래그하세요.");
        }
    }

    void OnDrawGizmos()
    {
        if (!drawGizmos || !Application.isPlaying) return;
        Gizmos.color = Color.cyan; if (leftTarget) Gizmos.DrawWireSphere(leftTarget.position, 0.03f);
        Gizmos.color = Color.magenta; if (rightTarget) Gizmos.DrawWireSphere(rightTarget.position, 0.03f);
    }
#endif
}
