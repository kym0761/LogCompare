using LogClient;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using System.Net;
using System.Net.Sockets;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

//builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

//붙을 API Server의 링크
//docker를 위해 http로 변경(원래는 https)
//test를 위해 ipconfig로 ip 확인 후 현재 컴퓨터 ip로 설정(원래 localhost)
//만약 처음 적용하는 컴퓨터이라면(windows 환경) 방화벽 설정 -> 인바운드 -> 새규칙 -> 포트 오픈 설정 필요.
var baseAddress = new Uri(builder.HostEnvironment.BaseAddress);

string apiBaseUrl = $"http://{baseAddress.Host}:7075/"; ;
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(apiBaseUrl) });

await builder.Build().RunAsync();
