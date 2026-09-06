using LogLibrary.DB;
using LogWorker.Plugins;
using LogWorker.Providers;
using LogWorker.Workers;
using LogWorker.Interfaces;
using LogWorker.Managers;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

// 1. DB 컨텍스트 등록 (LogLibrary의 것을 사용)
builder.Services.AddDbContext<LogDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"), sqlOptions => sqlOptions.EnableRetryOnFailure()));

// 2. LLM 공급자 및 공통 옵션 DI 설정 (Ollama / vLLM 동적 선택 지원)
builder.Services.Configure<OllamaOptions>(builder.Configuration.GetSection("Ollama"));
builder.Services.Configure<VllmOptions>(builder.Configuration.GetSection("Vllm"));

builder.Services.AddSingleton<OllamaKernelProvider>();
builder.Services.AddSingleton<VllmKernelProvider>();

// 리사이클(런타임 스위칭) 지원을 위한 공급자 리졸버 등록
builder.Services.AddSingleton<ILlmResolver, LlmResolver>();

// 커널 내에서 자동 함수 호출(Function Calling)에 쓰일 데이터베이스 플러그인입니다.
// 작업 루프가 실행되는 스코프(Scope)별로 새로운 DB Context와 유기적으로 엮여야 하므로 Transient로 등록합니다.
builder.Services.AddTransient<LogDatabasePlugin>();

// 3. 백그라운드 서비스 및 핵심 비즈니스 로직 등록
// 프롬프트 관리자는 앱 수명 주기 전체에서 데이터를 공유하며, 인터페이스 기반으로 결합도를 낮추기 위해 Singleton 등록
builder.Services.AddSingleton<IPromptManager, PromptManager>();

// 로그 파싱 및 DB 적재 매니저는 각 배치 주기에 맞게 Scoped로 작동하며, 결합도를 낮추기 위해 인터페이스 매핑 등록
builder.Services.AddScoped<ILogIngestionManager, LogIngestionManager>();

// 백그라운드 워커 서비스 등록
builder.Services.AddHostedService<Worker>();

var host = builder.Build();

// DB 자동 마이그레이션 적용 (첫 가동 시 DB 생성 및 최신 마이그레이션 반영)
using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LogDbContext>();
    await db.Database.MigrateAsync();
}

host.Run();

//Ollama 사용시, 윈도우즈 환경의 호스트라면 환경변수에 OLLAMA_HOST / 0.0.0.0 추가하여 사용할 것!