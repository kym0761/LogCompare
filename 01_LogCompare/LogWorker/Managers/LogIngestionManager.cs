using LogLibrary.Classes;
using LogAPI.Classes.DB_Data;
using LogLibrary.DB;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using LogWorker.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LogWorker.Managers
{
    /// <summary>
    /// 로그 파일을 안전하게 수집하고, 파일명 및 본문을 파싱하여 DB 적재 및 아카이빙 프로세스를 총괄하는 매니저 클래스입니다.
    /// </summary>
    public class LogIngestionManager(ILogger<LogIngestionManager> logger, LogDbContext db, IConfiguration config) : ILogIngestionManager
    {
        private readonly string _rawLogsPath = config["FileSettings:RawLogFolderPath"] ?? "C:\\docker_data\\raw_logs";
        private readonly string _processedLogsPath = config["FileSettings:ProcessedLogFolderPath"] ?? "C:\\docker_data\\processed_logs";
        private readonly string _errorLogsPath = config["FileSettings:ErrorLogFolderPath"] ?? "C:\\docker_data\\error_logs";
        private readonly bool _enableArchiving = config.GetValue<bool>("FileSettings:EnableFileArchiving", false);

        public async Task ProcessRawLogsAsync(CancellationToken stoppingToken)
        {
            if (!Directory.Exists(_rawLogsPath)) return;

            var logFiles = Directory.GetFiles(_rawLogsPath, "*", SearchOption.AllDirectories);
            if (logFiles.Length == 0) return;

            foreach (var filePath in logFiles)
            {
                string fileName = Path.GetFileName(filePath);
                logger.LogInformation("파일 처리 시작: {FileName}", fileName);

                try
                {
                    // 1. 파일 읽기
                    string fullText = await File.ReadAllTextAsync(filePath, stoppingToken);

                    // 2. 파일명 파싱 (메서드 분리)
                    if (!TryParseFileName(fileName, out string deviceName, out DateOnly date))
                    {
                        logger.LogWarning("Device Name 또는 날짜 형식을 확인할 수 없어 스킵합니다. 파일명: {fileName}", fileName);
                        ArchiveFile(filePath, _errorLogsPath, fileName, "UnknownDevice");
                        continue;
                    }

                    // 3. 로그 내용 명령어 단위 파싱 (메서드 분리)
                    if (!TryExtractCommandLogs(fullText, out Dictionary<string, string> logs, out string errorReason))
                    {
                        logger.LogWarning("{Reason} 문제로 인해 파싱에 실패했습니다. 파일명: {fileName}", errorReason, fileName);
                        ArchiveFile(filePath, _errorLogsPath, fileName, errorReason);
                        continue;
                    }

                    // 4. DB 적재 트랜잭션 및 백업 처리 (메서드 분리)
                    bool isSaved = await SaveIngestedDataAsync(deviceName, date, logs, stoppingToken);
                    if (isSaved)
                    {
                        logger.LogInformation("DB 저장 완료: {FileName} ({Count}개 명령어)", fileName, logs.Count);
                        ArchiveFile(filePath, _processedLogsPath, fileName);
                    }
                    else
                    {
                        // 중복 등의 이력은 이미 완료 상태이므로 Duplicated 폴더로 안전하게 아카이빙
                        ArchiveFile(filePath, _processedLogsPath, fileName, "Duplicated");
                    }
                }
                catch (DbUpdateException ex)
                {
                    db.ChangeTracker.Clear(); // 예외 발생 시 트래커 초기화

                    if (ex.InnerException is Npgsql.PostgresException pgEx)
                    {
                        if (pgEx.SqlState == "22001") // 데이터 길이 초과
                        {
                            logger.LogError("데이터 길이 초과. 조용히 스킵합니다. 파일명: {fileName}", fileName);
                            ArchiveFile(filePath, _errorLogsPath, fileName, "MaxLengthExceeded");
                        }
                        else
                        {
                            logger.LogError(ex, "알 수 없는 Postgres DB 에러 발생 (State: {State}). 파일명: {fileName}", pgEx.SqlState, fileName);
                            ArchiveFile(filePath, _errorLogsPath, fileName, "DbError");
                        }
                    }
                    else
                    {
                        logger.LogError(ex, "알 수 없는 데이터 저장 오류. 파일명: {fileName}", fileName);
                        ArchiveFile(filePath, _errorLogsPath, fileName, "DbError");
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "파일 처리 중 치명적 에러 발생: {FileName}", fileName);
                    ArchiveFile(filePath, _errorLogsPath, fileName, "FatalError");
                }
            }
        }

        /// <summary>
        /// 파일명에서 장비명(DeviceName) 및 수집 날짜(Date)를 안전하게 분석 및 추출합니다.
        /// </summary>
        private bool TryParseFileName(string fileName, out string deviceName, out DateOnly date)
        {
            deviceName = "Unknown";
            date = DateOnly.FromDateTime(DateTime.UtcNow);

            string fileNameToParse = fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                ? fileName.Substring(0, fileName.Length - 4)
                : fileName;

            var fn = fileNameToParse.Split(".DeviceOutput.");
            if (fn.Length >= 1 && fn[0] != fileNameToParse)
            {
                deviceName = fn[0];
                for (int i = 1; i < fn.Length; i++)
                {
                    if (DateOnly.TryParseExact(fn[i], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var parsedDate))
                    {
                        date = parsedDate;
                        break;
                    }
                }
            }
            else
            {
                // [WARNING] 이와 같이 파일 명칭을 파싱하여 장비명 등을 추출하는 방식은,
                // 추후 설비 AS 교체나 추가 설치 등으로 인해 파일명 일관성이 깨질 경우 오동작할 위험이 매우 큽니다.
                // 따라서 시스템을 사용하는 부서에서는 업로드하는 파일 명칭 규칙을 반드시 통일하도록 가이드해야 합니다.
                var fn2 = fileNameToParse.Split('.');
                if (fn2.Length >= 1)
                {
                    deviceName = fn2[0];
                    for (int i = 1; i < fn2.Length; i++)
                    {
                        if (DateOnly.TryParseExact(fn2[i], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var parsedDate))
                        {
                            date = parsedDate;
                            break;
                        }
                    }
                }
            }

            return deviceName != "Unknown";
        }

        /// <summary>
        /// 본문 텍스트 내에서 머신 구분 기호를 탐색하고 각 Cisco 명령어와 원본 로그들을 매핑 추출합니다.
        /// </summary>
        private bool TryExtractCommandLogs(string fullText, out Dictionary<string, string> logs, out string errorReason)
        {
            logs = new Dictionary<string, string>();
            errorReason = string.Empty;

            // 정규식을 사용해 최초로 등장하는 진짜 설비명# 프롬프트 추출
            string? machineName = StringParser.GetMachineName(fullText);

            logger.LogInformation($"파싱된 머신 이름: {machineName}", machineName);

            if (string.IsNullOrEmpty(machineName))
            {
                errorReason = "NoMachineName";
                return false;
            }

            var strings = fullText.Split(machineName);
            foreach (var log in strings)
            {
                if (StringParser.IsAllWhiteSpace(log)) continue;

                string trimed = log.TrimStart().Replace("\r", "");
                var temp = trimed.Split('\n', 2);

                if (temp.Length < 2) continue;

                string logContent = temp[1];
                string command = StringParser.NormalizeCommandWithContent(temp[0], logContent);
                command = StringParser.GetSafeCommandName(command); // DB와 파일명의 100% 매칭을 위해 금지 문자 변환 및 규격화

                if (logContent.Length > 0)
                {
                    logs[command] = logContent;
                }
            }

            return true;
        }

        /// <summary>
        /// 추출된 대량의 로그와 장비 엔티티를 영속 영역(DB)에 동시 안전하게 트랜잭션 적재합니다.
        /// </summary>
        private async Task<bool> SaveIngestedDataAsync(string deviceName, DateOnly date, Dictionary<string, string> logs, CancellationToken stoppingToken)
        {
            // 1. 설비(Device) 정보 갱신 및 신규 삽입 (ON CONFLICT DO NOTHING 원시 SQL 수행)
            var description = $"{deviceName} 자동 등록 정보";
            var createdAt = DateTime.UtcNow;

            await db.Database.ExecuteSqlRawAsync(
                @"INSERT INTO ""Devices"" (""DeviceName"", ""Description"", ""CreatedAt"") 
                  VALUES ({0}, {1}, {2}) 
                  ON CONFLICT (""DeviceName"") DO NOTHING;", 
                deviceName, description, createdAt);

            // 2. DB에 이미 저장된 명령어 리스트를 조회 (DeviceName, Date 기준)
            var existingCommands = await db.RawLogs
                .Where(r => r.DeviceName == deviceName && r.Date == date)
                .Select(r => r.Command)
                .ToListAsync(stoppingToken);

            // 3. DB에 존재하지 않는 신규 로그 항목들만 필터링하여 Entity 리스트 생성
            var newEntries = logs
                .Where(log => !existingCommands.Contains(log.Key))
                .Select(log => new LogEntry
                {
                    DeviceName = deviceName,
                    Date = date,
                    Command = log.Key,
                    Content = log.Value
                }).ToList();

            // 모든 항목이 이미 존재한다면 중복 파일로 판단하여 false 리턴 (Duplicated 폴더로 아카이빙)
            if (newEntries.Count == 0)
            {
                logger.LogInformation("이미 데이터베이스에 모든 로그 항목이 존재합니다. (중복 처리 생략)");
                return false;
            }

            // 4. 신규 로그 적재 시도 및 롤백/중복 이격 대응
            try
            {
                db.RawLogs.AddRange(newEntries);
                await db.SaveChangesAsync(stoppingToken);
                db.ChangeTracker.Clear(); // 성공 후 추적기를 비워 메모리 점유 해제
                return true;
            }
            catch (DbUpdateException ex)
            {
                db.ChangeTracker.Clear(); // 트래커 정리

                if (ex.InnerException is Npgsql.PostgresException pgEx && pgEx.SqlState == "23505")
                {
                    logger.LogWarning("동시성 발생 또는 중복 항목 감지로 인해 저장이 생략되었습니다.");
                    return false; // 중복 적재 통과 성공
                }

                throw; // 길이 초과 등 타 예외는 상위 컨트롤로 전파해 적절한 백업 폴더로 격리
            }
        }

        /// <summary>
        /// 설정에 따라 파일을 아카이빙(이동) 처리하는 헬퍼 메서드입니다.
        /// </summary>
        private void ArchiveFile(string sourceFilePath, string targetDirectory, string fileName, string? subFolder = null)
        {
            if (!_enableArchiving) return;

            try
            {
                string finalTargetDir = targetDirectory;
                if (!string.IsNullOrEmpty(subFolder))
                {
                    finalTargetDir = Path.Combine(targetDirectory, subFolder);
                }

                Directory.CreateDirectory(finalTargetDir);
                string targetFilePath = Path.Combine(finalTargetDir, fileName);

                if (File.Exists(targetFilePath))
                {
                    string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                    string ext = Path.GetExtension(fileName);
                    string timestamp = DateTime.Now.ToString("yyyyMMddHHmmssfff");
                    targetFilePath = Path.Combine(finalTargetDir, $"{nameWithoutExt}_{timestamp}{ext}");
                }

                File.Move(sourceFilePath, targetFilePath);
                logger.LogInformation("파일 아카이빙 완료: {Source} -> {Target}", sourceFilePath, targetFilePath);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "파일 아카이빙(이동) 중 오류 발생: {FilePath}", sourceFilePath);
            }
        }
    }
}
