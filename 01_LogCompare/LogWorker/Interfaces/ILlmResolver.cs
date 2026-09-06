namespace LogWorker.Interfaces;

/// <summary>
/// 설정 파일의 감지 상태에 따라 동적으로 활성화된 LLM 프로바이더 및 옵션을 공급해 주는 리졸버 인터페이스
/// </summary>
public interface ILlmResolver
{
    /// <summary>
    /// 현재 설정된 LLM 서비스의 Kernel 공급 전담 프로바이더를 동적으로 획득합니다.
    /// </summary>
    IKernelProvider GetActiveProvider();

    /// <summary>
    /// 현재 설정된 LLM 서비스의 스케줄러 및 동작 설정 옵션 값을 동적으로 획득합니다.
    /// </summary>
    ILlmOptions GetActiveOptions();
}
