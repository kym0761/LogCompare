using System.Threading;
using System.Threading.Tasks;

namespace LogWorker.Interfaces;

public interface ILogIngestionManager
{
    /// <summary>
    /// 지정된 소스 경로의 원본 로그 파일들을 읽어 장비 정보 등록,
    /// 명령어 및 원본 로그 분할 파싱, DB 적재 및 아카이빙 백업 처리를 일괄 제어합니다.
    /// </summary>
    Task ProcessRawLogsAsync(CancellationToken stoppingToken);
}
