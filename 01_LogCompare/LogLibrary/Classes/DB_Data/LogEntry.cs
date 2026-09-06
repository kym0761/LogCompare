using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LogAPI.Classes.DB_Data
{
    public class LogEntry
    {
        [Key]
        public int Id { get; set; } // id (int) - 기본키

        [Required]
        [Column(TypeName = "text")]
        public string DeviceName { get; set; } = string.Empty; // devicename (varchar)

        // 시/분/초 없이 년-월-일만 저장 (PostgreSQL의 date 타입과 매칭)
        [Column(TypeName = "date")]
        public DateOnly Date { get; set; }

        [Column(TypeName = "text")]
        public string Command { get; set; } = string.Empty; // command (varchar)

        [Column(TypeName = "text")]
        public string Content { get; set; } = string.Empty; // content (text)
    }
}
