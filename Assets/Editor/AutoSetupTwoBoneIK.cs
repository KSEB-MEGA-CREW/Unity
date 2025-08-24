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
            EditorUtility.DisplayDialog("Auto-setup IK", "Hierarchy���� �ƹ�Ÿ ��Ʈ�� �����ϼ���.", "OK");
            return;
        }

        // Animator ����
        var animator = root.GetComponent<Animator>();
        if (!animator) animator = Undo.AddComponent<Animator>(root);

        // RigBuilder ���� (Animator ������ Rig ���̾���� ��) :contentReference[oaicite:9]{index=9}
        var rigBuilder = root.GetComponent<RigBuilder>();
        if (!rigBuilder) rigBuilder = Undo.AddComponent<RigBuilder>(root);

        // Rig ������Ʈ ����
        var rigGO = FindOrCreateChild(root.transform, "Rig", true);
        var rig = rigGO.GetComponent<Rig>();
        if (!rig) rig = Undo.AddComponent<Rig>(rigGO.gameObject);

        // RigBuilder Layers�� Rig ��� (RigLayer API) :contentReference[oaicite:10]{index=10}
        TryAddRigLayer(rigBuilder, rig);

        // �� ã�� (RPM/Mixamo �迭 ��� + ��Ī)
        var tLeftUpper = FindBone(root.transform, new[] { "LeftArm", "LeftUpperArm", "UpperArm_L" });
        var tLeftLower = FindBone(root.transform, new[] { "LeftForeArm", "LeftLowerArm", "LowerArm_L", "Forearm_L" });
        var tLeftHand = FindBone(root.transform, new[] { "LeftHand", "Hand_L" });

        var tRightUpper = FindBone(root.transform, new[] { "RightArm", "RightUpperArm", "UpperArm_R" });
        var tRightLower = FindBone(root.transform, new[] { "RightForeArm", "RightLowerArm", "LowerArm_R", "Forearm_R" });
        var tRightHand = FindBone(root.transform, new[] { "RightHand", "Hand_R" });

        if (!tLeftUpper || !tLeftLower || !tLeftHand || !tRightUpper || !tRightLower || !tRightHand)
        {
            EditorUtility.DisplayDialog("Auto-setup IK",
                "�� ��(Upper/Fore/Hand)�� ã�� ���߽��ϴ�.\n�� �̸��� Ȯ�����ּ���.", "OK");
            return;
        }

        // ���� IK ����
        CreateTwoBoneIK(rigGO.transform, "LeftArm_IK",
                        tLeftUpper, tLeftLower, tLeftHand,
                        out var leftTarget, out var leftHint);

        // ������ IK ����
        CreateTwoBoneIK(rigGO.transform, "RightArm_IK",
                        tRightUpper, tRightLower, tRightHand,
                        out var rightTarget, out var rightHint);

        // ���� ��Ŀ��
        Selection.objects = new Object[] { leftTarget.gameObject, rightTarget.gameObject };
        EditorGUIUtility.PingObject(leftTarget);

        EditorUtility.DisplayDialog("Auto-setup IK",
            "�Ϸ�! Target�� ������ ���� ��������� Ȯ���ϼ���.\n(��: ���� ������ Hint�� �����մϴ�)", "Great");
    }

    // �޴� Ȱ�� ����(���õ� ������Ʈ�� ���� ����)
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
        // IK ���
        var ikNode = FindOrCreateChild(rigParent, ikName);
        var ik = ikNode.GetComponent<TwoBoneIKConstraint>();
        if (!ik) ik = Undo.AddComponent<TwoBoneIKConstraint>(ikNode.gameObject);

        // Target / Hint ���� & ��ġ
        target = FindOrCreateChild(ikNode, ikName.Replace("_IK", "") + "_Target");
        hint = FindOrCreateChild(ikNode, ikName.Replace("_IK", "") + "_Hint");

        // Ÿ���� Tip�� ����(�ʱ� ��ġ)
        target.position = tip.position;
        target.rotation = tip.rotation;

        // ��Ʈ: �Ȳ�ġ ��� ��� �������� ��¦
        var dirUpper = (mid.position - root.position).normalized;
        var dirLower = (tip.position - mid.position).normalized;
        var planeN = Vector3.Cross(dirUpper, dirLower).normalized;
        if (planeN.sqrMagnitude < 1e-6f) planeN = Vector3.up;
        hint.position = mid.position + planeN * 0.1f;
        hint.rotation = mid.rotation;

        // ������ �Ҵ�
        var data = ik.data;
        data.root = root;
        data.mid = mid;
        data.tip = tip;
        data.target = target;
        data.hint = hint;

        // �� ����Ʈ: MediaPipe ��ǥ�� Ÿ���� ���� �����ϹǷ� ������ ���� ����
        // (Tip��Target �ʱ� �������� �����ϴ� �ɼ�. ���⼱ None�� �ڿ��������ϴ�) :contentReference[oaicite:11]{index=11}
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
        // ���� RigLayer API ���(������ ȣȯ)
        try
        {
            var layers = rigBuilder.layers;
            bool exists = false;
            foreach (var l in layers) { if (l != null && l.rig == rig) { exists = true; break; } }
            if (!exists) layers.Add(new RigLayer(rig, true)); // RigBuilder�� ���̾� ����/Ȱ�� ���� :contentReference[oaicite:12]{index=12}
            rigBuilder.layers = layers;
            return;
        }
        catch { /* ���� ���� ȣȯ: SerializedObject ��� */ }

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
