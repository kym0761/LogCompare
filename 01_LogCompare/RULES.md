# Project Rules for AI

이 파일은 각 프로젝트를 수정할 때 **절대 함부로 변경하거나 임의로 삭제해서는 안 되는 금지 조항(DO NOT)**을 정의합니다. AI 에이전트는 코드 수정 작업을 진행하기 전에 이 체크리스트를 반드시 대조하십시오.

**[메타 규칙] 사용자 요청과 규칙 간의 충돌 조율**:
- 사용자의 명시적인 수정 요청 사항이 이 파일에 적힌 금지 조항이나 필수 규칙과 충돌하여 정상적인 작업이 방해되거나 구현이 불가능하다고 판단되는 경우, **독단적으로 작업을 진행하지 말고 수정을 개시하기 전에 반드시 사용자에게 상황을 알린 뒤 다음 선택지를 제시하여 선택하도록 질문하십시오:**
  1. **"이번만 규칙을 예외로 무시하고 사용자의 명령을 우선 수행할지?"**
  2. **"이 규칙이 더 이상 유효하지 않으므로 `RULES.md` 파일 자체를 고치고 수정할지?"**
- 사용자가 선택한 방향에 대응하는 확인을 마친 후 작업을 수행해야 합니다.

---

## 1. LogClient (Blazor WebAssembly)

- **[금지] 스크립트 경로 단순화 금지**:
  - `LogClient/wwwroot/index.html` 파일 내의 다음 스크립트 태그를 절대 일반 `.js` 경로로 단순화하거나 변경하지 마십시오.
    ```html
    <script src="_framework/blazor.webassembly#[.{fingerprint}].js"></script>
    ```
- **[금지] 프로젝트 파일의 플레이스홀더 옵션 제거 금지**:
  - `LogClient.csproj`에 정의된 `<OverrideHtmlAssetPlaceholders>true</OverrideHtmlAssetPlaceholders>` 옵션을 임의로 지우거나 `false`로 바꾸지 마십시오. 이 옵션이 해제되면 캐시 버스팅이 작동하지 않고 배포 환경에서 구동 프리징이 생깁니다.

---

## 2. LogWorker (Background Service)

- **[금지] ChangeTracker.Clear() 누락 금지**:
  - `LogIngestionManager.cs` 등에서 대량 로그 저장 작업(`SaveChangesAsync`)이 완료된 직후 또는 예외(`DbUpdateException`) 캐치 블록 내에 삽입된 `db.ChangeTracker.Clear()` 호출을 절대 삭제하지 마십시오. 삭제 시 수백 MB 단위의 심각한 메모리 누수(OOM)가 발생합니다.
- **[금지] 중복 로그 사전 필터링 생략 금지**:
  - `RawLogs`에 신규 로그를 적재할 때, 기존 적재된 로그와의 중복 여부를 DB에서 먼저 대조하고 필터링하는 로직을 절대 건너뛰지 마십시오. (단순히 `AddRange`만 실행하면 Unique 인덱스 위반 에러로 전체 트랜잭션이 중복 데이터 때문에 롤백됩니다.)
- **[금지] 파일명 파싱 시 도트(.) 분할 단순화 금지**:
  - 로그 파일명 파싱(`TryParseFileName`) 시 파일명에 마침표(`.`)가 여러 개 포함될 수 있습니다. (예: `SW-CORE-01.20260525.txt`)
  - 단순히 첫 번째 점(`.`)으로 무조건 잘라서 장비명으로 판단하는 코드로 절대 바꾸지 마십시오. 날짜 형식(`yyyyMMdd`)이 나오기 전까지의 모든 토큰을 병합해야 온전한 장비 이름(`SW-CORE-01`)을 추출할 수 있습니다.
- **[금지] ExecuteAsync 루프의 예외 복구(Retry) 제거 금지**:
  - `Worker.cs`의 `ExecuteAsync` 내부에 구현된 상위 `try-catch` 및 예외 발생 시 대기 후 재시도(Retry) 구조를 절대 제거하지 마십시오. 에러 발생 시 백그라운드 데몬 자체가 영구 종료되는 장애가 발생합니다.

---

## 3. LogLibrary & Database Schema (PostgreSQL)

- **[금지] EF Core의 기본 Add로 Devices 인서트 변경 금지**:
  - 새로운 장비를 등록하는 DB 인서트 코드 작성 시, `db.Devices.Add()` 형태로 절대 변경하지 마십시오.
  - 반드시 아래와 같이 `ON CONFLICT ("DeviceName") DO NOTHING`을 명시한 원시 SQL을 이용해 동시성 등록 문제를 예방해야 합니다:
    ```csharp
    await db.Database.ExecuteSqlRawAsync(
        @"INSERT INTO ""Devices"" (""DeviceName"", ""Description"", ""CreatedAt"") 
          VALUES ({0}, {1}, {2}) 
          ON CONFLICT (""DeviceName"") DO NOTHING;", 
        deviceName, description, createdAt);
    ```

