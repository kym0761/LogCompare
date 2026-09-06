using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LogAPI.Classes.DB_Data
{
    public class LogDiagnosis
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public int? LogSummaryId { get; set; }

        [ForeignKey(nameof(LogSummaryId))]
        public LogSummary? LogSummary { get; set; }

        [Required]
        [MaxLength(100)]
        public string DeviceName { get; set; } = string.Empty;

        [Required]
        public DateOnly Date { get; set; }

        [Required]
        [MaxLength(200)]
        public string Command { get; set; } = string.Empty;

        public string ReferencedCommands { get; set; } = string.Empty;

        [Required]
        public string DiagnosisText { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string Severity { get; set; } = "Warning";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
