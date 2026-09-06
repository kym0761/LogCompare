using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LogAPI.Classes.DB_Data
{
    public class LogSummary
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string DeviceName { get; set; } = string.Empty;

        [Required]
        public DateOnly Date { get; set; }

        // 명령어 컬럼 추가
        [Required]
        //[MaxLength(200)]
        public string Command { get; set; } = string.Empty;

        [Required]
        public string SummaryText { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string Severity { get; set; } = "Normal";

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}