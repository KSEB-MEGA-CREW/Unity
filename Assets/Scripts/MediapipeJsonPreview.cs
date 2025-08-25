// MediapipeJsonPreview.cs
// Attach to your RenderCamera (Game view overlay) – also draws 3D gizmos in Scene view.
#if !UNITY_WEBGL || UNITY_EDITOR
using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

[RequireComponent(typeof(Camera))]
public class MediapipeJsonPreview : MonoBehaviour
{

    
    [Header("Input JSON")]
    public TextAsset jsonAsset;
    [Tooltip("StreamingAssets 상대 경로 (jsonAsset이 없을 때 폴백). 예) Sub/clip.json")]
    public string streamingAssetsRelativePath = "";

    [Header("Cameras")]
    [Tooltip("좌표 변환 기준 카메라(움직이지 않는 Driving 카메라)")]
    public Camera drivingCamera;

    [Header("Playback")]
    [Min(0.1f)] public float fps = 30f;
    [Range(0.1f, 3f)] public float playbackSpeed = 1f;
    public bool loop = true;
    public bool playOnStart = true;

    [Header("2D Overlay (Game View)")]
    public bool draw2DOverlay = true;
    public bool drawGrid = true;
    [Range(0.002f, 0.03f)] public float pointSize = 0.008f; // overlay size in viewport units
    public bool drawIndices = false;
    public Color leftColor = new Color(0.2f, 0.8f, 1f, 1f);
    public Color rightColor = new Color(1f, 0.4f, 0.7f, 1f);
    public Color gridColor = new Color(1,1,1,0.25f);

    [Header("Pose overlay")]
    public bool drawPose2D = true;
    public bool drawPose3D = true;
    public Color poseColor = new Color(1f, 1f, 0f, 0.9f);

    [Header("3D Gizmos (Scene/Game Gizmos)")]
    public bool draw3DGizmos = true;
    public float worldDepth = 1.8f; // meters from drivingCamera
    public float gizmoPointSize = 0.015f;

    [Header("Mapping stability")]
    [Tooltip("시작 시 카메라 FOV/Aspect/Transform을 스냅샷해 이후에도 그대로 사용")]
    public bool lockMappingToStartCamera = true;

    [Header("Input options")]
    [Tooltip("입력이 좌우 반전되어 있을 때 켜기")]
    public bool mirrorInputX = false;

    [Header("WebGL (optional)")]
    [Tooltip("WebGL에서 SendMessage를 받을 GameObject 이름 (기본값=이 오브젝트 이름)")]
    public string webglReceiverName = ""; // 공백이면 runtime에 gameObject.name 사용
    public string webglReceiverMethod = "LoadFromJsonText";

#if UNITY_WEBGL && !UNITY_EDITOR
    // JS 바인딩: 파일 선택 열고, 선택된 파일을 읽어 SendMessage(receiverName, methodName, jsonText)를 호출
    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern void MPV_OpenFilePicker(string receiverName, string methodName);
#endif

    // ───────── internal ─────────
    class LMV { public string name; public float x, y, z; public float visibility; }
    class FrameV { public LMV[] left_hand; public LMV[] right_hand; public LMV[] pose; }
    class SegmentRootV { public string video, gloss_id, source; public int occurrence; public float start, end, fps_sampled; public List<FrameV> frames; }

    List<FrameV> _frames; int _fi = 0; float _t = 0; bool _playing;
    Vector2[] _L2D, _R2D, _P2D;          // normalized 0..1 (x right, y down)
    Vector3[] _L3D, _R3D, _P3D;          // world
    bool[] _P2DHas;                      // 포즈 각 관절 존재 플래그
    Camera _thisCam;

    // stable mapping snapshot
    float _refFovDeg, _refAspect;
    Matrix4x4 _refCamLocalToWorld;

    static readonly int[][] HAND_EDGES = new int[][]
    {
        new[]{0,1}, new[]{1,2}, new[]{2,3}, new[]{3,4},        // thumb
        new[]{0,5}, new[]{5,6}, new[]{6,7}, new[]{7,8},        // index
        new[]{5,9}, new[]{9,10}, new[]{10,11}, new[]{11,12},   // middle (with palm link 5-9)
        new[]{9,13}, new[]{13,14}, new[]{14,15}, new[]{15,16}, // ring (with palm link 9-13)
        new[]{13,17}, new[]{17,18}, new[]{18,19}, new[]{19,20},// pinky (with palm link 13-17)
        new[]{0,5}, new[]{0,9}, new[]{0,13}, new[]{0,17}       // extra palm spokes
    };

