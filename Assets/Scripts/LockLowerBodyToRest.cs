// LockLowerBodyToRest.cs
using UnityEngine;
using System.Collections.Generic;

public class LockLowerBodyToRest : MonoBehaviour
{
    [Header("Lock these transforms to their rest(local) rotations")]
    public Transform hips, spine;
    public Transform leftUpperLeg, leftLowerLeg, leftFoot;
    public Transform rightUpperLeg, rightLowerLeg, rightFoot;

    private readonly Dictionary<Transform, Quaternion> rest = new();

    void Awake()
    {
        Cache(hips); Cache(spine);
        Cache(leftUpperLeg); Cache(leftLowerLeg); Cache(leftFoot);
        Cache(rightUpperLeg); Cache(rightLowerLeg); Cache(rightFoot);
    }

    void Cache(Transform t) { if (t) rest[t] = t.localRotation; }

    // Animator/리깅 이후에 덮어쓰기
    void LateUpdate()
    {
        foreach (var kv in rest)
            if (kv.Key) kv.Key.localRotation = kv.Value;
    }
}
