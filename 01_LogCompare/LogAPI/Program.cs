using LogLibrary.Classes;
using LogAPI.Classes.DB_Data;
using LogLibrary.DB;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using LogLibrary.Services;

var builder = WebApplication.CreateBuilder(args);

// --- 1. 기본 서비스 및 OpenAPI/Swagger 설정 ---
builder.Services.AddOpenApi();


#region PostgreSQL Settings
// --- 2. 데이터베이스 및 비즈니스 로직 서비스 등록 ---
// appsettings.json 또는 환경 변수에서 연결 문자열(ConnectionString)을 로드합니다.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// PostgreSQL DbContext 등록 (일시적인 연결 실패에 대비해 자동 재시도 패턴 EnableRetryOnFailure 적용)
builder.Services.AddDbContext<LogDbContext>(options => 
    options.UseNpgsql(connectionString, sqlOptions => sqlOptions.EnableRetryOnFailure()));

// 로그 파일 분석 및 업로드를 담당하는 비즈니스 서비스 등록
builder.Services.AddScoped<LogUploadService>();
#endregion


#region API CORS Settings
// CORS 설정 추가
//Cross Origin Resource Sharing
builder.Services.AddCors(options => {
    options.AddDefaultPolicy(policy => {policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();});
});
#endregion

var app = builder.Build();

// 전역 예외 처리 미들웨어 추가 (에러 발생 시 일관된 JSON 응답 반환)
app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { error = "서버 내부 오류가 발생했습니다.", status = 500 });
    });
});

// Configure the HTTP request pipeline.
// [개발용 API 문서화 도구 설정]
// 개발 환경(Development)인 경우 Scalar 및 OpenAPI JSON 엔드포인트를 제공합니다.
if (app.Environment.IsDevelopment())
{
    // https://localhost:7075/openapi/v1.json
    app.MapOpenApi();

    // https://localhost:7075/scalar/v1
    app.MapScalarApiReference();

}

// [HTTPS 리디렉션 및 보안 설정]
if (OperatingSystem.IsWindows())
{
    //apsettings.json에는 visual studio가 바라볼 connectionstring을
    //docker-compose 에는 api가 바라볼 connectionstring을 써준다.
    app.UseHttpsRedirection();
}


app.UseCors();

#region HTTPs Get
// --- 5. 조회(GET) 관련 API 엔드포인트 정의 ---

/*
// 예: 로그 조회 API (전체 데이터 풀스캔으로 인한 OOM, 성능 저하 문제로 주석 처리)
// 현재 프론트엔드(LogClient)에서는 이 엔드포인트를 사용하지 않습니다.
// 향후 관리자 페이지 등에서 전체 조회가 필요하다면 반드시 Paging(Skip/Take) 처리를 추가하여 부활시켜야 합니다.
app.MapGet("/api/logs", async (LogDbContext db) =>
    await db.RawLogs.OrderByDescending(l => l.Date).ToListAsync());
*/

// [특정 로그 상세 조회 API]
// 고유 ID(int)를 경로 변수로 전달받아 해당하는 로그 정보를 단건 조회합니다.
app.MapGet("/api/logs/{id:int}", async (int id, LogDbContext db) =>
{
    var log = await db.RawLogs.FindAsync(id);

    // 2. 데이터가 없으면 404 Not Found를 반환합니다.
    if (log is null)
    {
        return Results.NotFound(new { Message = $"{id}번 로그를 찾을 수 없습니다." });
    }

    return Results.Ok(log);
});


// [1. 모든 장비 이름 가져오기]
// DB에 등록된 모든 장비의 이름을 리스트 형태로 가져옵니다. (로그 비교 화면의 장비 선택 드롭다운용)
app.MapGet("/api/compare/devices", async (LogDbContext db) =>
    await db.Devices.Select(d => d.DeviceName).ToListAsync());

// [2. 특정 장비의 가용한 모든 날짜 가져오기]
// 입력받은 장비 이름(deviceName)에 해당하는 원본 로그들 중에서 고유한(Distinct) 날짜들을 역순으로 정렬하여 반환합니다.
app.MapGet("/api/compare/dates", async (string deviceName, LogDbContext db) =>
    await db.RawLogs
        .Where(l => l.DeviceName == deviceName)
        .Select(l => l.Date)
        .Distinct()
        .OrderByDescending(d => d)
        .ToListAsync());

// [3. 특정 장비와 날짜에 가용한 명령어 리스트 가져오기]
// 입력받은 장비 이름(deviceName)에 해당하는 원본 로그의 명령어(Command) 목록을 중복 없이 파싱된(ID) 순으로 정렬하여 반환합니다.
// 날짜(leftDate, rightDate)가 전달된 경우 해당 날짜의 명령어들만 필터링합니다.
app.MapGet("/api/compare/commands", async (
    string deviceName, 
    DateOnly? leftDate, 
    DateOnly? rightDate, 
    LogDbContext db) =>
{
    var query = db.RawLogs.Where(l => l.DeviceName == deviceName);

    if (leftDate.HasValue && rightDate.HasValue)
    {
        query = query.Where(l => l.Date == leftDate.Value || l.Date == rightDate.Value);
    }
    else if (leftDate.HasValue)
    {
        query = query.Where(l => l.Date == leftDate.Value);
    }
    else if (rightDate.HasValue)
    {
        query = query.Where(l => l.Date == rightDate.Value);
    }

    return await query
        .GroupBy(l => l.Command)
        .OrderBy(g => g.Min(l => l.Id)) // 파싱된(ID) 순서로 정렬
        .Select(g => g.Key)
        .ToListAsync();
});