    static readonly string[] POSE_KEYS = {
      "LEFT_SHOULDER","RIGHT_SHOULDER",
      "LEFT_ELBOW","RIGHT_ELBOW",
      "LEFT_WRIST","RIGHT_WRIST",
      "LEFT_HIP","RIGHT_HIP"
    };
    static readonly int[][] POSE_EDGES = {
      new[]{0,2}, new[]{2,4},   // L shoulder->elbow->wrist
      new[]{1,3}, new[]{3,5},   // R shoulder->elbow->wrist
      new[]{0,1},               // shoulders
      new[]{6,7},               // hips
      new[]{0,6}, new[]{1,7}    // shoulder-hip
    };

    // GL material for overlay
    static Material _lineMat;
    static void EnsureMat()
    {
        if (_lineMat) return;
        var shader = Shader.Find("Hidden/Internal-Colored");
        _lineMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        _lineMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        _lineMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        _lineMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        _lineMat.SetInt("_ZWrite", 0);
    }

    void Awake()
    {
        _thisCam = GetComponent<Camera>();
        if (playOnStart) _playing = true;
        if (string.IsNullOrEmpty(webglReceiverName)) webglReceiverName = gameObject.name;
    }

    IEnumerator Start()
    {
        if (!drivingCamera) drivingCamera = Camera.main;

        // snapshot mapping to ignore later camera/FOV/transform changes
        if (lockMappingToStartCamera && drivingCamera)
        {
            _refFovDeg = drivingCamera.fieldOfView;
            _refAspect = drivingCamera.aspect;
            _refCamLocalToWorld = drivingCamera.transform.localToWorldMatrix;
        }

        string json = null;
        if (jsonAsset) json = jsonAsset.text;
        else if (!string.IsNullOrEmpty(streamingAssetsRelativePath))
        {
            string path = System.IO.Path.Combine(Application.streamingAssetsPath, streamingAssetsRelativePath);
#if !UNITY_ANDROID || UNITY_EDITOR
            if (!path.StartsWith("file://")) path = "file://" + path;
#endif
            using var req = UnityWebRequest.Get(path);
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success) { Debug.LogError(req.error); yield break; }
            json = req.downloadHandler.text;
        }
        else
        {
            Debug.LogWarning("[MPViz] JSON 입력이 없습니다 (jsonAsset 또는 StreamingAssets 경로). 드래그/파일선택으로 로드하세요.");
            yield break;
        }

