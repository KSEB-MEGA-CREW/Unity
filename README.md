✋ Unity WebGL Client — Sign Language Player & IK Rig

MediaPipe 기반 좌표(JSON)를 Unity 씬에서 재생하고, TwoBoneIK + 손가락 드라이버로 아바타를 구동하는 WebGL 클라이언트입니다.
-------





-------
✅ 요구 사항

Unity 6 (2024/6000 계열) 권장

WebGL Build Support 설치

브라우저: Chromium 기반(Chrome/Edge), Firefox 최신
-------





-------
📁 프로젝트 구조
Unity/
├─ Assets/                # 씬, 스크립트, 프리팹, 리그
├─ Packages/
├─ ProjectSettings/
└─ UserSettings/
-------





-------
🧩 핵심 스크립트
IKFromMediapipe.cs

MediaPipe frames(pose, left_hand, right_hand) JSON을 읽어서

drivingCamera 기준의 일정 깊이 평면으로 Unity 월드 좌표로 투영

손목 Target / 팔꿈치 Hint 위치를 갱신하고 TwoBoneIK로 팔을 구동

손목 회전 안정화(플립 방지, 스무딩) 옵션 제공

팔 간격 보정(어깨-어깨 축 기준)

HandPoseDriver.cs

21개 손 랜드마크로 각 손가락 본을 구동

1€ 필터 / 단순보간 / 속도 제한 / 가동범위(ROM) 소프트 클램프

AutoAssignHandsByName.cs

믹사모/일반 휴머노이드 본 이름 패턴으로 손가락 본을 자동 바인딩

HandPoseDriver.RebuildRestMaps() 자동 호출

OneEuroFilter.cs

위치/방향 신호용 1€ 필터(속도 적응형 저역통과)
-------




-------
🚀 빠른 시작
1. 씬열기
2. SignDriver의 IKFromMediapipe 셋팅 ( drivingCamera는 Drivng Camera로 할당 필수 )
3. StreamingAsset의 json파일을 드래그 하여 사용 가능합니다. 또는, 파일명을 입력하여도 사용 가능합니다.
-------



-------
📜 라이선스
본 Unity 클라이언트/스크립트는 프로젝트 라이선스 정책에 따릅니다.
