using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;

namespace LogLibrary.Classes
{
    public class StringParser
    {
        // 로그 분석 시 자주 마주치는 축약어 매핑 사전
        private static readonly Dictionary<string, string> CommandMap = new Dictionary<string, string>
        {
            // 1. 기본 Show 명령어 계열
            ["sh"] = "show",
            ["ver"] = "version",
            ["run"] = "running-config",
            ["start"] = "startup-config",
            ["conf t"] = "configure terminal",

            // 2. 인터페이스 및 상태 관련
            ["int"] = "interface",
            ["gi"] = "GigabitEthernet",
            ["te"] = "TenGigabitEthernet",
            ["fa"] = "FastEthernet",
            ["vlan"] = "VLAN",
            ["desc"] = "description",
            ["shut"] = "shutdown",
            ["no shut"] = "no shutdown",

            // 3. 라우팅 및 프로토콜
            ["ip ro"] = "ip route",
            ["ip int br"] = "ip interface brief",
            ["ip access-l"] = "ip access-list",

            // 4. 시스템 관리 및 파일 복사
            ["wr"] = "write", // copy run start와 동일
            ["copy run start"] = "copy running-config startup-config",
            ["term len"] = "terminal length",
            ["inv"] = "inventory",
            ["logging buff"] = "logging buffered",

            //5. 커스텀
            ["inter"] = "interface",
            ["tran"] = "transceiver",
            ["de"] = "detail", 
            ["desc"] = "description",
            ["ex"] = "exclude",
            ["in"] = "include",
            ["sw"] = "switch"
        };

        //특정 string이 전부 공백인지 아닌지 확인하여 true false
        public static bool IsAllWhiteSpace(string str)
        {
            //모든 값이 공백이면(스페이스, 엔터, 탭) true, 아니라면 false
            return string.IsNullOrWhiteSpace(str);
        }

        public static string GetFullCommand(string command)
        {
            List<string> words = command.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList<string>();
            List<string> results = new List<string>();

            for (int i = 0; i < words.Count; i++)
            {
                // 다음 단어가 있다면 2단어 조합을 먼저 시도 (예: "conf" + "t")
                if (i + 1 < words.Count)
                {
                    string twoWords = $"{words[i]} {words[i + 1]}";
                    if (CommandMap.TryGetValue(twoWords, out var fullTwo))
                    {
                        results.Add(fullTwo);
                        i++; // 다음 단어까지 처리했으므로 인덱스 하나 건너뜀
                        continue;
                    }
                }

                // 2단어 매칭 실패 시 1단어 매칭 시도
                if (CommandMap.TryGetValue(words[i], out var fullOne))
                {
                    results.Add(fullOne);
                }
                else
                {
                    results.Add(words[i]); // 사전에 없으면 그대로 유지
                }
            }

            string fullCommand = string.Join(" ", results);

            return fullCommand;
        }

