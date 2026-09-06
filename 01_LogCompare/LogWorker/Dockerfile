# 1. 빌드 스테이지 (SDK 이미지 사용)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# NuGet 패키지 복원을 위해 .csproj 파일들을 먼저 복사 (캐시 최적화)
COPY ["LogWorker/LogWorker.csproj", "LogWorker/"]
COPY ["LogLibrary/LogLibrary.csproj", "LogLibrary/"]

# 패키지 복원 (LogWorker 프로젝트 기준)
RUN dotnet restore "LogWorker/LogWorker.csproj"

# 나머지 모든 소스 코드 복사
COPY . .

# 프로젝트 빌드 및 게시 (Release 모드)
WORKDIR "/src/LogWorker"
RUN dotnet publish "LogWorker.csproj" -c Release -o /app/publish /p:UseAppHost=false

# 2. 실행 스테이지 (가벼운 Runtime 이미지 사용)
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app

# 빌드 스테이지에서 생성된 결과물만 복사
COPY --from=build /app/publish .

# 프롬프트와 로그를 위한 볼륨 마운트 포인트 생성 (선택 사항)
RUN mkdir -p /app/prompts /app/raw_logs

# 앱 실행
ENTRYPOINT ["dotnet", "LogWorker.dll"]