# LogCompare

네트워크 장비(Cisco 등)의 대용량 터미널 점검 콘솔 로그를 파싱 및 구조화하여 관리하고, 과거 이력과의 시각적 변경점(Diff) 비교 및 2단계 계층형(2-Tier) 로컬 AI(Ollama)를 통한 이상 징후 자동 분석·교차 정밀 진단을 제공하는 통합 네트워크 로그 분석 플랫폼입니다.

![Main01](./Images/Main01.png)

![Log01](./Images/Log01.png)

![Compare01](./Images/Compare01.png)

---

## 🚀 주요 기능

### 1. 다채널 로그 수집 및 자동 구조화
- **웹 UI 업로드**: 브라우저에서 다중 로그 파일 일괄 드래그 앤 드롭 업로드 및 실시간 파싱.
- **로컬 디렉터리 자동 인제스천**: 백그라운드 워커가 `.raw_logs` 폴더로 유입되는 신규 로그 파일을 주기적으로 감지하여 DB 자동 적재.
- **파일명 메타데이터 안전 추출**: `{DeviceName}.{Date}.txt` 및 다중 도트(`.`) 패턴에서 확장자를 안전하게 사전 분리하고 날짜를 정밀 파싱하여, 과거 점검 로그가 오늘 날짜로 잘못 들어가는 현상을 방지하고 장비명·날짜·명령어별로 체계적 구조화.

### 2. 대용량 로그 뷰어 & 시각적 Diff 비교
- **가상 스크롤(Virtualize)**: 수만 줄 이상의 대용량 텍스트 로그도 버벅임(Freezing) 없이 매끄럽게 조회.
- **시각적 Diff**: 동일 장비의 점검 일자 간 변경점(추가/삭제/수정)을 나란히(Side-by-Side) 또는 인라인으로 색상별 직관적 비교.
- **Cisco Syslog 하이라이팅**: 에러(Error), 경고(Warning), 포트 Down 등 중요 시스템 메시지 자동 강조 및 빠른 탐색.

### 3. 2-Tier 계층형 로컬 AI 로그 분석 (Ollama `gemma4:12b`)
폐쇄망 및 사내 환경에서도 외부 데이터 유출 없이 로컬 LLM을 통한 정밀 분석을 수행합니다:
- **📋 1차 명령어별 독립 요약 (`LogSummary`)**:
  - 명령어 원본 로그를 분석하여 핵심 지표 요약 및 3단계 상태 심각도(**Normal** / **Warning** / **Critical**) 판정.
- **🩺 2차 AI 심층 교차 진단 (`LogDiagnosis`)**:
  - 1차 요약에서 이상 징후(Warning/Critical) 감지 시 자동 가동.
  - 동일 장비의 다른 모든 명령어 요약본을 교차 대조(Cross-Correlation)하여 **근본 원인(Root Cause), 타 명령어와의 인과관계, 시스템 영향도, 현장 엔지니어 긴급 조치 권고안(Action Items)** 도출.
- **데이터 기반 직접 분석 (Data-Driven)**:
  - 장비 모델명 식별 실패로 인한 분석 중단(SPOF)을 원천 배제하고, DB의 `RawLogs`에 실제 존재하는 명령어를 기반으로 중단 없이 자율 분석.

### 4. 프롬프트 단일화 및 계층형 정규화 엔진 (`.prompts`)
- **순수 기본 명령어 단일화**:
  - 스택 번호(`switch 1~4`), 디스크 파티션(`flash1:`), 파이프 필터링 옵션(`_ exclude ...`, `_ in Pool Total`) 등 지저분한 파일명 접미사를 전면 제거하고, 순수 기본 명령어 대표 템플릿(50종)으로 완전 단일화.
- **범용 파이프 스트리핑 및 계층형 매칭 엔진 (`PromptManager.cs`)**:
  - 수집된 로그에 엔지니어별 파이프 필터(`| exclude`, `| include` 등)나 축약어가 포함되어 있더라도, 본체 명령어를 자동 추출 및 정규화(Exact ➔ Sanitized ➔ Normalized Base Fallback)하여 대표 프롬프트로 100% 매핑 보장.
- **단호한 기술 지시체 (`~하라`, `~할 것`) 표준화**:
  - AI의 도구 호출 누락 및 사족 생성을 원천 차단하기 위해 모든 프롬프트의 존댓말을 건조하고 명확한 기술 지시체로 전수 개편.
- **신규 프롬프트 가이드라인 제공**:
  - 향후 신규 명령어 추가 시 준수해야 할 표준 규칙을 [`.prompts/PROMPT_GUIDE.md`](./.prompts/PROMPT_GUIDE.md)로 문서화하여 일관된 유지보수 지원.

### 5. 사용자 편의 인터페이스 (UI/UX)
- **카드 아코디언 (접기 / 펼치기)**: 1차 요약 및 2차 교차 진단 카드를 헤더 클릭으로 독립적으로 접고 펼침 가능.
- **세로 스크롤 & 고대비 다크 테마**: 화면 높이에 맞춰 부드럽게 세로 스크롤바가 자동 생성되며, 선명한 고대비 텍스트 가독성 제공.
- **대화형 AI 어시스턴트 (`AiChat`)**: 조회 중인 장비의 로그 콘텍스트를 기반으로 실시간 질의응답 지원.

---

## 📂 프로젝트 구조

```
01_LogCompare/
├── LogClient/                  # Blazor WebAssembly 프론트엔드 SPA
│   └── Components/             # LogSummaryPanel, LogDiff, ShowLoggingPanel, AiChat 등
├── LogAPI/                     # ASP.NET Core Minimal API 백엔드
├── LogWorker/                  # Semantic Kernel + Ollama 2단계 계층형 AI 분석 데몬
├── LogLibrary/                 # 공통 엔티티(EF Core), DbContext, 파싱 엔진
├── .prompts/                   # AI 분석 프롬프트 저장소
│   ├── Summary/                # 1차 명령어별 대표 요약 프롬프트 (순수 기본형 45종)
│   ├── Diagnosis/              # 2차 심층 교차 진단 프롬프트 (default 및 항목별 특화 5종)
│   ├── system.txt              # 환각 방지 절대 규칙 및 공통 시스템 프롬프트
│   └── PROMPT_GUIDE.md         # 신규 프롬프트 생성 및 관리 표준 가이드라인
├── .raw_logs/                  # 워커가 자동 수집할 원본 로그 저장 디렉터리
├── docker-compose.yml          # 전체 서비스 원클릭 오케스트레이션 정의
└── PRD.md                      # 제품 상세 요구사항 정의서
```

---

> 상세한 시스템 아키텍처, 데이터베이스 스키마 및 비기능 요구사항은 [PRD.md](./01_LogCompare/PRD.md)를 참조하세요.
