using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WebAPI_DiemDanh.Models
{
    public class SalaryConfig
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        [ForeignKey("UserId")]
        public virtual ApplicationUser? User { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal HourlyRate { get; set; } = 50000m; // Mức lương / giờ cơ bản

        [Column(TypeName = "decimal(5,2)")]
        public decimal OvertimeRateMultiplier { get; set; } = 1.5m; // Hệ số ngoài giờ

        public double StandardDailyHours { get; set; } = 8.0; // Số giờ chuẩn 1 ngày

        public DateTime EffectiveDate { get; set; } = DateTime.UtcNow;

        [MaxLength(255)]
        public string? Note { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }
    }
}