---

## 4. LogAPI (Web API)

- **[금지] UseHttpsRedirection 무조건 적용 금지**:
  - `Program.cs`에서 `app.UseHttpsRedirection()` 코드가 `OperatingSystem.IsWindows()` 조건 없이 모든 환경에 강제로 적용되도록 변경하지 마십시오. 도커 컨테이너 내부(리눅스 환경)에서는 SSL 인증서가 없으므로 해당 리디렉션이 동작하면 통신이 끊어집니다.
- **[금지] 업로드 API에 CSRF/Antiforgery 활성화 금지**:
  - 파일 업로드용 POST API인 `/api/logs/upload` 뒤의 `.DisableAntiforgery()` 설정을 절대 제거하지 마십시오. 제거 시 외부 클라이언트 및 자바스크립트 업로드 시 요청이 거부됩니다.
- **[금지] 무제한 로그 조회 API 활성화 금지**:
  - 대량 로그를 그대로 메모리에 올려 직렬화하는 엔드포인트(예: 페이징 없는 전체 조회)를 절대 주석 제거하거나 부활시키지 마십시오. OOM으로 서버가 바로 마비될 수 있습니다.

---

## 5. 공통 코드 스타일 규칙 (General Coding Standards)

- **[필수] 모든 제어문 중괄호 `{}` 의무 사용**:
  - `if`, `else if`, `else`, `for`, `foreach`, `while` 등 모든 제어문의 본문 코드가 단 **1줄이더라도 중괄호 `{}`를 생략하지 말고 무조건 사용**하십시오.
  - 자바스크립트나 C# 등 언어 종류에 상관없이, 한 줄짜리 구문이라도 중괄호 없이 한 줄로 작성하는 것을 금지합니다.
    - *잘못된 예*: 
      ```csharp
      if (deviceName == null) return;
      ```
    - *올바른 예*: 
      ```csharp
      if (deviceName == null) 
      {
          return;
      }
      ```
- **[필수] 로컬 변수 소문자 시작 (camelCase)**:
  - 함수/메서드 내부에 선언되는 모든 로컬 변수(Local Variable)의 명칭은 **무조건 앞글자를 소문자로 시작(camelCase)**하여 사용하십시오.
  - 클래스의 멤버 프로퍼티나 필드 명칭과의 혼동을 막기 위해, 로컬 변수 이름에 대문자로 시작(PascalCase)하는 형태의 명칭을 명명하는 것을 절대 금지합니다.
    - *잘못된 예*: `var DeviceName = "Router-A";`
    - *올바른 예*: `var deviceName = "Router-A";`
- **[필수] 비동기 메서드 Async 접미사 사용**:
  - `Task` 또는 `Task<T>`를 반환하는 모든 비동기 메서드의 명칭 끝에는 **반드시 `Async` 접미사를 부여**해야 합니다. (예외: Web API의 HTTP 엔드포인트 맵핑 메서드나 컨트롤러 액션 메서드는 제외)
    - *잘못된 예*: `public async Task ProcessData()`
    - *올바른 예*: `public async Task ProcessDataAsync()`
- **[필수] Nullable 느낌표(!) 억제 연산자 오남용 금지**:
  - 컴파일러의 Null 관련 경고를 우회하기 위해 변수 뒤에 널 억제 연산자(`!`)를 붙여 에러를 숨기지 마십시오.
  - 반드시 실제 `null` 분기 처리 검사 및 적절한 대체값 지정(예: null 병합 연산자 `??`, 조건부 멤버 접근 연산자 `?.`)을 통해 런타임 NullReferenceException 예외를 차단하십시오.
    - *잘못된 예*: `var len = device.Name!.Length;`
    - *올바른 예*: `var len = device.Name?.Length ?? 0;`
- **[필수] var 키워드와 명시적 타입 구분 규정**:
  - 변수 선언 시 `var` 키워드는 **대입하는 우변을 통해 타입을 한눈에 즉시 식별할 수 있는 상황**(예: `new` 인스턴스 생성, 명시적 캐스팅 등)에만 사용하십시오.
  - 기본 원시 타입(`int`, `string`, `bool`, `double` 등)이나 우변에 메서드 호출만 정의되어 타입을 직접 유추하기 힘든 경우에는 반드시 명시적 타입을 작성하십시오.
    - *잘못된 예*: `var count = 10; var name = GetName();`
    - *올바른 예*: `int count = 10; string name = GetName(); var list = new List<string>();`
