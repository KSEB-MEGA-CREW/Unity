using UnityEngine;

[DefaultExecutionOrder(10000)]
public class LockAvatarWorldPose : MonoBehaviour {
    public Transform anchor;     // 원하는 기준 위치(없으면 현재 위치 기억)
    Vector3 pos; Quaternion rot;
    void Awake() {
        if (anchor) { pos = anchor.position; rot = anchor.rotation; }
        else { pos = transform.position; rot = transform.rotation; }
    }
    void LateUpdate() { transform.SetPositionAndRotation(pos, rot); }
}