// [4. 선택된 조건의 실제 로그 내용 가져오기]
// 지정된 장비, 날짜, 명령어 조건에 부합하는 원본 로그의 실제 내용(Content)을 단건 조회하여 반환합니다.
app.MapGet("/api/compare/content", async (string deviceName, DateOnly date, string command, LogDbContext db) =>
{
    var log = await db.RawLogs
        .FirstOrDefaultAsync(l => l.DeviceName == deviceName && l.Date == date && l.Command == command);
    return log?.Content ?? "";
});

// [5. 요약본 및 정밀 진단 통합 조회 API]
// 지정된 장비, 날짜, 명령어 조건에 해당하는 로그의 AI 1차 요약 및 2차 교차 진단 결과를 함께 조회합니다.
app.MapGet("/api/summary", async (
    string deviceName,
    string date,
    string command,
    LogDbContext db) =>
{
    // 날짜 문자열을 DateOnly로 변환 (파싱 실패 시 BadRequest 반환)
    if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var parsedDate))
    {
        return Results.BadRequest("잘못된 날짜 형식입니다. (yyyy-MM-dd 필요)");
    }

    // DB에서 해당 장비, 날짜, 명령어에 일치하는 1차 요약본 단건 조회
    var summaryEntry = await db.LogSummaries
        .FirstOrDefaultAsync(s =>
            s.DeviceName == deviceName &&
            s.Date == parsedDate &&
            s.Command == command);

    if (summaryEntry == null)
    {
        return Results.Ok(new SummaryResponseDto("", "Normal", null));
    }

    // 해당 요약에 연계된 2차 정밀 교차 진단 결과 조회
    var diagnosisEntry = await db.LogDiagnoses
        .FirstOrDefaultAsync(d =>
            d.DeviceName == deviceName &&
            d.Date == parsedDate &&
            d.Command == command);

    DiagnosisResponseDto? diagnosisDto = null;
    if (diagnosisEntry != null)
    {
        diagnosisDto = new DiagnosisResponseDto(
            diagnosisEntry.DiagnosisText,
            diagnosisEntry.ReferencedCommands,
            diagnosisEntry.Severity,
            diagnosisEntry.UpdatedAt
        );
    }

    return Results.Ok(new SummaryResponseDto(
        summaryEntry.SummaryText,
        summaryEntry.Severity,
        diagnosisDto
    ));
});

// [6. 2차 정밀 교차 진단 단독 조회 API]
app.MapGet("/api/diagnosis", async (
    string deviceName,
    string date,
    string command,
    LogDbContext db) =>
{
    if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var parsedDate))
    {
        return Results.BadRequest("잘못된 날짜 형식입니다. (yyyy-MM-dd 필요)");
    }

    var diagnosis = await db.LogDiagnoses
        .FirstOrDefaultAsync(d =>
            d.DeviceName == deviceName &&
            d.Date == parsedDate &&
            d.Command == command);

    if (diagnosis == null)
    {
        return Results.NotFound(new { Message = "해당 조건의 2차 정밀 진단 결과가 존재하지 않습니다." });
    }

    return Results.Ok(diagnosis);
});

#endregion


#region HTTPs Post
// [로그 파일 업로드 및 분석/저장 API]
// 폼 데이터(Multipart Form-Data) 형식으로 로그 파일을 업로드받아 파싱 및 DB 저장을 수행합니다.
app.MapPost("/api/logs/upload", async ([FromForm] IFormFile file, LogUploadService uploadService) =>
{
    using var stream = file.OpenReadStream();
    var result = await uploadService.ProcessUploadAsync(stream, file.FileName);
    
    if (result.IsSuccess)
    {
        return Results.Ok(new
        {
            Message = result.Message,
            Device = result.DeviceName,
            Count = result.UploadedCount
        });
    }
    else if (result.StatusCode == 409)
    {
        return Results.Conflict(new { Message = result.Message });
    }
    else if (result.StatusCode == 400)
    {
        return Results.BadRequest(new { Message = result.Message });
    }
    else
    {
        return Results.Problem(detail: result.Message, title: "데이터 저장 오류");
    }
})
.DisableAntiforgery(); // API 엔드포인트 특성상 CSRF/Antiforgery 토큰 검증을 비활성화합니다.

#endregion


// --- 7. 호스트 애플리케이션 실행 ---
// 실행 중인 운영체제가 Windows 환경인 경우 명시적으로 https://localhost:7075 주소로 바인딩하여 실행하고,
// 그 외 리눅스/도커 환경 등인 경우 프레임워크 기본 설정을 따릅니다.
if (OperatingSystem.IsWindows())
{
    app.Run("https://localhost:7075");
}
else 
{
    app.Run();
}

public record SummaryResponseDto(string SummaryText, string Severity, DiagnosisResponseDto? Diagnosis);
public record DiagnosisResponseDto(string DiagnosisText, string ReferencedCommands, string Severity, DateTime UpdatedAt);


