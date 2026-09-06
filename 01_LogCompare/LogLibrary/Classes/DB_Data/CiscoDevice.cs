using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LogAPI.Classes.DB_Data
{
    public class CiscoDevice
    {
        public int Id { get; set; }

        [Required]
        [Column(TypeName = "text")]
        public string DeviceName { get; set; } = string.Empty; // 설비명 (예: SW-CORE-01)

        public string? Description { get; set; } // 설비 설명 (옵션)

        public int StackCount { get; set; } = 0; // 스택 멤버 스위치 대수 (0이면 미분석 상태)

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
