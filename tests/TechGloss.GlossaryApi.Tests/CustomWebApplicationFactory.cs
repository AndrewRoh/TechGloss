using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TechGloss.Core.Models;
using TechGloss.GlossaryApi.Data;

namespace TechGloss.GlossaryApi.Tests;

/// <summary>
/// 테스트 전용 WebApplicationFactory.
/// - 인메모리 SQLite를 사용하여 파일 기반 glossary.db 의존성 제거
/// - IClassFixture 인스턴스마다 고유한 DB 이름 → 테스트 클래스 간 격리 보장
/// - seed.json 파일 경로 없이 코드에서 직접 시드 삽입
/// </summary>
public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    // 인메모리 SQLite는 마지막 연결이 닫히면 데이터가 사라지므로
    // Factory 수명 동안 연결 하나를 살려둠
    private readonly SqliteConnection _keepAlive;
    private readonly string _dbName = $"TechGlossTest_{Guid.NewGuid():N}";

    public CustomWebApplicationFactory()
    {
        _keepAlive = new SqliteConnection($"Data Source={_dbName};Mode=Memory;Cache=Shared");
        _keepAlive.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // 기존 DbContext 옵션 제거 (파일 기반 SQLite)
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<GlossaryDbContext>));
            if (descriptor != null) services.Remove(descriptor);

            // 인메모리 SQLite로 교체 (_keepAlive와 동일한 캐시 공유 → 데이터 유지)
            services.AddDbContext<GlossaryDbContext>(opt =>
                opt.UseSqlite($"Data Source={_dbName};Mode=Memory;Cache=Shared"));
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // base.CreateHost 내부에서 Program.cs 스타트업 코드 실행:
        //   db.Database.EnsureCreated() → 테이블 생성 완료
        //   SeedIfEmpty()              → seed.json 미발견 → 시드 없이 리턴
        // 따라서 여기서 직접 시드 삽입
        var host = base.CreateHost(builder);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GlossaryDbContext>();
        SeedTestData(db);

        return host;
    }

    private static void SeedTestData(GlossaryDbContext db)
    {
        if (db.Entries.Any()) return; // 중복 방지

        db.Entries.AddRange(
            Entry("11111111-0000-0000-0000-000000000001", "배포",       "deploy",
                "소프트웨어를 서버나 환경에 설치하고 실행 가능한 상태로 만드는 작업."),
            Entry("11111111-0000-0000-0000-000000000002", "빌드",       "build",
                "소스 코드를 컴파일·링크하여 실행 가능한 산출물을 생성하는 과정."),
            Entry("11111111-0000-0000-0000-000000000003", "렌더링",     "render",
                "데이터나 마크업을 화면에 시각적으로 출력하는 처리 과정."),
            Entry("11111111-0000-0000-0000-000000000004", "의존성",     "dependency",
                "코드나 패키지가 동작하기 위해 외부에서 필요로 하는 라이브러리·모듈."),
            Entry("11111111-0000-0000-0000-000000000005", "구현",       "implementation",
                "설계된 기능이나 알고리즘을 실제 코드로 작성하는 행위."),
            Entry("11111111-0000-0000-0000-000000000006", "캐시",       "cache",
                "자주 사용되는 데이터를 빠른 저장소에 임시 보관하여 접근 속도를 높이는 기법."),
            Entry("11111111-0000-0000-0000-000000000007", "인터페이스", "interface",
                "두 시스템이나 컴포넌트가 상호작용하는 방법을 정의한 계약."),
            Entry("11111111-0000-0000-0000-000000000008", "컨테이너",   "container",
                "애플리케이션과 실행 환경을 격리하는 경량 가상화 단위."),
            Entry("11111111-0000-0000-0000-000000000009", "마이크로서비스", "microservice",
                "단일 책임을 가진 소규모 서비스들의 집합으로 구성된 아키텍처 패턴."),
            Entry("11111111-0000-0000-0000-000000000010", "리팩터링",   "refactoring",
                "외부 동작을 유지하면서 코드 내부 구조를 개선하는 작업.")
        );
        db.SaveChanges();
    }

    private static GlossaryEntry Entry(string id, string ko, string en, string def) => new()
    {
        Id                = Guid.Parse(id),
        TermKo            = ko,
        TermEn            = en,
        TermKoNormalized  = ko,
        TermEnNormalized  = en,
        DefinitionKo      = def,
        Status            = "published",
        CreatedAt         = DateTimeOffset.UtcNow,
        UpdatedAt         = DateTimeOffset.UtcNow
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing) _keepAlive.Dispose();
        base.Dispose(disposing);
    }
}
