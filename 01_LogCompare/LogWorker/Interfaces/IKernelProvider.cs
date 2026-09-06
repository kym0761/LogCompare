using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace LogWorker.Interfaces;

/// <summary>
/// 특정 LLM 엔진(Ollama, vLLM 등)에 맞춘 Semantic Kernel 인스턴스 및 실행 설정을 생성하는 프로바이더 인터페이스
/// </summary>
public interface IKernelProvider
{
    /// <summary>
    /// 지정된 서비스 제공자(DI 컨테이너)의 플러그인을 포함하여 특정 LLM 호환 엔드포인트를 바라보는 Kernel 인스턴스를 생성합니다.
    /// </summary>
    Kernel CreateKernel(IServiceProvider serviceProvider);

    /// <summary>
    /// 해당 LLM 프로바이더 동작에 필요한 시스템 프롬프트 및 함수 호출(Function Calling) 실행 설정을 가져옵니다.
    /// </summary>
    OpenAIPromptExecutionSettings GetExecutionSettings();
}
