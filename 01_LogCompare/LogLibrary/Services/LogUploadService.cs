using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LogLibrary.Classes;
using LogAPI.Classes.DB_Data;
using LogLibrary.DB;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LogLibrary.Services
{
    public class LogUploadService
    {
        private readonly LogDbContext _db;
        private readonly ILogger<LogUploadService> _logger;

        public LogUploadService(LogDbContext db, ILogger<LogUploadService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<LogUploadResult> ProcessUploadAsync(Stream fileStream, string filename)
        {
            using var reader = new StreamReader(fileStream);
            string fullText = await reader.ReadToEndAsync();

            if (!TryParseFileName(filename, out string deviceName, out DateOnly date))
            {
                return new LogUploadResult 
                { 
                    IsSuccess = false, 
                    StatusCode = 400, 
                    Message = "device name 또는 날짜 형식을 확인할 수 없습니다." 
                };
            }

            // 정규식으로 로그 본문에서 최초로 등장하는 설비명# 프롬프트(예: ABC-01#)를 추출합니다.
            // 본문 내의 주석/하드웨어 정보 등에 포함된 '#' 기호와 혼동하지 않도록 줄 시작 기준으로 탐색합니다.
            string? machineName = StringParser.GetMachineName(fullText);
            if (string.IsNullOrEmpty(machineName))
            {
                return new LogUploadResult 
                { 
                    IsSuccess = false, 
                    StatusCode = 400, 
                    Message = "설비명# 프롬프트를 찾을 수 없습니다." 
                };
            }

            // 찾아낸 프롬프트 문자열(예: ABC-01#)을 기준으로 본문을 명령어 단위로 분할합니다.
            string[] strings = fullText.Split(machineName);

            // key: 명령어, value: 명령어에 대한 로그 전체
            Dictionary<string, string> logs = new Dictionary<string, string>();

            foreach (string log in strings) 
            {
                // 빈 string은 전부 스킵
                if (StringParser.IsAllWhiteSpace(log))
                {
                    continue;
                }

                // 맨 앞의 \r\n 제거(trimstart), 이어서 텍스트 안의 \r 제거(replace)
                // 첫 줄은 command, 다음 줄 내용들은 전부 log 내용
                string trimed = log.TrimStart().Replace("\r", "");

                string[] temp = trimed.Split('\n', 2);
                if (temp.Length < 2) 
                {
                    continue; // 줄바꿈 없이 끝난 경우 무시
                }
                
                string logContent = temp[1];
                string command = StringParser.NormalizeCommandWithContent(temp[0], logContent);
                command = StringParser.GetSafeCommandName(command); // DB와 파일명의 100% 매칭을 위해 규격화

                if (logContent.Length == 0)
                {
                    // logContent가 0이라면 굳이 해당 명령어를 처리할 필요가 없으므로 스킵함.
                    continue;
                }

                // command에 매칭되는 로그 내용 저장
                logs[command] = logContent;
            }

            // DB 저장 엔트리 생성
            var entries = new List<LogEntry>();
            foreach (var log in logs)
            {
                LogEntry logEntry = new LogEntry
                {
                    DeviceName = deviceName,
                    Date = date, // 파일명에서 정확히 파싱된 날짜 저장
                    Command = log.Key, // 명령어
                    Content = log.Value // 명령어에 대한 전체 로그 내용
                };

                entries.Add(logEntry);
            }

            // 동시성 문제 해결을 위해 PostgreSQL의 ON CONFLICT 구문을 사용한 원시 SQL 쿼리 실행
            string description = $"{deviceName} 자동 등록 정보";
            DateTime createdAt = DateTime.UtcNow;

            await _db.Database.ExecuteSqlRawAsync(
                @"INSERT INTO ""Devices"" (""DeviceName"", ""Description"", ""CreatedAt"") 
                  VALUES ({0}, {1}, {2}) 
                  ON CONFLICT (""DeviceName"") DO NOTHING;", 
                deviceName, description, createdAt);

            // DB에 이미 저장된 명령어 리스트를 조회 (DeviceName, Date 기준)
            var existingCommands = await _db.RawLogs
                .Where(r => r.DeviceName == deviceName && r.Date == date)
                .Select(r => r.Command)
                .ToListAsync();

            // DB에 존재하지 않는 신규 로그 항목들만 필터링
            var newEntries = entries
                .Where(e => !existingCommands.Contains(e.Command))
                .ToList();

            if (newEntries.Count == 0)
            {
                return new LogUploadResult { IsSuccess = false, StatusCode = 409, Message = "이미 모든 로그 데이터가 저장되어 있습니다." };
            }

            try
            {
                _db.RawLogs.AddRange(newEntries);
                await _db.SaveChangesAsync(); 
                
                return new LogUploadResult 
                {
                    IsSuccess = true, 
                    StatusCode = 200, 
                    Message = newEntries.Count == entries.Count
                        ? "성공적으로 업로드되었습니다."
                        : $"중복 항목을 제외하고 업로드 완료 (신규: {newEntries.Count}개 / 중복: {entries.Count - newEntries.Count}개)",
                    DeviceName = deviceName,
                    UploadedCount = newEntries.Count
                };
            }
            catch (DbUpdateException ex)
            {
                // PostgreSQL의 구체적인 에러 코드를 확인합니다 (PostgresException)
                if (ex.InnerException is Npgsql.PostgresException pgEx)
                {
                    if (pgEx.SqlState == "23505") // Unique Violation (진짜 중복)
                    {
                        return new LogUploadResult { IsSuccess = false, StatusCode = 409, Message = "이미 존재하는 데이터입니다." };
                    }
                    if (pgEx.SqlState == "22001") // String Data Right Truncation (길이 초과)
                    {
                        foreach (var entry in ex.Entries)
                        {
                            var entity = entry.Entity as LogEntry;
                            if (entity != null)
                            {
                                _logger.LogError("[에러 추적] Command 길이({CmdLen}), DeviceName 길이({DevLen}) 초과로 저장 실패.", entity.Command.Length, entity.DeviceName.Length);
                            }
                        }
                        return new LogUploadResult { IsSuccess = false, StatusCode = 400, Message = "데이터가 너무 깁니다. (서버 에러 로그 확인 필요.)" };
                    }
                }

                // 그 외 알 수 없는 에러들
                return new LogUploadResult { IsSuccess = false, StatusCode = 500, Message = ex.InnerException?.Message ?? "데이터 저장 오류" };
            }
        }

        /// <summary>
        /// 파일명에서 장비명(DeviceName) 및 수집 날짜(Date)를 안전하게 분석 및 추출합니다.
        /// </summary>
        private static bool TryParseFileName(string fileName, out string deviceName, out DateOnly date)
        {
            deviceName = "Unknown";
            date = DateOnly.FromDateTime(DateTime.Today);

            string fileNameToParse = fileName;
            if (fileNameToParse.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                fileNameToParse = fileNameToParse.Substring(0, fileNameToParse.Length - 4);
            }
            else if (fileNameToParse.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
            {
                fileNameToParse = fileNameToParse.Substring(0, fileNameToParse.Length - 4);
            }

            // 1. ".DeviceOutput." 구분자가 있는 경우
            string[] fn = fileNameToParse.Split(".DeviceOutput.", StringSplitOptions.None);
            if (fn.Length >= 1 && fn[0] != fileNameToParse)
            {
                deviceName = fn[0];
                for (int i = 1; i < fn.Length; i++)
                {
                    if (DateOnly.TryParseExact(fn[i], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out DateOnly parsedDate))
                    {
                        date = parsedDate;
                        break;
                    }
                }
                return !string.IsNullOrWhiteSpace(deviceName) && deviceName != "Unknown";
            }

            // 2. 도트(.)로 분할하는 경우 (RULES.md 준수: 날짜 yyyyMMdd 직전까지의 모든 토큰을 병합)
            string[] tokens = fileNameToParse.Split('.');
            if (tokens.Length >= 1)
            {
                int dateIndex = -1;
                for (int i = 0; i < tokens.Length; i++)
                {
                    if (DateOnly.TryParseExact(tokens[i], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out DateOnly parsedDate))
                    {
                        date = parsedDate;
                        dateIndex = i;
                        break;
                    }
                }

                if (dateIndex > 0)
                {
                    // 날짜 토큰 이전까지의 모든 토큰을 결합 (예: SW-CORE-01.20260525 -> SW-CORE-01)
                    deviceName = string.Join('.', tokens.Take(dateIndex));
                }
                else if (dateIndex == -1)
                {
                    // 날짜 토큰이 없는 경우 첫 번째 토큰
                    deviceName = tokens[0];
                }
                else
                {
                    // dateIndex == 0
                    deviceName = tokens.Length > 1 ? string.Join('.', tokens.Skip(1)) : "Unknown";
                }
            }

            return !string.IsNullOrWhiteSpace(deviceName) && deviceName != "Unknown";
        }
    }
}
