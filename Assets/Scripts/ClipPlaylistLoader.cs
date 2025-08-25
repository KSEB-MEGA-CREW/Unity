using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json.Linq;

public class ClipPlaylistLoader : MonoBehaviour
{
    [Tooltip("프레임을 구동할 대상")]
    public IKFromMediapipe target;

    [Header("초기 파일 목록 (에디터용)")]
    [Tooltip("기본값: StreamingAssets 기준 상대경로 또는 StreamingAssets/xxx.json 형태, 혹은 http(s) 절대 URL")]
    public List<string> files = new List<string>();

    [Header("실행 옵션")]
    [Tooltip("여러 JSON을 하나로 합쳐서 한 번에 로드")]
    public bool concatenateIntoOne = true;
    [Tooltip("마지막 후 다시 처음부터")]
    public bool loop = false;
    [Tooltip("개별 클립 플레이리스트 방식일 때, 클립 사이 대기시간(초)")]
    public float gapSeconds = 0f;

    IEnumerator Start()
    {
        if (!target) target = GetComponent<IKFromMediapipe>();

        // URL 파라미터 파싱
        // ?concat=a.json,b.json,c.json  -> 모두 합쳐서 1번 로드
        // ?playlist=a.json,b.json,c.json -> 순차 재생(끝나면 다음)
        string concatQ = GetQuery("concat");
        string plistQ  = GetQuery("playlist");

        if (!string.IsNullOrEmpty(concatQ))
        {
            files = SplitCsv(concatQ);
            yield return StartCoroutine(ConcatAndPlay(files));
            yield break;
        }
        if (!string.IsNullOrEmpty(plistQ))
        {
            files = SplitCsv(plistQ);
            yield return StartCoroutine(PlaySequential(files));
            yield break;
        }

        // URL 파라미터가 없으면 에디터에 넣어둔 목록 사용
        if (files != null && files.Count > 0)
        {
            if (concatenateIntoOne) yield return StartCoroutine(ConcatAndPlay(files));
            else yield return StartCoroutine(PlaySequential(files));
        }
    }

    // ───────────────── helpers ─────────────────
    List<string> SplitCsv(string csv)
        => csv.Split(',').Select(s => UnityWebRequest.UnEscapeURL(s.Trim())).Where(s => !string.IsNullOrEmpty(s)).ToList();

    string GetQuery(string key)
    {
        var url = Application.absoluteURL;
        int q = url.IndexOf('?'); if (q < 0) return null;
        foreach (var kv in url.Substring(q + 1).Split('&'))
        {
            var p = kv.Split('=');
            if (p.Length == 2 && p[0] == key) return p[1];
        }
        return null;
    }

    string ResolvePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;

        // 절대 URL이면 그대로 사용
        if (path.StartsWith("http://") || path.StartsWith("https://") || path.StartsWith("file://"))
            return path;

        // "StreamingAssets/파일" 또는 상대경로 → StreamingAssets 기준으로 해석
        string rel = path.StartsWith("StreamingAssets/") ? path.Substring("StreamingAssets/".Length) : path;

#if UNITY_WEBGL && !UNITY_EDITOR
        // WebGL 런타임: 빌드 출력물 루트에서 StreamingAssets/ 상대로 접근
        return "StreamingAssets/" + rel;
#else
        // 에디터/데스크톱: file:// + 실제 경로
        string full = System.IO.Path.Combine(Application.streamingAssetsPath, rel);
        if (!full.StartsWith("file://")) full = "file://" + full;
        return full;
#endif
    }

    IEnumerator DownloadText(string path, System.Action<string> onDone)
    {
        string url = ResolvePath(path);
        using var req = UnityWebRequest.Get(url);
        yield return req.SendWebRequest();
        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[Playlist] Download failed: {path} ({req.error})");
            onDone?.Invoke(null);
        }
        else onDone?.Invoke(req.downloadHandler.text);
    }

    // 여러 JSON을 하나로 합쳐서 한 번에 로드
    IEnumerator ConcatAndPlay(List<string> list)
    {
        if (list == null || list.Count == 0 || target == null) yield break;

        var allFrames = new JArray();
        float fps = Mathf.Max(1f, target.fps); // 기본값
        bool fpsSet = false;

        foreach (var p in list)
        {
            string json = null;
            yield return DownloadText(p, t => json = t);
            if (string.IsNullOrEmpty(json)) continue;

            var first = json.TrimStart().FirstOrDefault();
            if (first == '[')
            {
                var arr = JArray.Parse(json);
                foreach (var f in arr) allFrames.Add(f);
            }
            else
            {
                var jo = JObject.Parse(json);
                var fr = jo["frames"] as JArray;
                if (fr != null) foreach (var f in fr) allFrames.Add(f);
                if (!fpsSet && jo["fps_sampled"] != null)
                {
                    float f = jo.Value<float>("fps_sampled");
                    if (f > 0) { fps = f; fpsSet = true; }
                }
            }
        }

        var root = new JObject
        {
            ["fps_sampled"] = fps,
            ["frames"] = allFrames
        };

        target.LoadFromJsonText(root.ToString(Newtonsoft.Json.Formatting.None));

        if (loop && list.Count > 0)
        {
            // 무한 반복: 끝나면 다시 합쳐서 시작(간단히 프레임 전송만 반복해도 됨)
            while (loop)
            {
                yield return null; // 한 프레임 양보
                target.LoadFromJsonText(root.ToString(Newtonsoft.Json.Formatting.None));
            }
        }
    }

    // 각 JSON을 순서대로 재생(끝날 때까지 기다렸다 다음 재생)
    IEnumerator PlaySequential(List<string> list)
    {
        if (list == null || list.Count == 0 || target == null) yield break;

        do
        {
            foreach (var p in list)
            {
                string json = null;
                yield return DownloadText(p, t => json = t);
                if (string.IsNullOrEmpty(json)) continue;

                // 지속시간 추정(프레임 수 / fps)
                int frameCount = 0;
                float fps = Mathf.Max(1f, target.fps);

                var first = json.TrimStart().FirstOrDefault();
                if (first == '[')
                {
                    var arr = JArray.Parse(json);
                    frameCount = arr.Count;
                }
                else
                {
                    var jo = JObject.Parse(json);
                    var fr = jo["frames"] as JArray;
                    frameCount = fr != null ? fr.Count : 0;
                    float f = jo.Value<float?>("fps_sampled") ?? 0f;
                    if (f > 0) fps = f;
                }

                target.LoadFromJsonText(json);

                float duration = (frameCount > 0) ? (frameCount / Mathf.Max(1f, fps)) : (2f);
                yield return new WaitForSeconds(duration + Mathf.Max(0f, gapSeconds));
            }
        } while (loop);
    }
}