        if (!Parse(json))
        {
            Debug.LogError("[MPViz] JSON 파싱 실패");
            yield break;
        }
    }

    bool Parse(string json)
    {
        try
        {
            char first = json.SkipWhile(char.IsWhiteSpace).FirstOrDefault();
            if (first == '[')
            {
                _frames = JsonConvert.DeserializeObject<List<FrameV>>(json);
            }
            else
            {
                var jo = JObject.Parse(json);
                if (jo["frames"] != null)
                {
                    _frames = jo["frames"].ToObject<List<FrameV>>();
                    float f = jo.Value<float?>("fps_sampled") ?? 0f;
                    if (f > 0) fps = f;
                }
                else
                {
                    var r = jo.ToObject<SegmentRootV>();
                    _frames = r?.frames ?? new List<FrameV>();
                    if (r != null && r.fps_sampled > 0) fps = r.fps_sampled;
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[MPViz] Parse error: {e.Message}");
            return false;
        }
        _fi = 0; _t = 0; _playing = true;
        return _frames != null && _frames.Count > 0;
    }

    void Update()
    {
        if (_frames == null || _frames.Count == 0 || !_thisCam || !drivingCamera) return;

        if (_playing)
        {
            _t += Time.deltaTime * Mathf.Max(0.01f, playbackSpeed);
            float step = 1f / Mathf.Max(1f, fps);
            while (_t >= step)
            {
                _t -= step;
                _fi++;
                if (_fi >= _frames.Count) _fi = loop ? 0 : _frames.Count - 1;
            }
        }

        if (Input.GetKeyDown(KeyCode.Space)) _playing = !_playing;
        if (Input.GetKeyDown(KeyCode.RightArrow)) { _fi = Mathf.Min(_fi + 1, _frames.Count - 1); _t = 0; }
        if (Input.GetKeyDown(KeyCode.LeftArrow))  { _fi = Mathf.Max(_fi - 1, 0); _t = 0; }

        var f = _frames[_fi];
        _L2D = BuildHand2D(f.left_hand);
        _R2D = BuildHand2D(f.right_hand);
        _P2D = BuildPose2D(f.pose);                  // 부분 허용
        _L3D = Build3D(_L2D, worldDepth);
        _R3D = Build3D(_R2D, worldDepth);
        _P3D = Build3DPartial(_P2D, _P2DHas, worldDepth); // 존재하는 포즈만 3D 변환
    }

    Vector2[] BuildHand2D(LMV[] hand)
    {
        if (hand == null) return null;
        var map = new Dictionary<string, LMV>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var lm in hand) if (lm != null && !string.IsNullOrEmpty(lm.name) && !map.ContainsKey(lm.name)) map[lm.name] = lm;

        string[] names = {
            "WRIST",
            "THUMB_CMC","THUMB_MCP","THUMB_IP","THUMB_TIP",
            "INDEX_FINGER_MCP","INDEX_FINGER_PIP","INDEX_FINGER_DIP","INDEX_FINGER_TIP",
            "MIDDLE_FINGER_MCP","MIDDLE_FINGER_PIP","MIDDLE_FINGER_DIP","MIDDLE_FINGER_TIP",
            "RING_FINGER_MCP","RING_FINGER_PIP","RING_FINGER_DIP","RING_FINGER_TIP",
            "PINKY_MCP","PINKY_PIP","PINKY_DIP","PINKY_TIP"
        };

        var out21 = new Vector2[21];
        for (int i = 0; i < 21; i++)
        {
            if (!map.TryGetValue(names[i], out var lm)) return null; // 불완전하면 null
            float nx = Mathf.Clamp01(lm.x);
            float ny = Mathf.Clamp01(lm.y);
            if (mirrorInputX) nx = 1f - nx;
            out21[i] = new Vector2(nx, ny); // (0,0)=좌상, (1,1)=우하
        }
        return out21;
    }

    // ★ 부분 포즈 허용: 없는 키는 건너뛰고, 있는 키만 반환/표시
    Vector2[] BuildPose2D(LMV[] pose)
    {
        _P2DHas = null;
        if (pose == null) return null;

        var map = new Dictionary<string, LMV>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var lm in pose)
            if (lm != null && !string.IsNullOrEmpty(lm.name) && !map.ContainsKey(lm.name))
                map[lm.name] = lm;

        var outN = new Vector2[POSE_KEYS.Length];
        var has  = new bool[POSE_KEYS.Length];

        for (int i = 0; i < POSE_KEYS.Length; i++)
        {
            if (map.TryGetValue(POSE_KEYS[i], out var lm))
            {
                float nx = Mathf.Clamp01(lm.x);
                float ny = Mathf.Clamp01(lm.y);
                if (mirrorInputX) nx = 1f - nx;
                outN[i] = new Vector2(nx, ny);
                has[i] = true;
            }
            else
            {
                outN[i] = Vector2.zero;
                has[i] = false;
            }
        }

        _P2DHas = has; // 어떤 관절이 존재하는지 저장
        return outN;   // 항상 배열 반환(부분만 유효)
    }

    Vector3[] Build3D(Vector2[] pts2D, float zMeters)
    {
        if (pts2D == null) return null;
        var arr = new Vector3[pts2D.Length];
        for (int i = 0; i < pts2D.Length; i++)
        {
            arr[i] = ToWorldStable(drivingCamera, pts2D[i].x, pts2D[i].y, zMeters);
        }
        return arr;
    }

    // ★ 포즈용: 존재하는 점만 3D 변환
    Vector3[] Build3DPartial(Vector2[] pts2D, bool[] has, float zMeters)
    {
        if (pts2D == null || has == null) return null;
        var arr = new Vector3[pts2D.Length];
        for (int i = 0; i < pts2D.Length; i++)
        {
            arr[i] = has[i] ? ToWorldStable(drivingCamera, pts2D[i].x, pts2D[i].y, zMeters)
                            : Vector3.zero;
        }
        return arr;
    }

    Vector3 ToWorldStable(Camera cam, float nx, float ny, float z)
    {
        nx = Mathf.Clamp01(nx);
        ny = Mathf.Clamp01(ny);
        // NDC
        float u = 2f * nx - 1f;
        float v = 1f - 2f * ny; // 위가 +로 가도록 뒤집기

        float fovDeg = lockMappingToStartCamera ? _refFovDeg : cam.fieldOfView;
        float aspect = lockMappingToStartCamera ? _refAspect : cam.aspect;
        float fovRad = fovDeg * Mathf.Deg2Rad;
        float tanHalf = Mathf.Tan(fovRad * 0.5f);

        Vector3 dirCam = new Vector3(u * aspect * tanHalf, v * tanHalf, 1f);
        float dist = Mathf.Max(0.01f, z);
        float t = dist / Mathf.Max(1e-6f, dirCam.z);
        Vector3 pCam = dirCam * t;

        if (lockMappingToStartCamera)
            return _refCamLocalToWorld.MultiplyPoint3x4(pCam);
        else
            return cam.transform.TransformPoint(pCam);
    }

    // ───────── 2D overlay ─────────
    void OnPostRender()
    {
        if (!draw2DOverlay) return;
        EnsureMat();
        _lineMat.SetPass(0);

        GL.PushMatrix();
        GL.LoadOrtho(); // 0..1 overlay

        // grid
        if (drawGrid)
        {
            GL.Begin(GL.LINES);
            GL.Color(gridColor);
            for (int i = 1; i < 10; i++)
            {
                float t = i / 10f;
                GL.Vertex(new Vector3(t, 0, 0)); GL.Vertex(new Vector3(t, 1, 0));
                GL.Vertex(new Vector3(0, t, 0)); GL.Vertex(new Vector3(1, t, 0));
            }
            // border
            GL.Vertex(new Vector3(0,0,0)); GL.Vertex(new Vector3(1,0,0));
            GL.Vertex(new Vector3(1,0,0)); GL.Vertex(new Vector3(1,1,0));
            GL.Vertex(new Vector3(1,1,0)); GL.Vertex(new Vector3(0,1,0));
            GL.Vertex(new Vector3(0,1,0)); GL.Vertex(new Vector3(0,0,0));
            GL.End();
        }

        // left & right hands 2D
        DrawHand2D(_L2D, leftColor);
        DrawHand2D(_R2D, rightColor);

        // pose 2D (부분 허용)
        if (drawPose2D && _P2D != null && _P2DHas != null)
        {
            GL.Begin(GL.LINES);
            GL.Color(poseColor);
            foreach (var e in POSE_EDGES)
            {
                if (_P2DHas[e[0]] && _P2DHas[e[1]])
                {
                    var a = ToOverlay(_P2D[e[0]]);
                    var b = ToOverlay(_P2D[e[1]]);
                    GL.Vertex(a); GL.Vertex(b);
                }
            }
            GL.End();
        }

        GL.PopMatrix();
    }

    void DrawHand2D(Vector2[] pts, Color col)
    {
        if (pts == null) return;

        // lines
        GL.Begin(GL.LINES);
        GL.Color(new Color(col.r, col.g, col.b, 0.9f));
        foreach (var e in HAND_EDGES)
        {
            var a = ToOverlay(pts[e[0]]);
            var b = ToOverlay(pts[e[1]]);
            GL.Vertex(a); GL.Vertex(b);
        }
        GL.End();

        // points (quads)
        float r = pointSize;
        GL.Begin(GL.QUADS);
        GL.Color(col);
        for (int i = 0; i < pts.Length; i++)
        {
            var p = ToOverlay(pts[i]);
            GL.Vertex(new Vector3(p.x - r, p.y - r, 0));
            GL.Vertex(new Vector3(p.x + r, p.y - r, 0));
            GL.Vertex(new Vector3(p.x + r, p.y + r, 0));
            GL.Vertex(new Vector3(p.x - r, p.y + r, 0));
        }
        GL.End();

        if (drawIndices)
        {
            GL.Begin(GL.LINES);
            GL.Color(Color.black);
            float r2 = r * 0.6f;
            for (int i = 0; i < pts.Length; i++)
            {
                var p = ToOverlay(pts[i]);
                GL.Vertex(new Vector3(p.x - r2, p.y, 0));
                GL.Vertex(new Vector3(p.x + r2, p.y, 0));
                GL.Vertex(new Vector3(p.x, p.y - r2, 0));
                GL.Vertex(new Vector3(p.x, p.y + r2, 0));
            }
            GL.End();
        }
    }

    static Vector3 ToOverlay(Vector2 n) => new Vector3(Mathf.Clamp01(n.x), 1f - Mathf.Clamp01(n.y), 0);

    // ───────── 3D gizmos ─────────
    void OnDrawGizmos()
    {
        if (!Application.isPlaying || !draw3DGizmos) return;

        DrawGizmosHand(_L3D, leftColor);
        DrawGizmosHand(_R3D, rightColor);

        // pose 3D (부분 허용)
        if (drawPose3D && _P3D != null && _P2DHas != null)
        {
            Gizmos.color = new Color(poseColor.r, poseColor.g, poseColor.b, 0.85f);
            foreach (var e in POSE_EDGES)
            {
                if (_P2DHas[e[0]] && _P2DHas[e[1]])
                    Gizmos.DrawLine(_P3D[e[0]], _P3D[e[1]]);
            }
            for (int i = 0; i < _P3D.Length; i++)
            {
                if (_P2DHas[i]) Gizmos.DrawSphere(_P3D[i], gizmoPointSize);
            }
        }

        // axes at plane center (optional)
        if (drivingCamera)
        {
            Vector3 center = ToWorldStable(drivingCamera, 0.5f, 0.5f, worldDepth);
            float s = 0.2f;
            Gizmos.color = Color.red;   Gizmos.DrawLine(center, center + drivingCamera.transform.right * s);
            Gizmos.color = Color.green; Gizmos.DrawLine(center, center + drivingCamera.transform.up * s);
            Gizmos.color = Color.blue;  Gizmos.DrawLine(center, center + drivingCamera.transform.forward * s);
        }
    }

    void DrawGizmosHand(Vector3[] pts, Color col)
    {
        if (pts == null) return;
        Gizmos.color = new Color(col.r, col.g, col.b, 0.85f);
        foreach (var e in HAND_EDGES)
        {
            if (e[0] < pts.Length && e[1] < pts.Length)
                Gizmos.DrawLine(pts[e[0]], pts[e[1]]);
        }
        foreach (var p in pts)
        {
            Gizmos.DrawSphere(p, gizmoPointSize);
        }
    }

    // ───────── HUD / Drag&Drop / WebGL 버튼 ─────────
    public void LoadFromJsonText(string json)
    {
        if (!Parse(json)) { Debug.LogError("[MPViz] Drag/Web JSON parse failed"); return; }
        Debug.Log($"[MPViz] Loaded via text: {_frames.Count} frames");
    }

    void OnGUI()
    {
        // HUD (프레임 표기)
        if (_frames != null && _frames.Count > 0)
        {
            var style = new GUIStyle(GUI.skin.label){ fontSize = 14, normal = { textColor = Color.white } };
            GUI.Label(new Rect(8, Screen.height - 24, 600, 20),
                $"Frame: {_fi+1}/{_frames.Count}  (Space=Play/Pause, ←/→=Step)", style);
        }

#if UNITY_EDITOR
        // 에디터: 드래그&드롭 영역을 "화면 전체"로
        var drop = new Rect(0, 0, Screen.width, Screen.height);
        GUI.Box(new Rect(12, 12, 260, 70), "Drop .json anywhere (Editor)\n또는 아래 버튼 클릭");

        var e = Event.current;
        if (e != null && (e.type == EventType.DragUpdated || e.type == EventType.DragPerform))
        {
            if (drop.Contains(e.mousePosition))
            {
                bool hasJson = UnityEditor.DragAndDrop.paths.Any(p => p.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase));
                UnityEditor.DragAndDrop.visualMode = hasJson ? UnityEditor.DragAndDropVisualMode.Copy
                                                              : UnityEditor.DragAndDropVisualMode.Rejected;
                if (e.type == EventType.DragPerform && hasJson)
                {
                    UnityEditor.DragAndDrop.AcceptDrag();
                    string path = UnityEditor.DragAndDrop.paths.First(p => p.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase));
                    string json = System.IO.File.ReadAllText(path);
                    LoadFromJsonText(json);
                    jsonAsset = null; streamingAssetsRelativePath = "";
                    Debug.Log("[MPViz] Drag&Drop: " + path);
                }
                e.Use();
            }
        }

        // 에디터: 파일 열기 버튼
        if (GUI.Button(new Rect(12, 88, 180, 28), "Load JSON... (Editor)"))
        {
            string startDir = System.IO.Path.GetFullPath(Application.dataPath);
            string path = UnityEditor.EditorUtility.OpenFilePanel("Pick JSON", startDir, "json");
            if (!string.IsNullOrEmpty(path))
            {
                string json = System.IO.File.ReadAllText(path);
                LoadFromJsonText(json);
                jsonAsset = null; streamingAssetsRelativePath = "";
                Debug.Log("[MPViz] FilePicker: " + path);
            }
        }
#endif

#if UNITY_WEBGL && !UNITY_EDITOR
        // WebGL: 파일 선택 버튼 (드래그는 브라우저 보안상 불가)
        if (GUI.Button(new Rect(12, 12, 180, 32), "Load JSON..."))
        {
            MPV_OpenFilePicker(webglReceiverName, webglReceiverMethod);
        }
#endif
    }
}
#endif