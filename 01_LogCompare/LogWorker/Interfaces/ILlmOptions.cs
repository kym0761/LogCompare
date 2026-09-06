namespace LogWorker.Interfaces;

/// <summary>
/// LLM 동작 및 백그라운드 워커 스케줄링에 필요한 공통 설정 옵션을 정의하는 인터페이스
/// </summary>
public interface ILlmOptions
{
    /// <summary> LLM 서비스의 호환 엔드포인트 주소 </summary>
    string Endpoint { get; }

    /// <summary> LLM 모델 명 </summary>
    string ModelId { get; }

    /// <summary> 자동 함수 호출 플래너 동작 모드 (Auto, Required, None) </summary>
    string FunctionChoice { get; }

    /// <summary> LLM 응답 무작위성 온도 제어 (0.0 ~ 2.0) </summary>
    double Temperature { get; }

    /// <summary> LLM 응답 최대 토큰 수 </summary>
    int? MaxTokens { get; }

    /// <summary> 자동 함수 호출 최대 반복 횟수 </summary>
    int MaxIterations { get; }

    /// <summary> 자동 함수 호출 간 최소 대기 시간 (ms) </summary>
    int MinIterationTimeMs { get; }

    /// <summary> 워커의 분석 배치 작업 간 기본 대기 시간 (ms) </summary>
    int WorkerDelayMs { get; }

    /// <summary> LLM 분석 분석 프로세스 전체 활성화 여부 </summary>
    bool EnableLLMAnalysis { get; }

    /// <summary> 배치 작업 주기 (분) </summary>
    int BatchIntervalMinutes { get; }

    /// <summary> 오류 발생 시 재시도 대기 시간 (분) </summary>
    int RetryIntervalMinutes { get; }
}
