using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;


namespace LogLibrary.Classes.DB_Data
{
    /// <summary>
    /// 모델별 명령어 매핑 테이블 (1번 방식: 단일 테이블 구조)[cite: 1, 5]
    /// </summary>
    public class ModelCommand
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string ModelName { get; set; } = string.Empty; // 외래키 역할 (3750, C9300 등)

        [Required]
        //[MaxLength(200)]
        public string CommandText { get; set; } = string.Empty; // 명령어 (show version 등)

        public bool IsEnabled { get; set; } = true; // 분석 활성화 여부

    }
}
