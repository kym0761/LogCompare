using LogWorker.Plugins;
using LogWorker.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace LogWorker.Providers;

public class OllamaKernelProvider(IOptions<OllamaOptions> options, IPromptManager promptManager) : IKernelProvider
{
    private readonly OllamaOptions _options = options.Value;
    private readonly IPromptManager _promptManager = promptManager;


    public Kernel CreateKernel(IServiceProvider serviceProvider)
    {
        // 1. Semantic Kernel 빌더 생성 및 Ollama OpenAI 호환 엔드포인트 연동 설정
        var builder = Kernel.CreateBuilder()
            .AddOpenAIChatCompletion(
                modelId: _options.ModelId,     // Ollama에 등록된 로컬 모델명 (예: gemma4:e4b)
                apiKey: "no-key",              // Ollama는 로컬 구동이므로 별도의 API 키가 필요 없음
                endpoint: new Uri(_options.Endpoint) // Ollama 로컬 서비스 API 주소
            );

        // 2. 사용할 플러그인들을 등록한다.
        // 플러그인은 각 작업 루프에서 커널이 생성될 때 DI 컨테이너(Scope)로부터 가져오므로 Transient로 관리됩니다.
        var dbPlugin = serviceProvider.GetRequiredService<LogDatabasePlugin>();
        builder.Plugins.AddFromObject(dbPlugin);

        /* 
         * !여기에! 향후 요구사항에 따라 다른 플러그인들을 추가적으로 장착할 수 있습니다.
         * builder.Plugins.AddFromObject(serviceProvider.GetRequiredService<AnotherPlugin>());
         */

        var kernel = builder.Build();

        // 3. 플래너 자동 호출 제어를 위한 커스텀 자동 함수 호출 필터(IAutoFunctionInvocationFilter) 등록
        kernel.AutoFunctionInvocationFilters.Add(new OllamaAutoInvocationFilter(_options));

        return kernel;
    }

    public OpenAIPromptExecutionSettings GetExecutionSettings()
    {
        // 1. 설정 옵션값에 따른 자동 함수 호출 플래너 동작(FunctionChoiceBehavior) 결정
        FunctionChoiceBehavior behavior = _options.FunctionChoice.ToLower() switch
        {
            "required" => FunctionChoiceBehavior.Required(),
            "none" => FunctionChoiceBehavior.None(),
            _ => FunctionChoiceBehavior.Auto() // 기본값 Auto
        };

        // LLM 기본 동작 설정을 제공합니다. (System Prompt 지정 및 동적 바인딩된 플래너 기능 포함)
        return new OpenAIPromptExecutionSettings
        {
            ChatSystemPrompt = _promptManager.GetSystemPrompt(),
            FunctionChoiceBehavior = behavior, // 동적으로 보완된 behavior 적용
            MaxTokens = _options.MaxTokens,     // 옵션으로 전달받은 최대 토큰 수
            Temperature = _options.Temperature  // 옵션으로 전달받은 온도 설정
        };
    }
}

/// <summary>
/// 자동 함수 호출(Tool Calling) 과정에서 이터레이션 한도 및 최소 대기 시간을 통제하는 커스텀 필터
/// </summary>
public class OllamaAutoInvocationFilter(OllamaOptions options) : IAutoFunctionInvocationFilter
{
    public async Task OnAutoFunctionInvocationAsync(AutoFunctionInvocationContext context, Func<AutoFunctionInvocationContext, Task> next)
    {
        // 1. 최대 반복(Iteration) 한도 점검 및 차단
        if (context.RequestSequenceIndex >= options.MaxIterations)
        {
            context.Terminate = true; // 플래너 루프 강제 종료
            return;
        }

        //빠른 테스트를 위해 비활성화
        //// 2. 로컬 LLM 과부하 및 속도 제한을 방지하기 위한 최소 대기 시간 보장
        //if (options.MinIterationTimeMs > 0)
        //{
        //    await Task.Delay(options.MinIterationTimeMs);
        //}

        // 3. 다음 이터레이션 또는 실제 함수 실행 진행
        await next(context);
    }
}




