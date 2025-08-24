// Assets/Editor/AutoSetupTwoBoneIK.cs
// Unity 6.x + Animation Rigging 1.3.x
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations.Rigging;

public static class AutoSetupTwoBoneIK
{
    [MenuItem("Tools/SignAvatar/Auto-setup Arms IK (Two-Bone)")]
    public static void SetupSelected()
    {
        var root = Selection.activeGameObject;
        if (!root)
        {
            EditorUtility.DisplayDialog("Auto-setup IK", "Hierarchy에서 아바타 루트를 선택하세요.", "OK");
            return;
        }

        // Animator 보장
        var animator = root.GetComponent<Animator>();
        if (!animator) animator = Undo.AddComponent<Animator>(root);

        // RigBuilder 보장 (Animator 옆에서 Rig 레이어들을 평가) :contentReference[oaicite:9]{index=9}
        var rigBuilder = root.GetComponent<RigBuilder>();
        if (!rigBuilder) rigBuilder = Undo.AddComponent<RigBuilder>(root);

        // Rig 오브젝트 보장
        var rigGO = FindOrCreateChild(root.transform, "Rig", true);
        var rig = rigGO.GetComponent<Rig>();
        if (!rig) rig = Undo.AddComponent<Rig>(rigGO.gameObject);

        // RigBuilder Layers에 Rig 등록 (RigLayer API) :contentReference[oaicite:10]{index=10}
        TryAddRigLayer(rigBuilder, rig);

        // 본 찾기 (RPM/Mixamo 계열 명명 + 별칭)
        var tLeftUpper = FindBone(root.transform, new[] { "LeftArm", "LeftUpperArm", "UpperArm_L" });
        var tLeftLower = FindBone(root.transform, new[] { "LeftForeArm", "LeftLowerArm", "LowerArm_L", "Forearm_L" });
        var tLeftHand = FindBone(root.transform, new[] { "LeftHand", "Hand_L" });

        var tRightUpper = FindBone(root.transform, new[] { "RightArm", "RightUpperArm", "UpperArm_R" });
        var tRightLower = FindBone(root.transform, new[] { "RightForeArm", "RightLowerArm", "LowerArm_R", "Forearm_R" });
        var tRightHand = FindBone(root.transform, new[] { "RightHand", "Hand_R" });

        if (!tLeftUpper || !tLeftLower || !tLeftHand || !tRightUpper || !tRightLower || !tRightHand)
        {
            EditorUtility.DisplayDialog("Auto-setup IK",
                "팔 본(Upper/Fore/Hand)을 찾지 못했습니다.\n본 이름을 확인해주세요.", "OK");
            return;
        }

        // 왼팔 IK 계층
        CreateTwoBoneIK(rigGO.transform, "LeftArm_IK",
                        tLeftUpper, tLeftLower, tLeftHand,
                        out var leftTarget, out var leftHint);

        // 오른팔 IK 계층
        CreateTwoBoneIK(rigGO.transform, "RightArm_IK",
                        tRightUpper, tRightLower, tRightHand,
                        out var rightTarget, out var rightHint);

        // 선택 포커스
        Selection.objects = new Object[] { leftTarget.gameObject, rightTarget.gameObject };
        EditorGUIUtility.PingObject(leftTarget);

        EditorUtility.DisplayDialog("Auto-setup IK",
            "완료! Target을 움직여 팔이 따라오는지 확인하세요.\n(팁: 굽힘 방향은 Hint로 조절합니다)", "Great");
    }

    // 메뉴 활성 조건(선택된 오브젝트가 있을 때만)
    [MenuItem("Tools/SignAvatar/Auto-setup Arms IK (Two-Bone)", true)]
    public static bool SetupSelected_Validate() => Selection.activeGameObject != null;

    // === Helpers ===

