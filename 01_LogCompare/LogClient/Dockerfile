# 1. 빌드 스테이지
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 프로젝트 파일 복사 및 복원
COPY ["LogClient/LogClient.csproj", "LogClient/"]
COPY ["LogLibrary/LogLibrary.csproj", "LogLibrary/"]
RUN dotnet restore "LogClient/LogClient.csproj"

# 전체 소스 복사 및 빌드 (Release 모드)
COPY . .
RUN dotnet build "LogClient/LogClient.csproj" -c Release -o /app/build

# Publish (정적 파일 생성)
FROM build AS publish
RUN dotnet publish "LogClient/LogClient.csproj" -c Release /p:UseAppHost=false

# 2. 실행 스테이지 (Nginx 사용)
FROM nginx:alpine AS final
WORKDIR /usr/share/nginx/html

# 위 스테이지에서 만들어진 정적 파일들(wwwroot)을 Nginx 경로로 복사
COPY --from=publish /src/LogClient/bin/Release/net10.0/publish/wwwroot .

# (중요) Nginx 기본 설정을 덮어쓰기 위해 설정 파일 복사 (아래 2번 단계 참고)
COPY LogClient/nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 7014
CMD ["nginx", "-g", "daemon off;"]