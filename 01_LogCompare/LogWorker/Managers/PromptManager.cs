using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using LogWorker.Interfaces;
using LogLibrary.Classes;

namespace LogWorker.Managers
{
    public class PromptManager : IPromptManager
    {
        private readonly string _promptsPath;

        // 1차 요약용: 명령어 -> 프롬프트 내용
        private readonly Dictionary<string, string> _summaryPrompts = new(StringComparer.OrdinalIgnoreCase);

        // 2차 정밀 진단용: 명령어 -> 프롬프트 내용
        private readonly Dictionary<string, string> _diagnosisPrompts = new(StringComparer.OrdinalIgnoreCase);

        // 시스템 프롬프트
        public string SystemPrompt { get; private set; } = string.Empty;

        // 2차 기본 교차 진단 템플릿
        public string DefaultDiagnosisPrompt { get; private set; } = string.Empty;

        public PromptManager(IConfiguration config)
        {
            _promptsPath = config["FileSettings:PromptFolderPath"] ?? Path.Combine(AppContext.BaseDirectory, "Prompts");
        }

        /// <summary>
        /// 프로그램 시작 시 지정된 경로에서 1차 요약 및 2차 교차 진단 프롬프트 텍스트를 각각 분리 로드합니다.
        /// </summary>
        public void LoadPrompts()
        {
            _summaryPrompts.Clear();
            _diagnosisPrompts.Clear();
            Directory.CreateDirectory(_promptsPath);

            // 1. 공통 시스템 프롬프트 로드 (최상위 경로의 system.txt)
            string systemPromptPath = Path.Combine(_promptsPath, "system.txt");
            if (File.Exists(systemPromptPath))
            {
                SystemPrompt = File.ReadAllText(systemPromptPath);
            }
            else
            {
                SystemPrompt = GetDefaultSystemPrompt();
            }

            // 2. 1차 요약 프롬프트 로드 (Summary/ 하위 폴더 우선, 없으면 최상위 폴더 폴백)
            string summaryDirPath = Path.Combine(_promptsPath, "Summary");
            string targetSummaryDir = Directory.Exists(summaryDirPath) ? summaryDirPath : _promptsPath;

            var summaryFiles = Directory.GetFiles(targetSummaryDir, "*.txt");
            foreach (var file in summaryFiles)
            {
                string cmdName = Path.GetFileNameWithoutExtension(file).Trim();
                if (cmdName.Equals("system", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                _summaryPrompts[cmdName] = File.ReadAllText(file);
            }

            // 3. 2차 교차 정밀 진단 프롬프트 로드 (Diagnosis/ 하위 폴더)
            string diagnosisDirPath = Path.Combine(_promptsPath, "Diagnosis");
            if (Directory.Exists(diagnosisDirPath))
            {
                var diagFiles = Directory.GetFiles(diagnosisDirPath, "*.txt");
                foreach (var file in diagFiles)
                {
                    string cmdName = Path.GetFileNameWithoutExtension(file).Trim();
                    string content = File.ReadAllText(file);

                    if (cmdName.Equals("default", StringComparison.OrdinalIgnoreCase))
                    {
                        DefaultDiagnosisPrompt = content;
                    }
                    else
                    {
                        _diagnosisPrompts[cmdName] = content;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(DefaultDiagnosisPrompt))
            {
                DefaultDiagnosisPrompt = GetHardcodedDefaultDiagnosisPrompt();
            }
        }

        public string GetSystemPrompt()
        {
            if (!string.IsNullOrWhiteSpace(SystemPrompt))
            {
                return SystemPrompt;
            }
            return GetDefaultSystemPrompt();
        }

        /// <summary>
        /// 1차 요약 프롬프트 반환 (하위 호환용)
        /// </summary>
        public string GetPrompt(string modelName, string command, string deviceName)
        {
            return GetSummaryPrompt(modelName, command, deviceName);
        }

        /// <summary>
        /// 1차 요약 전용 프롬프트를 반환합니다. (Summary/ 폴더 기준)
        /// 계층형 매칭: 1순위 완전일치 -> 2순위 파일명 안전문자 일치 -> 3순위 대표 명령어 템플릿 폴백
        /// </summary>
        public string GetSummaryPrompt(string modelName, string command, string deviceName)
        {
            if (TryFindPrompt(_summaryPrompts, command, out string? matchedTemplate) && matchedTemplate != null)
            {
                return ReplaceSummaryVariables(matchedTemplate, deviceName, command);
            }

            string fallbackText = "{deviceName} 설비의 '{command}' 로그를 분석하고 요약한 뒤, SaveDeviceSummary 함수를 호출해 줘.";
            return ReplaceSummaryVariables(fallbackText, deviceName, command);
        }

        /// <summary>
        /// 2차 정밀 교차 진단 전용 프롬프트를 반환합니다. (Diagnosis/ 폴더 기준, 없으면 default 사용)
        /// 계층형 매칭: 1순위 완전일치 -> 2순위 파일명 안전문자 일치 -> 3순위 대표 명령어 템플릿 폴백
        /// </summary>
        public string GetDiagnosisPrompt(string modelName, string command, string deviceName, string dateStr, string severity, string summaryText)
        {
            string template = DefaultDiagnosisPrompt;

            if (TryFindPrompt(_diagnosisPrompts, command, out string? matchedTemplate) && matchedTemplate != null)
            {
                template = matchedTemplate;
            }

            return template
                .Replace("{deviceName}", deviceName)
                .Replace("{command}", command)
                .Replace("{dateStr}", dateStr)
                .Replace("{severity}", severity)
                .Replace("{summaryText}", summaryText);
        }

        /// <summary>
        /// 프롬프트 딕셔너리에서 명령어를 계층형으로 탐색합니다:
        /// 1. 완전 일치 (Exact Match)
        /// 2. 윈도우 파일명 안전 문자 치환 일치 (Sanitized Match)
        /// 3. 정규화된 대표 명령어 후보군 순차 탐색 (Normalized Base Fallback)
        /// </summary>
        private bool TryFindPrompt(Dictionary<string, string> promptDict, string command, out string? result)
        {
            result = null;
            if (string.IsNullOrWhiteSpace(command))
            {
                return false;
            }

            // 1. 완전 일치
            if (promptDict.TryGetValue(command, out string? exactText) && exactText != null)
            {
                result = exactText;
                return true;
            }

            // 2. 윈도우 파일명 안전 문자 치환 일치
            string safeCommand = GetSafeFileName(command);
            if (promptDict.TryGetValue(safeCommand, out string? safeText) && safeText != null)
            {
                result = safeText;
                return true;
            }

            // 3. 정규화된 대표 명령어 및 동의어 후보군 탐색
            List<string> candidates = GetNormalizedBaseCandidates(command);
            foreach (string candidate in candidates)
            {
                if (promptDict.TryGetValue(candidate, out string? candidateText) && candidateText != null)
                {
                    result = candidateText;
                    return true;
                }

                string safeCandidate = GetSafeFileName(candidate);
                if (promptDict.TryGetValue(safeCandidate, out string? safeCandidateText) && safeCandidateText != null)
                {
                    result = safeCandidateText;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 명령어에서 동적 번호(스위치/슬롯/디스크) 및 문법 차이를 정규화하여 공통 대표 후보 목록을 생성합니다.
        /// </summary>
        private List<string> GetNormalizedBaseCandidates(string command)
        {
            var candidates = new List<string>();
            if (string.IsNullOrWhiteSpace(command))
            {
                return candidates;
            }

            string trimmed = command.Trim();

            // [규칙 1] 파이프(|) 또는 언더바(_) 필터링 옵션 범용 제거
            // 예: show processes cpu sorted | exclude 0.00 -> show processes cpu sorted
            // 예: show interface status _ exclude notconnect -> show interface status
            // 예: show processes memory | in Pool Total -> show processes memory
            string withoutFilter = Regex.Replace(trimmed, @"\s*(\||_)\s*(exclude|ex|include|inc|in|begin|section)\b.*$", "", RegexOptions.IgnoreCase).Trim();
            if (!string.Equals(withoutFilter, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(withoutFilter);
            }

            // 단순 파이프(|) 기호 기준 앞부분 본체 명령어 추출
            int pipeIdx = trimmed.IndexOf('|');
            if (pipeIdx > 0)
            {
                string baseBeforePipe = trimmed.Substring(0, pipeIdx).Trim();
                if (!candidates.Contains(baseBeforePipe, StringComparer.OrdinalIgnoreCase))
                {
                    candidates.Add(baseBeforePipe);
                }
            }

            // [규칙 2] dir 파일시스템 명령어 통합 (dir flash1:, dir flash-1:, dir bootflash: 등 -> dir flash)
            if (Regex.IsMatch(trimmed, @"^dir\s+(flash[-\d]*|bootflash[-\d]*|slavebootflash[-\d]*):?_?$", RegexOptions.IgnoreCase))
            {
                candidates.Add("dir flash");
            }

            // [규칙 3] 스택 스위치 번호 접미사 제거 (예: show fwd-asic drops exceptions switch 1 -> show fwd-asic drops exceptions)
            string withoutSwitch = Regex.Replace(trimmed, @"\s+switch\s+\d+$", "", RegexOptions.IgnoreCase);
            if (!string.Equals(withoutSwitch, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(withoutSwitch);
            }

            // [규칙 4] active / standby 이중화 접미사 제거 (예: show fwd-asic drops exceptions active -> show fwd-asic drops exceptions)
            string withoutRole = Regex.Replace(trimmed, @"\s+(active|standby)$", "", RegexOptions.IgnoreCase);
            if (!string.Equals(withoutRole, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(withoutRole);
            }

            // [규칙 5] switch 번호 + 추가 파라미터 (예: show processes cpu sorted switch 1 -> show processes cpu sorted)
            string withoutSwitchMiddle = Regex.Replace(trimmed, @"\s+switch\s+\d+\s*", " ", RegexOptions.IgnoreCase);
            if (!string.Equals(withoutSwitchMiddle, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(withoutSwitchMiddle.Trim());
            }

            // [규칙 6] 환경(Environment) 명령어 정규화 (show env, show env all -> show environment all)
            if (Regex.IsMatch(trimmed, @"^show\s+env(\s+all)?$", RegexOptions.IgnoreCase))
            {
                candidates.Add("show environment all");
                candidates.Add("show env all");
                candidates.Add("show env");
            }

            // [규칙 7] 광 트랜시버(Transceiver) 축약형 정규화 (show inter tran de -> show interfaces transceiver detail)
            if (Regex.IsMatch(trimmed, @"^show\s+inter(faces)?\s+tran(sceiver)?\s+de(tail)?$", RegexOptions.IgnoreCase))
            {
                candidates.Add("show interfaces transceiver detail");
                candidates.Add("show inter tran de");
            }

            // [규칙 8] 인터페이스 상태 명령어 (show interface status, show interfaces status | ...)
            if (Regex.IsMatch(trimmed, @"^show\s+interface(s)?\s+status.*$", RegexOptions.IgnoreCase))
            {
                candidates.Add("show interface status");
            }

            // [규칙 9] 인터페이스 에러 카운터 (show interfaces counters errors | include ...)
            if (Regex.IsMatch(trimmed, @"^show\s+interface(s)?\s+counters\s+errors.*$", RegexOptions.IgnoreCase))
            {
                candidates.Add("show interfaces counters errors");
            }

            // [규칙 10] 프로세스 메모리 풀 (show processes memory | include Pool Total 등)
            if (Regex.IsMatch(trimmed, @"^show\s+process(es)?\s+memory.*$", RegexOptions.IgnoreCase))
            {
                candidates.Add("show processes memory");
            }

            // [규칙 11] 플랫폼 리소스 (memory platform, tcam utilization, cpu platform sorted)
            if (Regex.IsMatch(trimmed, @"^show\s+memory\s+platform(\s+switch\s+\d+)?$", RegexOptions.IgnoreCase))
            {
                candidates.Add("show memory platform");
            }
            if (Regex.IsMatch(trimmed, @"^show\s+tcam\s+utilization(\s+switch\s+\d+)?$", RegexOptions.IgnoreCase))
            {
                candidates.Add("show tcam utilization");
            }
            if (Regex.IsMatch(trimmed, @"^show\s+cpu\s+platform\s+sorted(\s+switch\s+\d+)?$", RegexOptions.IgnoreCase))
            {
                candidates.Add("show cpu platform sorted");
            }

            // [규칙 12] 동의어 / 축약형 정규화 후보
            // process <-> processes, sort <-> sorted, ex <-> exclude
            string normalizedSynonyms = trimmed;
            normalizedSynonyms = Regex.Replace(normalizedSynonyms, @"\bprocess\b", "processes", RegexOptions.IgnoreCase);
            normalizedSynonyms = Regex.Replace(normalizedSynonyms, @"\bsort\b", "sorted", RegexOptions.IgnoreCase);
            normalizedSynonyms = Regex.Replace(normalizedSynonyms, @"\bex\b", "exclude", RegexOptions.IgnoreCase);
            if (!string.Equals(normalizedSynonyms, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(normalizedSynonyms);
                string synonymWithoutSwitch = Regex.Replace(normalizedSynonyms, @"\s+switch\s+\d+$", "", RegexOptions.IgnoreCase);
                if (!string.Equals(synonymWithoutSwitch, normalizedSynonyms, StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(synonymWithoutSwitch);
                }
                string synonymWithoutSwitchMiddle = Regex.Replace(normalizedSynonyms, @"\s+switch\s+\d+\s*", " ", RegexOptions.IgnoreCase);
                if (!string.Equals(synonymWithoutSwitchMiddle, normalizedSynonyms, StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(synonymWithoutSwitchMiddle.Trim());
                }
                string synonymWithoutFilter = Regex.Replace(normalizedSynonyms, @"\s*(\||_)\s*(exclude|ex|include|inc|in|begin|section)\b.*$", "", RegexOptions.IgnoreCase).Trim();
                if (!string.Equals(synonymWithoutFilter, normalizedSynonyms, StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(synonymWithoutFilter);
                }
            }

            return candidates;
        }

        private string GetSafeFileName(string command)
        {
            string safe = StringParser.GetSafeCommandName(command);
            if (safe.Length > 200)
            {
                safe = safe.Substring(0, 200);
            }
            return safe;
        }

        private string ReplaceSummaryVariables(string template, string deviceName, string command)
        {
            return template
                .Replace("{deviceName}", deviceName)
                .Replace("{command}", command);
        }

        private string GetDefaultSystemPrompt()
        {
            return 
                """
                당신은 네트워크 장비 로그 분석 전문가이다. Cisco 장비에서 수집된 원본 로그를 분석하여, 
                명령어 유형에 따른 정밀 요약본을 작성해야 한다. 
                요약본에는 반드시 변경점이나 중요한 정보가 포함되어야 하며, 
                상태의 이상 유무(Severity: Normal, Warning, Critical)를 판정하여 DB 저장 함수에 함께 전달해야 한다. 
                DB에 저장할 때는 항상 'DeviceName', 'Date', 'Command'를 기준으로 저장해야 하며, 
                동일한 명령어라도 날짜가 다르면 별도의 요약본이 필요하다.

                [환각(Hallucination) 방지 절대 규칙 - 필수 준수]
                1. 사실 기반 분석: 반드시 제공된 원본 로그 텍스트에 직접 명시된 사실과 수치에만 근거하라.
                2. 정보 누락 시 임의 추측 금지: 원본 로그에 특정 정보(온도, 전원 공급, 팬, 특정 포트 상태 등)가 명시되어 있지 않거나 누락된 경우, 절대로 정상(Normal/OK)으로 지어내거나 추측하지 말 것. 반드시 '로그 본문에 해당 정보 없음(확인 불가)'으로 솔직하게 명시하라.
                3. 데이터 날조 금지: 원본 로그에 존재하지 않는 가상의 IP, 포트 번호, 프로세스명, 에러 코드를 절대로 지어내지 말 것.
                4. 엄격한 심각도 판정: 판정 기준에 부합하지 않거나 확인되지 않는 장애를 과장하거나 축소하지 말 것.
                """;
        }

        private string GetHardcodedDefaultDiagnosisPrompt()
        {
            return
                """
                당신은 Cisco 네트워크 장비의 복합 장애를 분석하고 원인을 규명하는 전문 AI 네트워크 진단 엔지니어이다.
                현재 [{deviceName}] 장비의 '{command}' 항목에서 1차 이상 징후(심각도: {severity})가 감지되었다.

                [1차 요약 분석 결과]
                {summaryText}

                [진단 지시사항]
                1. 'GetPeerCommandSummaries' 도구를 호출하여 동일 날짜({dateStr})에 기록된 타 명령어들의 1차 요약 분석 결과를 모두 조회하라.
                2. 조회된 타 명령어들의 요약 내용(CPU, 메모리, 인터페이스 상태, 시스템 로그, 전원/환경 등)과 현재 이상 항목을 교차 대조(Cross-Correlation)하여 다음 항목들을 심층 진단하라:
                   - [근본 원인 추론 (Root Cause)]: 이상 징후를 유발한 핵심 원인이 무엇인지 도출
                   - [타 명령어와의 상관관계]: 동료 명령어 요약본에서 관찰된 관련 징후 및 상호 연관성 설명
                   - [영향도 평가]: 해당 이슈가 전체 장비 가동 및 서비스 트래픽에 미치는 파급 효과
                   - [엔지니어 조치 권고안 (Action Items)]: 현장에서 즉시 취해야 할 조치 및 점검 사항
                3. 진단이 완료되면 반드시 'SaveDeviceDiagnosis' 도구를 호출하여 결과를 DB에 저장하라.
                   - deviceName: "{deviceName}"
                   - command: "{command}"
                   - dateStr: "{dateStr}"
                   - referencedCommands: (분석 시 참고한 타 명령어 목록, 쉼표로 구분. 예: "show logging, show interface status")
                   - diagnosisText: (작성한 종합 교차 진단 리포트 마크다운 본문)
                   - severity: ("Warning" 또는 "Critical" 중 최종 판정된 종합 심각도)
                일반 대화로 응답 종료 절대 금지.
                """;
        }
    }
}