        /// <summary>
        /// 명령어 텍스트와 로그 본문을 함께 받아, 본문 내용의 유일한 키워드 시그니처를 기반으로 
        /// 명령어를 매우 깔끔하고 직관적인 축약/표준 형태로 치환합니다.
        /// </summary>
        public static string NormalizeCommandWithContent(string command, string logContent)
        {
            if (string.IsNullOrEmpty(command)) return command;
            if (logContent == null) logContent = string.Empty;

            // 1. 로그 본문 특성으로 먼저 100% 매칭되는 시그니처가 있는지 탐색
            
            // SifRacDataCrcErrorCnt
            if (logContent.Contains("SifRacDataCrcErrorCnt", StringComparison.OrdinalIgnoreCase))
            {
                int switchNum = ExtractSwitchNumber(command);
                return $"show SifRacDataCrcErrorCnt switch {switchNum}";
            }

            // SifRacRwCrcErrorCnt
            if (logContent.Contains("SifRacRwCrcErrorCnt", StringComparison.OrdinalIgnoreCase))
            {
                int switchNum = ExtractSwitchNumber(command);
                return $"show SifRacRwCrcErrorCnt switch {switchNum}";
            }

            // SifRacPcsCodeWordErrorCnt
            if (logContent.Contains("SifRacPcsCodeWordErrorCnt", StringComparison.OrdinalIgnoreCase))
            {
                int switchNum = ExtractSwitchNumber(command);
                return $"show SifRacPcsCodeWordErrorCnt switch {switchNum}";
            }

            // SifRacInvalidRingWordCnt
            if (logContent.Contains("SifRacInvalidRingWordCnt", StringComparison.OrdinalIgnoreCase))
            {
                int switchNum = ExtractSwitchNumber(command);
                return $"show SifRacInvalidRingWordCnt switch {switchNum}";
            }

            // IGR_MISC_FATAL_ERROR
            if (logContent.Contains("IGR_MISC_FATAL_ERROR", StringComparison.OrdinalIgnoreCase) || 
                command.Contains("igr_misc", StringComparison.OrdinalIgnoreCase))
            {
                if (command.Contains("active", StringComparison.OrdinalIgnoreCase))
                {
                    return "show fwd-asic drops exceptions active";
                }
                if (command.Contains("standby", StringComparison.OrdinalIgnoreCase))
                {
                    return "show fwd-asic drops exceptions standby";
                }
                
                int switchNum = ExtractSwitchNumber(command);
                return $"show fwd-asic drops exceptions switch {switchNum}";
            }

            // TCAM utilization (예: show platform hardware fed switch 1 fwd-asic resource tcam utilization)
            if (command.Contains("tcam", StringComparison.OrdinalIgnoreCase) && command.Contains("utilization", StringComparison.OrdinalIgnoreCase))
            {
                int switchNum = ExtractSwitchNumber(command);
                return $"show tcam utilization switch {switchNum}";
            }

            // CPU Platform sorted (예: show processes cpu platform sorted location switch 1 R0)
            if (command.Contains("cpu", StringComparison.OrdinalIgnoreCase) && 
                command.Contains("platform", StringComparison.OrdinalIgnoreCase) && 
                command.Contains("sorted", StringComparison.OrdinalIgnoreCase))
            {
                int switchNum = ExtractSwitchNumber(command);
                return $"show cpu platform sorted switch {switchNum}";
            }

            // Memory Platform (예: show processes memory platform location switch 1 R0)
            if (command.Contains("memory", StringComparison.OrdinalIgnoreCase) && 
                command.Contains("platform", StringComparison.OrdinalIgnoreCase) && 
                command.Contains("location", StringComparison.OrdinalIgnoreCase))
            {
                int switchNum = ExtractSwitchNumber(command);
                return $"show memory platform switch {switchNum}";
            }

            // 2. 본문 기반 추론에 매핑되지 않는 경우, 기존의 일반 단축어 복원 본문 반환
            string cleaned = System.Text.RegularExpressions.Regex.Replace(command, @"\s+", " ").Trim().ToLower();
            string expanded = GetFullCommand(cleaned);
            return expanded;
        }

        private static int ExtractSwitchNumber(string text)
        {
            // 1. switch 1, sw 1, switch1, sw1 형태 탐색
            var match1 = System.Text.RegularExpressions.Regex.Match(text, @"\b(switch|sw)\s*(\d+)\b");
            if (match1.Success && int.TryParse(match1.Groups[2].Value, out int num1))
            {
                return num1;
            }

            // 2. swit ch 1, swi ch 1 등 글자 내부에 공백 오타가 들어간 특수한 경우 방어 (예: "swit ch 1")
            var match2 = System.Text.RegularExpressions.Regex.Match(text, @"\b(swit\s*ch|swi\s*ch)\s*(\d+)\b");
            if (match2.Success && int.TryParse(match2.Groups[2].Value, out int num2))
            {
                return num2;
            }

            // 3. 문자열 내 모든 숫자 중 가장 마지막 숫자 위치 탐색
            var matches = System.Text.RegularExpressions.Regex.Matches(text, @"\b(\d+)\b");
            if (matches.Count > 0 && int.TryParse(matches[matches.Count - 1].Groups[1].Value, out int lastNum))
            {
                return lastNum;
            }

            return 1; // 기본값
        }

        // 매번 Regex 객체를 생성하지 않도록 static readonly로 선언
        private static readonly System.Text.RegularExpressions.Regex PromptRegex =
            new System.Text.RegularExpressions.Regex(@"(?:\r?\n|^)([A-Za-z0-9\-_]+#)", System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// 로그 본문에서 정규식을 이용해 최초로 등장하는 진짜 설비명# 프롬프트(예: "ABC-01#")를 찾아 반환합니다.
        /// 본문 내의 주석/하드웨어 정보 등에 포함된 '#' 기호와 혼동하지 않도록 줄 시작 기준으로 탐색합니다.
        /// </summary>
        public static string? GetMachineName(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;

            var match = PromptRegex.Match(text);

            if (match.Success)
            {
                //텍스트의 "ABC-01#"만 찾아 반환함.
                return match.Groups[1].Value;
            }

            return null;
        }

        /// <summary>
        /// 파일명과 DB 명령어 명칭의 100% 일치를 보장하기 위해 
        /// 윈도우 파일 시스템 금지 문자를 언더바(_)로 치환하고 양끝 공백을 트리밍합니다.
        /// </summary>
        public static string GetSafeCommandName(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return command;

            string safe = command;
            char[] invalidChars = new char[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' };
            foreach (char c in invalidChars)
            {
                safe = safe.Replace(c, '_');
            }

            // 다중 공백을 단일 공백으로 치환하고 앞뒤 공백 제거
            safe = System.Text.RegularExpressions.Regex.Replace(safe, @"\s+", " ").Trim();
            return safe;
        }
    }
}
