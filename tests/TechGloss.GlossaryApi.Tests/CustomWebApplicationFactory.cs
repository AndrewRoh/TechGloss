using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TechGloss.Core.Models;
using TechGloss.GlossaryApi.Data;

namespace TechGloss.GlossaryApi.Tests;

/// <summary>
/// 테스트 전용 WebApplicationFactory.
/// - EF Core InMemory + 전용 서비스 프로바이더로 PostgreSQL 의존성 제거
/// - IClassFixture 인스턴스마다 고유한 DB 이름 → 테스트 클래스 간 격리 보장
/// </summary>
public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"TechGlossTest_{Guid.NewGuid():N}";

    // Npgsql과 InMemory가 동일한 DI 컨테이너에 공존하면 EF Core가 충돌을 감지한다.
    // 전용 서비스 프로바이더를 사용해 InMemory 컨텍스트를 완전히 격리한다.
    private static readonly IServiceProvider _inMemoryEfProvider =
        new ServiceCollection()
            .AddEntityFrameworkInMemoryDatabase()
            .BuildServiceProvider(validateScopes: false);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Npgsql DbContext 옵션 제거
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<GlossaryDbContext>));
            if (descriptor != null) services.Remove(descriptor);

            // InMemory DB로 교체 — 전용 서비스 프로바이더로 Npgsql 서비스와 충돌 방지
            services.AddDbContext<GlossaryDbContext>(opt =>
                opt.UseInMemoryDatabase(_dbName)
                   .UseInternalServiceProvider(_inMemoryEfProvider));
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GlossaryDbContext>();
        SeedTestData(db);

        return host;
    }

    private static void SeedTestData(GlossaryDbContext db)
    {
        if (db.Entries.Any()) return;

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
}
