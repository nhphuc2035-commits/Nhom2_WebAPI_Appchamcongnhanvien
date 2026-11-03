using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using WebAPI_DiemDanh.Models;

namespace WebAPI_DiemDanh.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<AttendanceRecord> AttendanceRecords { get; set; } = null!;
        public DbSet<SalaryConfig> SalaryConfigs { get; set; } = null!;
        public DbSet<PositionRate> PositionRates { get; set; } = null!;
        public DbSet<SalaryBonus> SalaryBonuses { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Cấu hình bảng AttendanceRecord
            builder.Entity<AttendanceRecord>(entity =>
            {
                entity.ToTable("AttendanceRecords");

                entity.HasKey(e => e.Id);

                entity.HasIndex(e => new { e.UserId, e.AttendanceDate })
                      .HasDatabaseName("IX_Attendance_User_Date");

                entity.HasIndex(e => e.AttendanceDate)
                      .HasDatabaseName("IX_Attendance_Date");

                entity.Property(e => e.AuthenticationMethod)
                      .HasMaxLength(50)
                      .IsRequired();

                entity.Property(e => e.Remarks)
                      .HasMaxLength(255);

                entity.HasOne(e => e.User)
                      .WithMany(u => u.AttendanceRecords)
                      .HasForeignKey(e => e.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // Cấu hình bảng SalaryConfig
            builder.Entity<SalaryConfig>(entity =>
            {
                entity.ToTable("SalaryConfigs");

                entity.HasKey(e => e.Id);

                entity.HasIndex(e => e.UserId)
                      .HasDatabaseName("IX_SalaryConfig_User");

                entity.Property(e => e.HourlyRate)
                      .HasPrecision(18, 2);

                entity.Property(e => e.OvertimeRateMultiplier)
                      .HasPrecision(5, 2);

                entity.HasOne(e => e.User)
                      .WithMany(u => u.SalaryConfigs)
                      .HasForeignKey(e => e.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
