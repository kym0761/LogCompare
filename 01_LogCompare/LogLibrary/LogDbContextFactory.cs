using LogLibrary.DB; // LogDbContext가 있는 네임스페이스
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LogLibrary.Classes.DB_Data // 적당한 네임스페이스
{
    /// <summary>
    /// EF Core 도구가 마이그레이션을 실행할 때만 "임시로" DB 컨텍스트를 만들어주는 팩토리입니다.
    /// 실제 앱이 돌아갈 때는(Worker, API) 이 코드를 타지 않고 각자의 appsettings.json을 사용합니다.
    /// </summary>
    public class LogDbContextFactory : IDesignTimeDbContextFactory<LogDbContext>
    {
        public LogDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<LogDbContext>();

            // 여기에 로컬 DB나 개발용 DB 연결 문자열을 하드코딩해 줍니다. (마이그레이션 생성용)

            string connectionString = "Host=localhost;Port=5434;Database=LogCompareDb;Username=postgres;Password=password";

            // PostgreSQL 사용
            optionsBuilder.UseNpgsql(connectionString);

            return new LogDbContext(optionsBuilder.Options);
        }
    }
}