    static Transform FindOrCreateChild(Transform parent, string name, bool resetLocal = false)
    {
        var t = parent.Find(name);
        if (!t)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            t = go.transform;
            Undo.SetTransformParent(t, parent, "Parent " + name);
            if (resetLocal)
            {
                t.localPosition = Vector3.zero;
                t.localRotation = Quaternion.identity;
                t.localScale = Vector3.one;
            }
        }
        return t;
    }

    static Transform FindBone(Transform root, IEnumerable<string> names)
    {
        foreach (var n in names)
        {
            var t = FindDeep(root, n);
            if (t) return t;
        }
        return null;
    }

    static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        for (int i = 0; i < t.childCount; i++)
        {
            var r = FindDeep(t.GetChild(i), name);
            if (r) return r;
        }
        return null;
    }

    static void CreateTwoBoneIK(Transform rigParent, string ikName,
                                Transform root, Transform mid, Transform tip,
                                out Transform target, out Transform hint)
    {
        // IK 노드
        var ikNode = FindOrCreateChild(rigParent, ikName);
        var ik = ikNode.GetComponent<TwoBoneIKConstraint>();
        if (!ik) ik = Undo.AddComponent<TwoBoneIKConstraint>(ikNode.gameObject);

        // Target / Hint 생성 & 배치
        target = FindOrCreateChild(ikNode, ikName.Replace("_IK", "") + "_Target");
        hint = FindOrCreateChild(ikNode, ikName.Replace("_IK", "") + "_Hint");

        // 타깃은 Tip과 정렬(초기 배치)
        target.position = tip.position;
        target.rotation = tip.rotation;

        // 힌트: 팔꿈치 평면 노멀 방향으로 살짝
        var dirUpper = (mid.position - root.position).normalized;
        var dirLower = (tip.position - mid.position).normalized;
        var planeN = Vector3.Cross(dirUpper, dirLower).normalized;
        if (planeN.sqrMagnitude < 1e-6f) planeN = Vector3.up;
        hint.position = mid.position + planeN * 0.1f;
        hint.rotation = mid.rotation;

        // 데이터 할당
        var data = ik.data;
        data.root = root;
        data.mid = mid;
        data.tip = tip;
        data.target = target;
        data.hint = hint;

        // ★ 포인트: MediaPipe 좌표로 타깃을 직접 구동하므로 오프셋 유지 끄기
        // (Tip↔Target 초기 오프셋을 유지하는 옵션. 여기선 None이 자연스럽습니다) :contentReference[oaicite:11]{index=11}
        data.maintainTargetPositionOffset = false;
        data.maintainTargetRotationOffset = false;

        data.targetPositionWeight = 1f;
        data.targetRotationWeight = 1f;
        data.hintWeight = 1f;
        ik.data = data;

        ik.weight = 1f;

        Undo.RecordObject(ik, "Setup TwoBoneIK");
        EditorUtility.SetDirty(ik);
    }

    static void TryAddRigLayer(RigBuilder rigBuilder, Rig rig)
    {
        // 공식 RigLayer API 사용(버전별 호환)
        try
        {
            var layers = rigBuilder.layers;
            bool exists = false;
            foreach (var l in layers) { if (l != null && l.rig == rig) { exists = true; break; } }
            if (!exists) layers.Add(new RigLayer(rig, true)); // RigBuilder가 레이어 순서/활성 관리 :contentReference[oaicite:12]{index=12}
            rigBuilder.layers = layers;
            return;
        }
        catch { /* 낮은 버전 호환: SerializedObject 경로 */ }

        var so = new SerializedObject(rigBuilder);
        var layersProp = so.FindProperty("m_Layers") ?? so.FindProperty("layers");
        if (layersProp != null)
        {
            for (int i = 0; i < layersProp.arraySize; i++)
            {
                var el = layersProp.GetArrayElementAtIndex(i);
                var rigProp = el.FindPropertyRelative("rig") ?? el.FindPropertyRelative("m_Rig");
                if (rigProp != null && rigProp.objectReferenceValue == rig)
                { so.ApplyModifiedProperties(); return; }
            }
            int index = layersProp.arraySize;
            layersProp.InsertArrayElementAtIndex(index);
            var layer = layersProp.GetArrayElementAtIndex(index);
            var rigRef = layer.FindPropertyRelative("rig") ?? layer.FindPropertyRelative("m_Rig");
            var active = layer.FindPropertyRelative("active") ?? layer.FindPropertyRelative("m_Active");
            if (rigRef != null) rigRef.objectReferenceValue = rig;
            if (active != null) active.boolValue = true;
            so.ApplyModifiedProperties();
        }
    }
}
