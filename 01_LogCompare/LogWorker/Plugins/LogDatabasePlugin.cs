using LogAPI.Classes.DB_Data;
using LogLibrary.DB;
using LogLibrary.Classes.DB_Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace LogWorker.Plugins
{
    //program.cs에서
    //kernelBuilder.Plugins.AddFromType<PluginClass>();
    //builder.Services.AddTransient<PluginClass>();
    //코드로 플러그인을 장착하면 LLM이 함수를 자동으로 인식해서 필요할 때마다 호출할 수 있습니다.
    public class LogDatabasePlugin(LogDbContext db, ILogger<LogDatabasePlugin> logger)
    {

        //참고할 요약 이력 3개 가져오기
        //이 3가지 정보 이력을 확인하여 다음 요약본을 생성할 때 참고함.
        //만약 요약본이 없다면 프롬프트에서 정의한 내용에 대한 요약을 진행할 것으로 예상.
        [KernelFunction("GetPastSummaries")]
        [Description("특정 설비/명령어의 기준 날짜 이전 과거 요약 이력(최대 3건) 조회")]
        public async Task<string> GetPastSummariesAsync(
            [Description("설비명 (예: SW-CORE-01)")] string deviceName, 
            [Description("명령어 (예: show version)")] string command,
            [Description("기준 날짜 (yyyy-MM-dd, 이전 데이터만 조회)")] string dateStr)
        {
            int num = 3;

            if (!DateOnly.TryParse(dateStr, out var date))
            {
                return "에러: 기준 날짜 형식이 잘못되었습니다. yyyy-MM-dd 형식을 사용하십시오.";
            }

            // DB에서 데이터를 가져올 때(Select) 바로 구분선까지 포함된 문자열로 만듭니다.
            var summaries = await db.LogSummaries
                .Where(s => s.DeviceName == deviceName && s.Command == command && s.Date < date)
                .OrderByDescending(s => s.Date)
                .Take(num)
                .Select(s => $"[{s.Date:yyyy-MM-dd}] 요약: {s.SummaryText}\n==========")
                .ToListAsync();

            if (!summaries.Any())
                return "과거 기록이 없습니다.";

            // 이미 구분선이 포함된 문자열 리스트이므로 줄바꿈으로 합치기만 하면 됩니다.
            return string.Join("\n", summaries);
        }

        //RawLog를 하나 가져온다
        [KernelFunction("GetRawLogContent")]
        [Description("특정 장비/날짜/명령어의 원본 로그 조회")]
        public async Task<string> GetRawLogContentAsync(
            [Description("장비명 (예: SW-CORE-01)")] string deviceName,
            [Description("명령어 (예: show version)")] string command,
            [Description("날짜 (yyyy-MM-dd)")] string dateStr)
        {
            if (!DateOnly.TryParse(dateStr, out var date))
            {
                return "날짜 형식이 잘못되었습니다. yyyy-MM-dd 형식을 사용하세요.";
            }

            // [WARNING] 현재 l.Command == command 와 같이 엄격한 일치 비교를 수행하고 있습니다.
            // Cisco 장비 특성상 명령어 축약형(sh ver, show ver, show version 등)이나 후행 공백이 존재할 경우
            // LLM이 로그 본문을 조회하지 못하는 문제가 발생할 수 있습니다.
            // 향후 필요시 Contains나 공백 제거 트리밍 로직을 도입하여 비교 방식을 완화해야 합니다.
            var rawLog = await db.RawLogs
                .Where(l => l.DeviceName == deviceName &&
                            l.Command == command &&
                            l.Date == date)
                .Select(l => l.Content) // 로그 본문 필드 명칭에 맞춰 수정 필요
                .FirstOrDefaultAsync();

            return rawLog ?? "해당 조건의 원본 로그를 찾을 수 없습니다.";
        }

        //원본 로그 중 가장 최근에 저장된 날짜
        [KernelFunction("GetLatestLogDate")]
        [Description("원본 로그의 가장 최근 저장 날짜 확인")]
        public async Task<string> GetLatestLogDateAsync(
            [Description("장비명 (전체 조회시 null 또는 생략)")] string? deviceName = null)
        {
            // 쿼리 시작
            var query = db.RawLogs.AsQueryable();

            if (!string.IsNullOrEmpty(deviceName))
            {
                query = query.Where(l => l.DeviceName == deviceName);
            }

            // 가장 최근 날짜 1건만 가져오기
            var latestDate = await query
                .OrderByDescending(l => l.Date)
                .Select(l => l.Date)
                .FirstOrDefaultAsync();

            if (latestDate == default)
            {
                return "기록된 로그가 없습니다.";
            }

            return latestDate.ToString("yyyy-MM-dd");
        }

        //요약본 저장
        [KernelFunction("SaveDeviceSummary")]
        [Description("로그 요약본 데이터베이스 저장/업데이트")]
        public async Task<string> SaveDeviceSummaryAsync(
            [Description("설비명 (예: SW-CORE-01)")] string deviceName,
            [Description("명령어 (예: show version)")] string command,
            [Description("요약 텍스트")] string summaryText,
            [Description("로그 날짜 (yyyy-MM-dd)")] string dateStr,
            [Description("상태 심각도 (Normal, Warning, Critical 중 하나, 기본값: Normal)")] string severity = "Normal")
        {
            if (!DateOnly.TryParse(dateStr, out var date))
            {
                return "날짜 형식이 잘못되었습니다. 저장에 실패했습니다.";
            }

            // 방어적 심각도 정규화 (LLM 응답의 공백/소문자/한글 등 보정)
            string normalizedSeverity = severity?.Trim().ToLowerInvariant() switch
            {
                "warning" or "주의" or "경고" => "Warning",
                "critical" or "위험" or "치명" => "Critical",
                _ => "Normal"
            };

            try
            {
                // 해당 날짜/장비/명령어의 기존 요약본이 있는지 확인
                var existingSummary = await db.LogSummaries
                    .FirstOrDefaultAsync(s => s.DeviceName == deviceName &&
                                             s.Command == command &&
                                             s.Date == date);

                if (existingSummary != null)
                {
                    // 기존 데이터가 있으면 업데이트
                    existingSummary.SummaryText = summaryText;
                    existingSummary.Severity = normalizedSeverity;
                    existingSummary.UpdatedAt = DateTime.UtcNow;
                    db.LogSummaries.Update(existingSummary);
                }
                else
                {
                    // 없으면 새로운 레코드 생성
                    var newSummary = new LogSummary
                    {
                        DeviceName = deviceName,
                        Command = command,
                        SummaryText = summaryText,
                        Severity = normalizedSeverity,
                        Date = date,
                        UpdatedAt = DateTime.UtcNow
                    };
                    await db.LogSummaries.AddAsync(newSummary);
                }

                await db.SaveChangesAsync(); // DB 반영
                return $"{deviceName}의 {dateStr} 요약본(상태: {normalizedSeverity})이 성공적으로 저장되었습니다.";
            }
            catch (Exception ex)
            {
                return $"DB 저장 중 오류 발생: {ex.Message}";
            }
        }

        // 특정 설비/날짜의 타 명령어 1차 요약 목록 전체 조회
        [KernelFunction("GetPeerCommandSummaries")]
        [Description("특정 설비/날짜에 수행된 다른 명령어들의 1차 요약 목록 전체를 조회하여 교차 분석에 활용")]
        public async Task<string> GetPeerCommandSummariesAsync(
            [Description("설비명 (예: SW-CORE-01)")] string deviceName,
            [Description("기준 날짜 (yyyy-MM-dd)")] string dateStr,
            [Description("현재 진단 중인 대상 명령어 (결과에서 제외할 명령어)")] string currentCommand)
        {
            if (!DateOnly.TryParse(dateStr, out var date))
            {
                return "에러: 기준 날짜 형식이 잘못되었습니다. yyyy-MM-dd 형식을 사용하십시오.";
            }

            var peerSummaries = await db.LogSummaries
                .Where(s => s.DeviceName == deviceName && s.Date == date && s.Command != currentCommand)
                .OrderBy(s => s.Command)
                .Select(s => $"=== [명령어: {s.Command}] (상태: {s.Severity}) ===\n{s.SummaryText}\n")
                .ToListAsync();

            if (!peerSummaries.Any())
            {
                return "동일 날짜의 타 명령어 요약 기록이 없습니다.";
            }

            return string.Join("\n", peerSummaries);
        }

        // 2차 정밀 교차 진단 결과 저장
        [KernelFunction("SaveDeviceDiagnosis")]
        [Description("이상 징후 명령어에 대해 타 명령어와 교차 대조한 2차 정밀 진단 결과 저장/업데이트")]
        public async Task<string> SaveDeviceDiagnosisAsync(
            [Description("설비명 (예: SW-CORE-01)")] string deviceName,
            [Description("진단 대상 명령어 (예: show processes cpu)")] string command,
            [Description("로그 날짜 (yyyy-MM-dd)")] string dateStr,
            [Description("정밀 진단 보고서 텍스트 (Markdown)")] string diagnosisText,
            [Description("교차 분석에 참조한 타 명령어 목록 (예: show logging, show interface status)")] string referencedCommands = "",
            [Description("2차 진단 판정 심각도 (Warning 또는 Critical)")] string severity = "Warning")
        {
            if (!DateOnly.TryParse(dateStr, out var date))
            {
                return "날짜 형식이 잘못되었습니다. 저장에 실패했습니다.";
            }

            string normalizedSeverity = severity?.Trim().ToLowerInvariant() switch
            {
                "critical" or "위험" or "치명" => "Critical",
                _ => "Warning"
            };

            try
            {
                // 1차 요약 ID 조회 (FK 연결)
                var summary = await db.LogSummaries
                    .FirstOrDefaultAsync(s => s.DeviceName == deviceName && s.Command == command && s.Date == date);

                var existingDiagnosis = await db.LogDiagnoses
                    .FirstOrDefaultAsync(d => d.DeviceName == deviceName && d.Command == command && d.Date == date);

                if (existingDiagnosis != null)
                {
                    existingDiagnosis.DiagnosisText = diagnosisText;
                    existingDiagnosis.ReferencedCommands = referencedCommands;
                    existingDiagnosis.Severity = normalizedSeverity;
                    existingDiagnosis.LogSummaryId = summary?.Id;
                    existingDiagnosis.UpdatedAt = DateTime.UtcNow;
                    db.LogDiagnoses.Update(existingDiagnosis);
                }
                else
                {
                    var newDiagnosis = new LogDiagnosis
                    {
                        DeviceName = deviceName,
                        Command = command,
                        Date = date,
                        DiagnosisText = diagnosisText,
                        ReferencedCommands = referencedCommands,
                        Severity = normalizedSeverity,
                        LogSummaryId = summary?.Id,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    await db.LogDiagnoses.AddAsync(newDiagnosis);
                }

                await db.SaveChangesAsync();
                return $"{deviceName}의 '{command}' ({dateStr}) 2차 정밀 교차 진단 결과(상태: {normalizedSeverity})가 성공적으로 저장되었습니다.";
            }
            catch (Exception ex)
            {
                return $"2차 진단 DB 저장 중 오류 발생: {ex.Message}";
            }
        }


        [KernelFunction("UpdateDeviceModel")]
        [Description("설비의 물리적 장비 모델명 매핑 및 명령어 자동 등록")]
        public async Task UpdateDeviceModelMappingAsync(
        [Description("설비명 (예: SW-CORE-01)")] string deviceName,
        [Description("모델명 (예: WS-C3750X-48TS-L)")] string modelName)
        {
            // 1. 모델 마스터 테이블(CiscoModels)에 원시 SQL ON CONFLICT DO NOTHING 실행
            await db.Database.ExecuteSqlRawAsync(
                @"INSERT INTO ""CiscoModels"" (""ModelName"") 
                  VALUES ({0}) 
                  ON CONFLICT (""ModelName"") DO NOTHING;", 
                new object[] { modelName });

            // 2. 장비-모델 매핑 테이블(DeviceToModelMappings)에 원시 SQL ON CONFLICT DO UPDATE 실행
            var updatedAt = DateTime.UtcNow;
            await db.Database.ExecuteSqlRawAsync(
                @"INSERT INTO ""DeviceToModelMappings"" (""DeviceName"", ""ModelName"", ""UpdatedAt"") 
                  VALUES ({0}, {1}, {2}) 
                  ON CONFLICT (""DeviceName"") DO UPDATE 
                  SET ""ModelName"" = EXCLUDED.""ModelName"", ""UpdatedAt"" = EXCLUDED.""UpdatedAt"";", 
                new object[] { deviceName, modelName, updatedAt });
            logger.LogInformation("[플러그인 실행] {DeviceName} 장비와 '{ModelName}' 모델 매핑 완료 (UPSERT).", deviceName, modelName);

            // 👇 [추가 로직] 3. 방금 알아낸 모델명을 기준으로, 이 장비가 썼던 명령어들을 ModelCommands에 등록
            // RawLogs에서 이 장비의 명령어(Command) 목록을 중복 없이 가져옵니다.
            var deviceCommands = await db.RawLogs
                .Where(r => r.DeviceName == deviceName)
                .Select(r => r.Command)
                .Distinct()
                .ToListAsync();

            foreach (var cmd in deviceCommands)
            {
                // 원시 SQL ON CONFLICT DO NOTHING으로 안전하게 삽입
                await db.Database.ExecuteSqlRawAsync(
                    @"INSERT INTO ""ModelCommands"" (""ModelName"", ""CommandText"", ""IsEnabled"") 
                      VALUES ({0}, {1}, {2}) 
                      ON CONFLICT (""ModelName"", ""CommandText"") DO NOTHING;", 
                    new object[] { modelName, cmd, true });
            }
            logger.LogInformation("[플러그인 실행] '{ModelName}' 모델의 지원 명령어({Count}개) 점검 및 등록 완료.", modelName, deviceCommands.Count);
        }

        [KernelFunction("UpdateDeviceStackCount")]
        [Description("설비의 스택 멤버 스위치 총 대수(Stack Count) 정보를 데이터베이스에 업데이트합니다.")]
        public async Task UpdateDeviceStackCountAsync(
            [Description("설비명 (예: SW-CORE-01)")] string deviceName,
            [Description("판독된 스택 멤버 스위치의 총 개수")] int stackCount)
        {
            try
            {
                var device = await db.Devices
                    .FirstOrDefaultAsync(d => d.DeviceName == deviceName);

                if (device != null)
                {
                    device.StackCount = stackCount;
                    db.Devices.Update(device);
                    await db.SaveChangesAsync();
                    logger.LogInformation("[플러그인 실행] {DeviceName} 설비의 스택 개수를 {StackCount}대로 업데이트 완료.", deviceName, stackCount);
                }
                else
                {
                    logger.LogWarning("[플러그인 실행] {DeviceName} 설비를 Devices 테이블에서 찾을 수 없어 스택 수 업데이트 실패.", deviceName);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[플러그인 실행] 스택 개수 업데이트 중 예외 발생: {DeviceName}", deviceName);
            }
        }

    }
}
