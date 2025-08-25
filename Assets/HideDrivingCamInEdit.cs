using UnityEngine;
public class CameraMatrixLogger : MonoBehaviour {
    Camera cam; Matrix4x4 Pv, Vv;
    void Awake(){ cam=GetComponent<Camera>(); Pv=cam.projectionMatrix; Vv=cam.worldToCameraMatrix; }
    void LateUpdate(){
        if (cam.projectionMatrix != Pv || cam.worldToCameraMatrix != Vv) {
            Debug.Log("[Camera] Matrices changed this frame", this);
            Pv = cam.projectionMatrix; Vv = cam.worldToCameraMatrix;
        }
    }
}
