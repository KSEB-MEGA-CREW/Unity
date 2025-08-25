import os
import re
import json
import cv2
import mediapipe as mp

# ===== 사용자 설정 =====
video_path = r"C:\Users\USER\Downloads\v.mp4"
output_dir = os.path.join(os.path.dirname(video_path), "mp_json")  # 영상 폴더 아래 mp_json/ 에 저장
os.makedirs(output_dir, exist_ok=True)

# 속도-정밀도 트레이드오프: 1이면 모든 프레임, 2면 2프레임에 1프레임만 처리
FRAME_STEP = 1

# ===== 랜드마크 이름 (네 코드와 동일) =====
POSE_INDEXES = list(range(17))  # 0~16만 사용
hand_landmarks = [
    'WRIST', 'THUMB_CMC', 'THUMB_MCP', 'THUMB_IP', 'THUMB_TIP',
    'INDEX_FINGER_MCP', 'INDEX_FINGER_PIP', 'INDEX_FINGER_DIP', 'INDEX_FINGER_TIP',
    'MIDDLE_FINGER_MCP', 'MIDDLE_FINGER_PIP', 'MIDDLE_FINGER_DIP', 'MIDDLE_FINGER_TIP',
    'RING_FINGER_MCP', 'RING_FINGER_PIP', 'RING_FINGER_DIP', 'RING_FINGER_TIP',
    'PINKY_MCP', 'PINKY_PIP', 'PINKY_DIP', 'PINKY_TIP'
]
pose_landmarks = [
    "NOSE", "LEFT_EYE_INNER", "LEFT_EYE", "LEFT_EYE_OUTER",
    "RIGHT_EYE_INNER", "RIGHT_EYE", "RIGHT_EYE_OUTER",
    "LEFT_EAR", "RIGHT_EAR",
    "MOUTH_LEFT", "MOUTH_RIGHT",
    "LEFT_SHOULDER", "RIGHT_SHOULDER",
    "LEFT_ELBOW", "RIGHT_ELBOW",
    "LEFT_WRIST", "RIGHT_WRIST"
]

def sanitize_filename(name):
    return re.sub(r'[:\/\\?*<>|"]', '_', name)

def extract_hand_json(hand_obj):
    if hand_obj is None:
        return [{"name": name, "x": 0.0, "y": 0.0, "z": 0.0} for name in hand_landmarks]
    return [
        {"name": hand_landmarks[i], "x": float(lm.x), "y": float(lm.y), "z": float(lm.z)}
        for i, lm in enumerate(hand_obj.landmark)
    ]

def extract_pose_json(pose_obj):
    if pose_obj is None:
        return [{"name": name, "x": 0.0, "y": 0.0, "z": 0.0, "visibility": 0.0} for name in pose_landmarks]
    out = []
    for i, lm in enumerate(pose_obj.landmark):
        if i in POSE_INDEXES:  # 0~16
            out.append({
                "name": pose_landmarks[i],
                "x": float(lm.x), "y": float(lm.y), "z": float(lm.z),
                "visibility": float(getattr(lm, 'visibility', 0.0))
            })
    return out

# ===== 메인 처리 =====
base_id = os.path.splitext(os.path.basename(video_path))[0]
out_name = f"{sanitize_filename(base_id)}_FULL.json"
out_path = os.path.join(output_dir, out_name)

cap = cv2.VideoCapture(video_path)
if not cap.isOpened():
    raise RuntimeError(f"비디오를 열 수 없습니다: {video_path}")

fps = cap.get(cv2.CAP_PROP_FPS) or 30.0
total_frames = int(cap.get(cv2.CAP_PROP_FRAME_COUNT) or 0)
print(f"🎬 입력 영상: {video_path}")
print(f"   - FPS: {fps:.2f}, 프레임 수: {total_frames}")

results_json = []

with mp.solutions.holistic.Holistic(
    min_detection_confidence=0.7,
    min_tracking_confidence=0.7
) as holistic:
    frame_idx = 0
    while True:
        ret, frame = cap.read()
        if not ret:
            break

        # 프레임 스텝 샘플링 (속도 개선 옵션)
        if (frame_idx % FRAME_STEP) != 0:
            frame_idx += 1
            continue

        image_rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        results = holistic.process(image_rgb)

        frame_dict = {
            "left_hand":  extract_hand_json(results.left_hand_landmarks),
            "right_hand": extract_hand_json(results.right_hand_landmarks),
            "pose":       extract_pose_json(results.pose_landmarks)
        }
        results_json.append(frame_dict)

        # 진행상황 표시 (간단)
        if frame_idx % (30 * FRAME_STEP) == 0:
            print(f"  - 처리 중... {frame_idx}/{total_frames} 프레임")

        frame_idx += 1

cap.release()

with open(out_path, "w", encoding="utf-8") as f:
    json.dump(results_json, f, ensure_ascii=False, indent=2)

print(f"\n✅ 완료! 저장 위치: {out_path}")
print(f"   - 총 저장 프레임: {len(results_json)} (FRAME_STEP={FRAME_STEP})")
