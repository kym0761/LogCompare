using LogWorker.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LogWorker.Providers;

/// <summary>
/// 설정 파일(appsettings.json)의 변화를 실시간으로 감지하고 반영할 수 있도록 ILlmResolver를 구현하는 클래스
/// </summary>
/// 
/// IOptionsMonitor<T>를 사용하는 이유:
/// IOptions<T>는 애플리케이션 시작 시점에 설정값을 고정하지만,
/// IOptionsMonitor<T>는 appsettings.json 파일 등의 물리적 파일 변경사항을 실시간(무중단)으로 감지하여 CurrentValue에 반영합니다.
public class LlmResolver(
    IConfiguration configuration,
    IOptionsMonitor<OllamaOptions> ollamaMonitor,
    IOptionsMonitor<VllmOptions> vllmMonitor,
    IServiceProvider serviceProvider,
    ILogger<LlmResolver> logger) : ILlmResolver
{
    private string GetProviderType()
    {
        return configuration["LlmProvider"] ?? "Ollama";
    }

    public IKernelProvider GetActiveProvider()
    {
        var provider = GetProviderType().Trim().ToLower();
        return provider switch
        {
            "vllm" => serviceProvider.GetRequiredService<VllmKernelProvider>(),
            "ollama" => serviceProvider.GetRequiredService<OllamaKernelProvider>(),
            _ => FallbackToDefaultProvider(provider)
        };
    }

    public ILlmOptions GetActiveOptions()
    {
        var provider = GetProviderType().Trim().ToLower();
        return provider switch
        {
            "vllm" => vllmMonitor.CurrentValue,
            "ollama" => ollamaMonitor.CurrentValue,
            _ => FallbackToDefaultOptions()
        };
    }

    private IKernelProvider FallbackToDefaultProvider(string invalidProvider)
    {
        logger.LogWarning("설정된 LlmProvider 값 '{Provider}'이(가) 올바르지 않습니다. 기본값인 'Ollama'로 폴백합니다.", invalidProvider);
        return serviceProvider.GetRequiredService<OllamaKernelProvider>();
    }

    private ILlmOptions FallbackToDefaultOptions()
    {
        // GetActiveProvider()에서 경고 로그가 출력되므로 여기서는 단순히 옵션 객체만 반환합니다.
        return ollamaMonitor.CurrentValue;
    }
}
