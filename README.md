# ✋ Unity WebGL Client — Sign Language Player & IK Rig

MediaPipe 기반 좌표(JSON)를 Unity 씬에서 재생하고, TwoBoneIK + 손가락 드라이버로 아바타를 구동하는 **WebGL 클라이언트**입니다.

---

## ✅ 요구 사항
- **Unity 6** (2024/6000 계열 권장)
- **WebGL Build Support** 설치
- 브라우저: 최신 **Chrome / Edge / Firefox**

---

## 📂 프로젝트 구조
Unity/
├─ Assets/ # 씬, 스크립트, 프리팹, 리그
├─ Packages/
├─ ProjectSettings/
└─ UserSettings/




---

## 🧩 핵심 스크립트

### `IKFromMediapipe.cs`
- MediaPipe frames(`pose`, `left_hand`, `right_hand`) **JSON**을 읽음
- **drivingCamera** 기준의 일정 깊이 평면으로 **Unity 월드 좌표로 투영**
- 손목 **Target** / 팔꿈치 **Hint** 위치를 갱신하고 **TwoBoneIK**로 팔 구동
- 손목 회전 **안정화**(플립 방지, 스무딩) 옵션 제공
- **팔 간격 보정**(어깨-어깨 축 기준)

### `HandPoseDriver.cs`
- 21개 손 랜드마크로 **손가락 본** 구동
- **1€ 필터 / 단순 보간 / 속도 제한 / ROM(가동범위) 소프트 클램프** 제공

### `AutoAssignHandsByName.cs`
- 믹사모/일반 휴머노이드 본 이름 패턴으로 **손가락 본 자동 바인딩**
- `HandPoseDriver.RebuildRestMaps()` 자동 호출

### `OneEuroFilter.cs`
- 위치/방향 신호용 **1€ 필터**(속도 적응형 저역통과)

---

## 🚀 빠른 시작

1. **씬 열기**
2. `SignDriver` 오브젝트의 **IKFromMediapipe** 설정  
   - `drivingCamera` → **Driving Camera**로 반드시 할당
3. **StreamingAssets**에 있는 JSON 파일을  
   - **드래그**해서 사용하거나,  
   - **파일명**을 입력해 사용

> JSON은 MediaPipe Holistic 형식의 `frames` 배열을 포함해야 합니다.

---

## 📜 라이선스
본 Unity 클라이언트/스크립트는 **프로젝트 라이선스 정책**을 따릅니다.
