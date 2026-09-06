using LogAPI.Classes.DB_Data;
using LogLibrary.Classes.DB_Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LogLibrary.DB
{
    public class LogDbContext(DbContextOptions<LogDbContext> options) : DbContext(options)
    {
        //DB migration을 진행하려면 appsettings.json에 ConnectionString의 값을 확인 후 변경하세요

        //RawLog에 대한 Table
        public DbSet<LogEntry> RawLogs => Set<LogEntry>();

        //현재까지 DB에 들어간 Device에 대한 Table
        public DbSet<CiscoDevice> Devices => Set<CiscoDevice>();

        //LogSummary는 현재까지 DB에 들어간 Device의 요약 정보 (설비명, 가장 최근 로그 날짜, 총 로그 수 등)를 저장하는 테이블입니다.
        public DbSet<LogSummary> LogSummaries => Set<LogSummary>();

        //LogDiagnosis는 1차 요약에서 이상 징후가 포착된 건에 대해 타 명령어 요약을 교차 대조하여 정밀 분석한 결과 테이블입니다.
        public DbSet<LogDiagnosis> LogDiagnoses => Set<LogDiagnosis>();

        public DbSet<CiscoModel> CiscoModels => Set<CiscoModel>();
        public DbSet<ModelCommand> ModelCommands => Set<ModelCommand>();
        public DbSet<DeviceToModel> DeviceToModelMappings => Set<DeviceToModel>();



        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            //LogEntry
            {
                // 1. 단일 인덱스 (설비명으로만 검색할 때)
                modelBuilder.Entity<LogEntry>()
                    .HasIndex(l => l.DeviceName);

                // 2. 복합 인덱스 (설비명 + 날짜 조합으로 검색할 때 - 강추!)
                // 파일 이름 규칙이 '설비명.날짜'이므로 이 조합으로 인덱스를 걸면 검색 속도가 비약적으로 빨라집니다.
                modelBuilder.Entity<LogEntry>()
                    .HasIndex(l => new { l.DeviceName, l.Date })
                    .HasDatabaseName("IX_DeviceName_Date"); // 인덱스 이름 지정 (선택)

                // 3. 만약 중복 저장을 막고 싶다면 (Unique Index)
                // 같은 설비, 같은 날짜, 같은 커맨드의 로그는 하나만 있어야 한다면 .IsUnique()를 붙입니다.
                modelBuilder.Entity<LogEntry>()
                    .HasIndex(l => new { l.DeviceName, l.Date, l.Command })
                    .IsUnique();
            }

            //CiscoDevice
            {
                modelBuilder.Entity<CiscoDevice>()
                    .HasIndex(d => d.DeviceName)
                    .IsUnique(); // 설비명은 유니크해야 하므로 Unique Index
            }

            //DeviceSummaries
            {
                // 설비명 + 날짜 + 명령어 조합이 중복되지 않도록 유니크 인덱스 설정
                modelBuilder.Entity<LogSummary>()
                    .HasIndex(s => new { s.DeviceName, s.Date, s.Command })
                    .IsUnique();

                modelBuilder.Entity<LogSummary>()
                    .Property(s => s.Severity)
                    .HasDefaultValue("Normal")
                    .HasMaxLength(50);
            }

            //LogDiagnoses
            {
                modelBuilder.Entity<LogDiagnosis>()
                    .HasIndex(d => new { d.DeviceName, d.Date, d.Command })
                    .IsUnique();

                modelBuilder.Entity<LogDiagnosis>()
                    .Property(d => d.Severity)
                    .HasDefaultValue("Warning")
                    .HasMaxLength(50);

                modelBuilder.Entity<LogDiagnosis>()
                    .HasOne(d => d.LogSummary)
                    .WithMany()
                    .HasForeignKey(d => d.LogSummaryId)
                    .OnDelete(DeleteBehavior.SetNull);
            }


            //CiscoModel 설정
            {
                // 모델명 자체를 기본 키(PK)로 사용
                modelBuilder.Entity<CiscoModel>()
                    .HasKey(m => m.ModelName);
            }

            {
                // 특정 모델(ModelName)에 동일한 명령어(CommandText)가 중복 등록되는 것 방지
                modelBuilder.Entity<ModelCommand>()
                    .HasIndex(c => new { c.ModelName, c.CommandText })
                    .IsUnique();

                // 외래키 관계 설정 (부모: CiscoModel)
                modelBuilder.Entity<ModelCommand>()
                    .HasOne<CiscoModel>()
                    .WithMany()
                    .HasForeignKey(c => c.ModelName)
                    .OnDelete(DeleteBehavior.Cascade);
            }

            //DeviceToModel 설정
            {
                // 설비(DeviceName)당 하나의 모델만 가질 수 있도록 PK 설정
                modelBuilder.Entity<DeviceToModel>()
                    .HasKey(d => d.DeviceName);

                // CiscoDevice 테이블과의 1:1 관계 (설비명이 존재해야 매핑도 가능)
                modelBuilder.Entity<DeviceToModel>()
                    .HasOne<CiscoDevice>()
                    .WithOne()
                    .HasForeignKey<DeviceToModel>(d => d.DeviceName)
                    .HasPrincipalKey<CiscoDevice>(d => d.DeviceName)
                    .OnDelete(DeleteBehavior.Cascade);

                // CiscoModel 테이블과의 N:1 관계 (여러 설비가 같은 모델일 수 있음)
                modelBuilder.Entity<DeviceToModel>()
                    .HasOne<CiscoModel>()
                    .WithMany()
                    .HasForeignKey(d => d.ModelName);
            }

            //relation
            { 

                modelBuilder.Entity<LogEntry>()
                    .HasOne<CiscoDevice>() // LogEntry는 CiscoDevice와 1:N 관계 (LogEntry는 CiscoDevice를 참조하지만, CiscoDevice는 LogEntry를 참조하지 않음)
                    .WithMany() // CiscoDevice는 여러 LogEntry를 가질 수 있지만, 여기서는 네비게이션 프로퍼티가 없으므로 빈 WithMany() 사용
                    .HasPrincipalKey(d => d.DeviceName) // CiscoDevice의 DeviceName이 외래키로 사용됨
                    .HasForeignKey(l => l.DeviceName) // LogEntry의 DeviceName이 외래키로 사용됨
                    .OnDelete(DeleteBehavior.Cascade); // CiscoDevice가 삭제되면 관련된 LogEntry도 삭제 (선택 사항)

            }

        }
    }
}
