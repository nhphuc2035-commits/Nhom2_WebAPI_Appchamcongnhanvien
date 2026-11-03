using Microsoft.AspNetCore.Identity;
using System.Text.Json.Serialization;

namespace WebAPI_DiemDanh.Models
{
    public class ApplicationUser : IdentityUser
    {
        public string FullName { get; set; } = string.Empty;
        public string? Department { get; set; }
        public string? Position { get; set; }
        public string? AssignedShift { get; set; } = "Ca Hành Chính (08:00 - 17:00)";

        // Thông tin hồ sơ nhân sự chi tiết
        public DateTime? DateOfBirth { get; set; }
        public string? Gender { get; set; }
        public string? Address { get; set; }
        public string? CitizenId { get; set; }
        public DateTime? HireDate { get; set; }

        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        // Ràng buộc sinh trắc học chống điểm danh hộ
        public string? BiometricKey { get; set; }
        public string? BiometricDeviceId { get; set; }
        public DateTime? BiometricEnrolledAt { get; set; }

        [JsonIgnore]
        public virtual ICollection<AttendanceRecord> AttendanceRecords { get; set; } = new List<AttendanceRecord>();

        [JsonIgnore]
        public virtual ICollection<SalaryConfig> SalaryConfigs { get; set; } = new List<SalaryConfig>();
    }
}
