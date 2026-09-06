using LogWorker.Interfaces;

namespace LogWorker.Providers;

public class VllmOptions : ILlmOptions
{
    /// <summary>
    /// vLLM 서비스 또는 OpenAI 호환 로컬 API의 엔드포인트 주소
    /// </summary>
    public string Endpoint { get; set; } = "http://localhost:8000/v1";

    /// <summary>
    /// vLLM에 등록하여 구동 중인 기본 로컬 LLM 모델 식별자 (예: Llama-3-8B-Instruct 등)
    /// </summary>
    public string ModelId { get; set; } = "meta-llama/Meta-Llama-3-8B-Instruct";

    /// <summary>
    /// vLLM 서버 호출에 사용할 API Key (기본적으로 필요 없으나, 리버스 프록시나 게이트웨이가 있는 경우 설정 가능)
    /// </summary>
    public string? ApiKey { get; set; } = "no-key";


    // ──────────────── 플래너 및 자동 함수 호출(Function Calling) 옵션 ────────────────
    
    /// <summary>
    /// LLM의 플러그인 함수 호출 방식 설정 (Auto, Required, None)
    /// </summary>
    public string FunctionChoice { get; set; } = "Auto";

    /// <summary>
    /// LLM 응답 다양성 및 무작위성 제어 (0.0 ~ 2.0)
    /// </summary>
    public double Temperature { get; set; } = 0.1;

    /// <summary>
    /// LLM 응답 최대 토큰 수 (null인 경우 기본 제한 적용)
    /// </summary>
    public int? MaxTokens { get; set; } = 4096;

    /// <summary>
    /// LLM 자동 함수 호출(Tool Calling) 최대 반복 횟수 (플래너 최대 단계)
    /// </summary>
    public int MaxIterations { get; set; } = 5;

    /// <summary>
    /// LLM 자동 함수 호출 간 최소 대기 시간 (ms 단위, 로컬 LLM 부하 경감용)
    /// </summary>
    public int MinIterationTimeMs { get; set; } = 1000;

    /// <summary>
    /// 워커의 장비 분석 배치 작업 간 기본 대기 시간 (ms 단위)
    /// </summary>
    public int WorkerDelayMs { get; set; } = 5000;

    /// <summary>
    /// LLM 분석 기능(Phase 1 & Phase 2) 활성화 여부
    /// </summary>
    public bool EnableLLMAnalysis { get; set; } = true;

    /// <summary>
    /// 배치 작업 완료 후 다음 배치 실행 시까지 대기하는 주기 (분 단위, 기본 60분 = 1시간)
    /// </summary>
    public int BatchIntervalMinutes { get; set; } = 60;

    /// <summary>
    /// 등록된 설비가 없거나 오류 발생 시 재시도 대기 시간 (분 단위, 기본 10분)
    /// </summary>
    public int RetryIntervalMinutes { get; set; } = 10;
}
