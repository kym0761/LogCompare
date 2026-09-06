using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LogLibrary.Classes.DB_Data
{
    public class DeviceToModel //설비명과 모델명을 매핑하는 테이블
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; } // id (int) - 기본키

        [Required]
        [MaxLength(200)]
        public string DeviceName { get; set; } = string.Empty; // 장비명 예시: SW-CORE-01

        [Required]
        [MaxLength(100)]
        public string ModelName { get; set; } = string.Empty; // 모델 타입 예시: X3750?

        /// <summary>
        /// 데이터 등록 또는 업데이트 시간을 기록합니다.
        /// </summary>
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    }
}
