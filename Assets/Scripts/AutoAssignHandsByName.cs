using UnityEngine;
using System.Linq;

/// <summary>
/// Humanoid�� �ƴϾ �̸� �κ���ġ�� �հ��� ���� ã�� HandPoseDriver�� �輱.
/// - Ready Player Me Ǯ�ٵ�� Mixamo ȣȯ ���(Armature ��Ʈ, Index1/2/3 ��)�̶� �̸� ��Ī���� ���.
/// - Awake()���� ������ HandPoseDriver ���� ���� �輱�ϰ�, ������ Rest �� �����.
/// </summary>
[DefaultExecutionOrder(-50)]
[RequireComponent(typeof(HandPoseDriver))]
public class AutoAssignHandsByName : MonoBehaviour
{
    [Tooltip("���� Armature�� �巡��. ���� �ڵ� Ž��.")]
    public Transform searchRoot;

    HandPoseDriver drv;

    void Awake()
    {
        drv = GetComponent<HandPoseDriver>();
        if (!searchRoot) searchRoot = FindArmature(transform) ?? transform;

        // ��Ʈ(�ɼ�)
        drv.leftHandRoot = FindOne("LeftHand", "Hand_L", "mixamorig:LeftHand");
        drv.rightHandRoot = FindOne("RightHand", "Hand_R", "mixamorig:RightHand");

        // --- Left ---
        drv.l_thumb1 = FindOne("LeftHandThumb1", "Thumb1_L", "mixamorig:LeftHandThumb1");
        drv.l_thumb2 = FindOne("LeftHandThumb2", "Thumb2_L", "mixamorig:LeftHandThumb2");
        drv.l_thumb3 = FindOne("LeftHandThumb3", "Thumb3_L", "mixamorig:LeftHandThumb3");

        drv.l_index1 = FindOne("LeftHandIndex1", "Index1_L", "mixamorig:LeftHandIndex1");
        drv.l_index2 = FindOne("LeftHandIndex2", "Index2_L", "mixamorig:LeftHandIndex2");
        drv.l_index3 = FindOne("LeftHandIndex3", "Index3_L", "mixamorig:LeftHandIndex3");

        drv.l_middle1 = FindOne("LeftHandMiddle1", "Middle1_L", "mixamorig:LeftHandMiddle1");
        drv.l_middle2 = FindOne("LeftHandMiddle2", "Middle2_L", "mixamorig:LeftHandMiddle2");
        drv.l_middle3 = FindOne("LeftHandMiddle3", "Middle3_L", "mixamorig:LeftHandMiddle3");

        drv.l_ring1 = FindOne("LeftHandRing1", "Ring1_L", "mixamorig:LeftHandRing1");
        drv.l_ring2 = FindOne("LeftHandRing2", "Ring2_L", "mixamorig:LeftHandRing2");
        drv.l_ring3 = FindOne("LeftHandRing3", "Ring3_L", "mixamorig:LeftHandRing3");

        drv.l_pinky1 = FindOne("LeftHandPinky1", "Pinky1_L", "mixamorig:LeftHandPinky1", "LeftHandLittle1");
        drv.l_pinky2 = FindOne("LeftHandPinky2", "Pinky2_L", "mixamorig:LeftHandPinky2", "LeftHandLittle2");
        drv.l_pinky3 = FindOne("LeftHandPinky3", "Pinky3_L", "mixamorig:LeftHandPinky3", "LeftHandLittle3");

        // --- Right ---
        drv.r_thumb1 = FindOne("RightHandThumb1", "Thumb1_R", "mixamorig:RightHandThumb1");
        drv.r_thumb2 = FindOne("RightHandThumb2", "Thumb2_R", "mixamorig:RightHandThumb2");
        drv.r_thumb3 = FindOne("RightHandThumb3", "Thumb3_R", "mixamorig:RightHandThumb3");

        drv.r_index1 = FindOne("RightHandIndex1", "Index1_R", "mixamorig:RightHandIndex1");
        drv.r_index2 = FindOne("RightHandIndex2", "Index2_R", "mixamorig:RightHandIndex2");
        drv.r_index3 = FindOne("RightHandIndex3", "Index3_R", "mixamorig:RightHandIndex3");

        drv.r_middle1 = FindOne("RightHandMiddle1", "Middle1_R", "mixamorig:RightHandMiddle1");
        drv.r_middle2 = FindOne("RightHandMiddle2", "Middle2_R", "mixamorig:RightHandMiddle2");
        drv.r_middle3 = FindOne("RightHandMiddle3", "Middle3_R", "mixamorig:RightHandMiddle3");

        drv.r_ring1 = FindOne("RightHandRing1", "Ring1_R", "mixamorig:RightHandRing1");
        drv.r_ring2 = FindOne("RightHandRing2", "Ring2_R", "mixamorig:RightHandRing2");
        drv.r_ring3 = FindOne("RightHandRing3", "Ring3_R", "mixamorig:RightHandRing3");

        drv.r_pinky1 = FindOne("RightHandPinky1", "Pinky1_R", "mixamorig:RightHandPinky1", "RightHandLittle1");
        drv.r_pinky2 = FindOne("RightHandPinky2", "Pinky2_R", "mixamorig:RightHandPinky2", "RightHandLittle2");
        drv.r_pinky3 = FindOne("RightHandPinky3", "Pinky3_R", "mixamorig:RightHandPinky3", "RightHandLittle3");

        // �輱 �� Rest �� �籸�� (�߿�!)
        drv.RebuildRestMaps();

        // �ڵ� �輱�� �� ���� ���� �Ǵϱ� ���α�(�Ǽ� ����)
        enabled = false;
    }

    Transform FindArmature(Transform from)
    {
        var all = from.root.GetComponentsInChildren<Transform>(true);
        var arm = all.FirstOrDefault(t => t.name.Equals("Armature", System.StringComparison.OrdinalIgnoreCase));
        return arm ? arm : from;
    }

    Transform FindOne(params string[] keys)
    {
        if (!searchRoot) return null;
        var all = searchRoot.GetComponentsInChildren<Transform>(true);
        foreach (var k in keys)
        {
            var hit = all.FirstOrDefault(t =>
                t.name.IndexOf(k, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                t.name.IndexOf("mixamorig:" + k, System.StringComparison.OrdinalIgnoreCase) >= 0
            );
            if (hit) return hit;
        }
        return null;
    }
}
