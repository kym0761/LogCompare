using LogLibrary.DB;
using LogLibrary.Classes.DB_Data;
using LogAPI.Classes.DB_Data;
using LogWorker.Providers;
using LogWorker.Managers;
using LogWorker.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace LogWorker.Workers;

public class Worker(
    ILogger<Worker> logger, 
    IServiceScopeFactory scopeFactory, 
    IPromptManager promptManager, 
    ILlmResolver llmResolver) : BackgroundService
{
    // LLM 사용 시 1회 호출 후 대기 시간 (로컬 LLM 과부하 방지용)
    private int DelayMs => llmResolver.GetActiveOptions().WorkerDelayMs;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 워커 시작 시 프롬프트 로드 (앱 수명 주기 동안 단 1번만 실행)
        promptManager.LoadPrompts();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var activeOptions = llmResolver.GetActiveOptions();
                var activeProvider = llmResolver.GetActiveProvider();
                var settings = activeProvider.GetExecutionSettings();

                logger.LogInformation($"=== 워커 배치 작업 시작: {DateTimeOffset.Now} ===");

                // [1단계] 로그 수집 및 적재 (Raw Log Ingestion)
                using (var parseScope = scopeFactory.CreateScope())
                {
                    ILogIngestionManager logIngester = parseScope.ServiceProvider.GetRequiredService<ILogIngestionManager>();
                    await logIngester.ProcessRawLogsAsync(stoppingToken);
                }

                // [전처리] 분석 대상 설비 목록 조회
                List<string> deviceList;
                using (var dbScope = scopeFactory.CreateScope())
                {
                    LogDbContext? db = dbScope.ServiceProvider.GetRequiredService<LogDbContext>();
                    deviceList = await db.Devices.Select(d => d.DeviceName).ToListAsync(stoppingToken);
                }

                if (deviceList.Count == 0)
                {
                    logger.LogInformation("등록된 설비가 없습니다. 다음 배치까지 대기합니다.");
                    await Task.Delay(TimeSpan.FromMinutes(activeOptions.RetryIntervalMinutes), stoppingToken);
                    continue;
                }

                // LLM 분석이 활성화된 경우에만 2단계와 3단계를 수행합니다.
                if (activeOptions.EnableLLMAnalysis)
                {
                    logger.LogInformation("LLM 분석 단계 시작...");

                    try
                    {
                        // [안내] 현재는 명령어 기반 범용 프롬프트 체계로 파이프라인이 동작 중입니다.
                        // 추후 설비 모델별 프롬프트 커스터마이징이 필요할 경우 본 단계를 다시 활성화해야 하므로 데드 코드로 간주하지 마십시오.
                        // await SyncDeviceModelsAsync(deviceList, activeProvider, settings, stoppingToken);
                        // await SyncDeviceStackCountsAsync(deviceList, activeProvider, settings, stoppingToken);
                        
                        // [2단계 계층형 로그 자율 요약 (1차 요약 및 2차 교차 진단)]
                        await GenerateSummariesAsync(deviceList, activeProvider, settings, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "LLM 분석 단계(Phase 1 & Phase 2) 실행 중 예외가 발생했습니다. (수집 및 적재 기능은 유지됩니다.)");
                    }
                }
                else
                {
                    logger.LogInformation("LLM 분석 기능(EnableLLMAnalysis)이 비활성화되어 있어 단계를 건너뜁니다.");
                }

                
                logger.LogInformation("=== 모든 로그 분석 완료. 다음 배치까지 대기합니다. ===");

                // 설정 파일 주기에 따른 배치 대기 시간 적용
                await Task.Delay(TimeSpan.FromMinutes(activeOptions.BatchIntervalMinutes), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("워커 서비스가 중단 요청을 받아 정상적으로 종료됩니다.");
            }
            catch (Exception ex)
            {
                var activeOptions = llmResolver.GetActiveOptions();
                logger.LogError(ex, $"백그라운드 워커 실행 중 예상치 못한 치명적인 오류가 발생했습니다. {activeOptions.RetryIntervalMinutes}분 후 재시도합니다.");
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(activeOptions.RetryIntervalMinutes), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // 대기 중 종료 요청 처리
                }
            }
        }
    }

    #region Phase 1 : 모델명 식별 및 매핑
    // [안내] 현재는 범용 프롬프트 체계로 동작 중이며, 향후 모델별 프롬프트 커스텀 필요 시 재활성화하여 사용합니다. (데드 코드 아님)
    private async Task SyncDeviceModelsAsync(List<string> deviceList, IKernelProvider activeProvider, OpenAIPromptExecutionSettings settings, CancellationToken stoppingToken)
    {
        logger.LogInformation("--- [Phase 1] 메타데이터 동기화 시작 ---");

        foreach (var devicename in deviceList)
        {
            // 장비 하나를 분석할 때마다 완전히 새롭고 깨끗한 도구함(Scope)을 엽니다.
            using var scope = scopeFactory.CreateScope();
            LogDbContext? db = scope.ServiceProvider.GetRequiredService<LogDbContext>();

            // 이미 모델 정보가 존재한다면 식별 단계를 스킵합니다.
            bool isAlreadyMapped = await db.DeviceToModelMappings
                .AnyAsync(m => m.DeviceName == devicename && !string.IsNullOrEmpty(m.ModelName), stoppingToken);

            if (isAlreadyMapped)
            {
                logger.LogInformation($"[{devicename}] 이미 모델 정보가 등록되어 있어 식별을 건너뜁니다.");
                continue;
            }

            // IKernelProvider를 통해 이 스코프에 특화된 플러그인이 탑재된 깨끗한 커널을 생성합니다.
            Kernel kernel = activeProvider.CreateKernel(scope.ServiceProvider);

            bool hasShowVersionLog = await db.RawLogs.AnyAsync(r => r.DeviceName == devicename && r.Command.Contains("show version"), stoppingToken);
            hasShowVersionLog |= await db.RawLogs.AnyAsync(r => r.DeviceName == devicename && r.Command.Contains("show ver"), stoppingToken);
            
            // show version 로그가 없는 경우 모델 식별이 불가능하므로 건너뜁니다.
            if (!hasShowVersionLog)
            {
                continue;
            }

            logger.LogInformation($"[{devicename}] Phase 1: 모델명 식별을 위한 show version 분석 중...");

            string modelSearchPrompt = 
                    $"""
                    Cisco 네트워크 스위치 장비 기준이야.
                    다음 단계를 순서대로 실행해 줘:
                    1. 'GetLatestLogDate' 함수를 호출하여 {devicename}의 가장 최근 로그 날짜를 확인해.
                    2. 알아낸 날짜와 'show version' (혹은 'show ver') 명령어를 사용해 'GetRawLogContent' 함수를 호출하여 로그 본문을 가져와.
                    3. 로그 본문에서 하드웨어 모델명(예: 3750, 3850, 9300 등)을 찾아내. 모델명은 "cisco WS" 혹은 "cisco C"로 시작할 것이다. 이외의 것이 있다면 어쩔 수 없으니 적당히 모델명으로 보이는 4자리 숫자를 확인하여 가져와라. 
                    4. 마지막으로 'UpdateDeviceModel' 함수를 호출해서 DB에 장비와 모델 매핑을 업데이트해 줘.
                    """;

            logger.LogInformation($"[{devicename}] 적용할 프롬프트:\n{modelSearchPrompt}");

            try
            {
                await kernel.InvokePromptAsync(modelSearchPrompt, new(settings), cancellationToken: stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"[{devicename}] 모델명 식별 중 오류 발생");
            }

            // 로컬 LLM 과부하 방지를 위한 짧은 딜레이
            await Task.Delay(DelayMs, stoppingToken);
        }
    }
    #endregion

    #region Phase 1.5 : 스택 멤버 대수 식별 및 매핑
    // show switch 로그를 분석하여 물리적인 스택 스위치 총 대수를 식별하고 저장합니다.
    private async Task SyncDeviceStackCountsAsync(List<string> deviceList, IKernelProvider activeProvider, OpenAIPromptExecutionSettings settings, CancellationToken stoppingToken)
    {
        logger.LogInformation("--- [Phase 1.5] 스택 정보 식별 및 동기화 시작 ---");

        foreach (var devicename in deviceList)
        {
            // 장비 하나를 분석할 때마다 완전히 새롭고 깨끗한 도구함(Scope)을 엽니다.
            using var scope = scopeFactory.CreateScope();
            LogDbContext? db = scope.ServiceProvider.GetRequiredService<LogDbContext>();

            // 스택 개수가 이미 분석되어 0보다 크거나, 혹은 DB에 show switch 로그 자체가 없는 경우는 건너뜁니다.
            bool isAlreadyAnalyzed = await db.Devices
                .AnyAsync(d => d.DeviceName == devicename && d.StackCount > 0, stoppingToken);

            if (isAlreadyAnalyzed)
            {
                logger.LogInformation($"[{devicename}] 이미 스택 대수가 식별되어 있어 건너뜁니다.");
                continue;
            }

            bool hasShowSwitchLog = await db.RawLogs.AnyAsync(r => r.DeviceName == devicename && r.Command.Contains("show switch"), stoppingToken);
            hasShowSwitchLog |= await db.RawLogs.AnyAsync(r => r.DeviceName == devicename && r.Command.Contains("show sw"), stoppingToken);

            if (!hasShowSwitchLog)
            {
                // show switch 로그가 없으면 스택 판독을 할 수 없으므로 기본값 1 유지
                continue;
            }

            logger.LogInformation($"[{devicename}] Phase 1.5: 스택 대수 식별을 위한 show switch 분석 중...");

            // IKernelProvider를 통해 이 스코프에 특화된 플러그인이 탑재된 깨끗한 커널을 생성합니다.
            Kernel kernel = activeProvider.CreateKernel(scope.ServiceProvider);

            string stackSearchPrompt = 
                    $"""
                    Cisco 네트워크 스위치 장비 기준이야.
                    다음 단계를 순서대로 실행해 줘:
                    1. 'GetLatestLogDate' 함수를 호출하여 {devicename}의 가장 최근 로그 날짜를 확인해.
                    2. 알아낸 날짜와 'show switch' (혹은 'show sw') 명령어를 사용해 'GetRawLogContent' 함수를 호출하여 로그 본문을 가져와.
                    3. 가져온 로그 본문을 판독하여, 현재 스택으로 정상 구성 및 작동 중인 스위치(Ready/Active 상태의 스위치 개수)가 총 몇 대인지 알아내. (예: Switch 1~4가 나열되어 있으면 4)
                    4. 마지막으로 'UpdateDeviceStackCount' 함수를 호출해서 알아낸 스택 개수(stackCount)를 DB에 업데이트해 줘.
                    """;

            logger.LogInformation($"[{devicename}] 적용할 프롬프트:\n{stackSearchPrompt}");

            try
            {
                await kernel.InvokePromptAsync(stackSearchPrompt, new(settings), cancellationToken: stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"[{devicename}] 스택 대수 식별 중 오류 발생");
            }

            // 로컬 LLM 과부하 방지를 위한 짧은 딜레이
            await Task.Delay(DelayMs, stoppingToken);
        }
    }
    #endregion

    #region Phase 2 : 2단계 계층형 로그 분석 (1차 요약 및 2차 정밀 교차 진단)
    // 1차로 명령어별 요약을 수행하고, 이상 징후 감지 시 타 명령어 요약을 교차 대조하여 2차 정밀 진단을 수행합니다.
    private async Task GenerateSummariesAsync(List<string> deviceList, IKernelProvider activeProvider, OpenAIPromptExecutionSettings settings, CancellationToken stoppingToken)
    {
        logger.LogInformation("--- [Phase 2] 계층형 로그 요약 및 교차 진단 시작 ---");

        foreach (string deviceName in deviceList)
        {
            // 장비별로 새로운 스코프와 커널을 생성
            using IServiceScope scope = scopeFactory.CreateScope();
            LogDbContext db = scope.ServiceProvider.GetRequiredService<LogDbContext>();
            Kernel kernel = activeProvider.CreateKernel(scope.ServiceProvider);

            CiscoDevice? deviceEntity = await db.Devices.FirstOrDefaultAsync(d => d.DeviceName == deviceName, stoppingToken);
            int stackCount = deviceEntity?.StackCount ?? 1;
            if (stackCount <= 0)
            {
                stackCount = 1;
            }

            DateOnly scanStartDate = DateOnly.FromDateTime(DateTime.Today.AddMonths(-6));
            int maxProcessPerCommand = 5;

            // 모델 매핑에 의존하지 않고, DB의 RawLogs에 실제로 적재된 해당 장비의 최근 6개월 명령어 목록을 직접 조회
            List<string> targetCommands = await db.RawLogs
                .Where(r => r.DeviceName == deviceName && r.Date >= scanStartDate)
                .Select(r => r.Command)
                .Distinct()
                .ToListAsync(stoppingToken);

            if (targetCommands.Count == 0)
            {
                logger.LogInformation($"[{deviceName}] 최근 6개월 내 수집된 원본 로그가 없어 요약 분석을 건너뜁니다.");
                continue;
            }

            logger.LogInformation($"[{deviceName}] 계층형 로그 분석 시작 (스택 대수: {stackCount}대, 대상 명령어: {targetCommands.Count}종류)");

            // ==========================================
            // [Stage 1] 1차 명령어별 독립 요약 (LogSummary)
            // ==========================================
            foreach (string cmd in targetCommands)
            {
                var unsummarizedLogs = await db.RawLogs
                    .Where(r => r.DeviceName == deviceName && r.Command == cmd && r.Date >= scanStartDate)
                    .Where(r => !db.LogSummaries.Any(s => s.DeviceName == r.DeviceName && 
                                                          s.Command == r.Command && 
                                                          s.Date == r.Date))
                    .OrderBy(r => r.Date)
                    .Take(maxProcessPerCommand)
                    .Select(r => new { r.Date })
                    .ToListAsync(stoppingToken);

                if (unsummarizedLogs.Count == 0)
                {
                    continue;
                }

                logger.LogInformation($"[{deviceName}] - '{cmd}' 1차 미요약 건 발견: {unsummarizedLogs.Count}건 (1차 요약 가동)");

                foreach (var logInfo in unsummarizedLogs)
                {
                    string targetDateStr = logInfo.Date.ToString("yyyy-MM-dd");
                    logger.LogInformation($"   -> [Stage 1] 1차 요약 에이전트 가동 ({cmd}, 날짜: {targetDateStr})");

                    string basePrompt = promptManager.GetSummaryPrompt("", cmd, deviceName);

                    string agentMissionPrompt = 
                        $"""
                        {basePrompt}

                        당신은 [{deviceName}] 네트워크 장비(스택 스위치 총 대수: {stackCount}대)의 '{cmd}' 로그를 점검하는 AI 분석 엔지니어입니다.
                        대상 날짜는 [{targetDateStr}] 입니다.

                        아래 단계에 따라 당신에게 부여된 데이터베이스 도구들을 활용하여 1차 분석을 완료하십시오:

                        1. 'GetPastSummaries' 함수를 호출하여 이 장비/명령어의 과거 요약 이력을 조회하십시오. (매개변수: deviceName="{deviceName}", command="{cmd}", dateStr="{targetDateStr}")
                        2. 'GetRawLogContent' 함수를 호출하여 분석할 원본 로그를 가져오십시오. (매개변수: deviceName="{deviceName}", command="{cmd}", dateStr="{targetDateStr}")
                        3. 가져온 원본 로그를 요약하고 과거 상태와 비교하여 이상 징후 여부 및 상태 심각도(Normal / Warning / Critical)를 판정하십시오:
                           - Normal: 정상 작동 중, 특이 결함이나 장애 징후 없음.
                           - Warning: 주의 필요 (예: 자원 사용률 상승, 간헐적 패킷 드롭, 포트 Flap, 경미한 시스템 오류 등).
                           - Critical: 즉시 점검 필요 (예: CPU/메모리 임계치 초과, 포트 다운, 링크 단절, 전원/팬 장애, 치명적 시스템 오류 등).
                        4. [환각 방지 원칙]:
                           - 반드시 'GetRawLogContent'로 가져온 원본 로그 텍스트에 직접 명시된 사실과 수치에만 근거하십시오.
                           - 원본 로그에 특정 정보가 명시되어 있지 않거나 누락된 경우, 절대 정상으로 임의 추측하지 말고 '로그 미기재(확인 불가)'로 표기하십시오.
                        5. 분석이 완료되면 반드시 'SaveDeviceSummary' 함수를 호출하여 결과를 DB에 저장하십시오.
                           - SaveDeviceSummary 호출 매개변수: 
                             * deviceName: "{deviceName}"
                             * command: "{cmd}"
                             * dateStr: "{targetDateStr}"
                             * summaryText: (당신이 작성한 요약 및 시계열 분석 결과 텍스트)
                             * severity: ("Normal", "Warning", "Critical" 중 판정된 상태 심각도)
                        """;

                    try
                    {
                        await kernel.InvokePromptAsync(agentMissionPrompt, new(settings), cancellationToken: stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, $"[{deviceName}] '{cmd}' ({targetDateStr}) 1차 요약 수행 중 오류 발생");
                    }

                    db.ChangeTracker.Clear();
                    await Task.Delay(DelayMs, stoppingToken);
                }
            }

            // ==========================================
            // [Stage 2] 2차 정밀 교차 진단 (LogDiagnosis)
            // ==========================================
            // 조건:
            // 1. 해당 설비의 6개월 이내 요약본 중 Severity != "Normal" (Warning 또는 Critical)인 건
            // 2. 아직 LogDiagnoses에 진단 결과가 등록되지 않은 건
            // 3. [선행 조건 필수] 해당 날짜의 대상 명령어 1차 요약이 전부 완료된 경우에만 진입
            List<DateOnly> candidateDates = await db.LogSummaries
                .Where(s => s.DeviceName == deviceName && s.Date >= scanStartDate && s.Severity != "Normal")
                .Where(s => !db.LogDiagnoses.Any(d => d.DeviceName == s.DeviceName && d.Date == s.Date && d.Command == s.Command))
                .Select(s => s.Date)
                .Distinct()
                .OrderBy(d => d)
                .ToListAsync(stoppingToken);

            foreach (DateOnly candidateDate in candidateDates)
            {
                // 선행 조건 검증: 해당 날짜에 해당 장비의 활성 명령어 RawLogs 중 아직 LogSummaries에 등록되지 않은 건이 있는지 확인
                bool hasPendingStage1 = await db.RawLogs
                    .Where(r => r.DeviceName == deviceName && r.Date == candidateDate && targetCommands.Contains(r.Command))
                    .AnyAsync(r => !db.LogSummaries.Any(s => s.DeviceName == r.DeviceName && s.Date == r.Date && s.Command == r.Command), stoppingToken);

                if (hasPendingStage1)
                {
                    logger.LogInformation($"[{deviceName}] ({candidateDate:yyyy-MM-dd}) 아직 1차 요약이 완료되지 않은 명령어가 존재하여 2차 정밀 진단을 보류합니다.");
                    continue;
                }

                // 1차 요약이 모두 끝난 날짜이므로 이상 항목들에 대해 2차 교차 진단 진행
                var anomalySummaries = await db.LogSummaries
                    .Where(s => s.DeviceName == deviceName && s.Date == candidateDate && s.Severity != "Normal")
                    .Where(s => !db.LogDiagnoses.Any(d => d.DeviceName == s.DeviceName && d.Date == s.Date && d.Command == s.Command))
                    .ToListAsync(stoppingToken);

                foreach (var anomaly in anomalySummaries)
                {
                    string targetDateStr = candidateDate.ToString("yyyy-MM-dd");
                    logger.LogInformation($"   -> [Stage 2] 2차 정밀 교차 진단 에이전트 가동 ({anomaly.Command}, 상태: {anomaly.Severity}, 날짜: {targetDateStr})");

                    string diagnosisMissionPrompt = promptManager.GetDiagnosisPrompt(
                        "",
                        anomaly.Command,
                        deviceName,
                        targetDateStr,
                        anomaly.Severity,
                        anomaly.SummaryText);

                    try
                    {
                        await kernel.InvokePromptAsync(diagnosisMissionPrompt, new(settings), cancellationToken: stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, $"[{deviceName}] '{anomaly.Command}' ({targetDateStr}) 2차 정밀 교차 진단 수행 중 오류 발생");
                    }

                    db.ChangeTracker.Clear();
                    await Task.Delay(DelayMs, stoppingToken);
                }
            }
        }
    }
    #endregion
}