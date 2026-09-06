using System;
using System.Collections.Generic;
using System.Text;

namespace LogLibrary.Classes
{
    public class CiscoLog
    {
        public bool IsTimeCertain { get; set; } // 날짜와 시간 확실함 여부
        public string? Timestamp { get; set; }
        public string? Facility { get; set; }
        public string? Severity { get; set; }
        public string? Mnemonic { get; set; }
        public string? Message { get; set; }
    }
}
