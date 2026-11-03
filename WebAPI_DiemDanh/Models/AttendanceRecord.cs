using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WebAPI_DiemDanh.Models
{
    public class AttendanceRecord
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        [ForeignKey("UserId")]
        public virtual ApplicationUser? User { get; set; }

        [Required]
        public DateTime AttendanceDate { get; set; }

        public DateTime? CheckInTime { get; set; }

        public DateTime? CheckOutTime { get; set; }

        public double TotalWorkingHours { get; set; } = 0.0;

        /// <summary>
        /// Ca làm việc: Ca Hành Chính (08:00 - 17:00) | Ca Sáng (06:00 - 14:00) | Ca Chiều (14:00 - 22:00) | Ca Tối (18:00 - 22:00)
        /// </summary>
        [MaxLength(100)]
        public string? ShiftName { get; set; } = "Ca Hành Chính (08:00 - 17:00)";

        /// <summary>
        /// Fingerprint | FaceRecognition | NfcCard | Manual
        /// </summary>
        [MaxLength(50)]
        public string AuthenticationMethod { get; set; } = "Fingerprint";

        public bool IsManualEntry { get; set; } = false;

        public string? ManualEntryByUserId { get; set; }

        [MaxLength(255)]
        public string? Remarks { get; set; }

        /// <summary>
        /// Đường dẫn ảnh Selfie khoảnh khắc lúc bấm điểm danh (xác thực khuôn mặt minh bạch)
        /// </summary>
        [MaxLength(500)]
        public string? ProofImagePath { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }
    }
}
