# 집 PC Hands-on 실습 가이드 (IIS)

집 PC 한 대에 **IIS(API) + SQL Server + Worker/Watchdog**를 같이 올리는 구성으로 회사 배포와 같은 경로를 재현할 수 있습니다. 에이전트는 같은 머신에서 `https://localhost`(또는 PC 이름)로 IIS에 붙습니다.

핵심만 먼저 말하면:

- IIS에는 **Native AOT(`api/win-x64`)가 아니라 `api/iis/win-x64`** 를 씁니다.
- HTTPS는 **IIS 사이트 바인딩**이 담당합니다. API `appsettings.json`에 `Kestrel:Endpoints`를 넣지 마세요.
- 에이전트는 기본 `HttpClient`라 **자체 서명 인증서를 신뢰 저장소에 넣지 않으면 TLS 검증이 실패**합니다.
- 수집만 빨리 보고 싶으면 IIS 없이 `http://127.0.0.1:5080` loopback도 허용됩니다. 다만 IIS로 가려면 HTTPS가 맞습니다.

회사 배포 전체 절차는 [company-deployment.md](company-deployment.md)를, 프로젝트 개요는 [README.md](../README.md)를 참고하세요.

## 한 대에서의 역할 배치

```
집 PC
├── SQL Server Express (또는 Developer)
├── IIS 사이트  https://localhost  (또는 https://내PC이름)
│     └── api/iis/win-x64  (ASP.NET Core in-process)
└── Windows Service
      ├── SwLicenseWatcher.Agent.Worker
      └── SwLicenseWatcher.Agent.Watchdog
```

회사 문서의 "서버 vs PC"가 **같은 머신**에 겹칩니다. `Install-Agent.ps1`은 API를 설치하지 않고, IIS는 `Install-ApiServer.ps1`을 쓰지 않습니다(그 스크립트는 Kestrel AOT 전용).

## 0. 준비물

| 항목 | 집 테스트에서 |
| --- | --- |
| Windows | IIS는 Pro가 편합니다. Home이면 "Windows 기능"에서 IIS를 켜세요. |
| [.NET 10 ASP.NET Core Hosting Bundle](https://dotnet.microsoft.com/download/dotnet/10.0) | **IIS에 필수.** AspNetCoreModuleV2 + 공유 런타임 |
| SQL Server Express + SSMS | LocalDB도 가능하지만 Express가 IIS(LocalSystem)와 맞추기 쉽습니다 |
| 산출물 | GitHub Release ZIP, 또는 소스에서 publish |
| 관리자 PowerShell | 서비스 설치, IIS, 인증서 |

소스에서 IIS용 API만 만들 때:

```powershell
dotnet publish src\SwLicenseWatcher.Api\SwLicenseWatcher.Api.csproj `
  -r win-x64 -c Release --self-contained false `
  -p:PublishAot=false -p:PublishTrimmed=false -p:UseAppHost=false `
  -o C:\SwLw\api-iis
```

에이전트는 Release ZIP의 `agent-worker\win-x64`, `agent-watchdog\win-x64`를 쓰거나, 같은 방식으로 Worker/Watchdog을 publish하면 됩니다.

## 1. SQL Server

1. Express 설치 후 인스턴스 예: `localhost\SQLEXPRESS`
2. 데이터베이스 생성: `SwLicenseWatcher`
3. IIS 앱 풀이 **LocalSystem**이면 `NT AUTHORITY\SYSTEM`에 해당 DB 권한을 줍니다. 별도 SQL 계정을 쓰는 편이 더 단순합니다.

집 테스트용 연결 문자열 예:

```text
Server=localhost\SQLEXPRESS;Database=SwLicenseWatcher;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=True
```

운영 문서의 `TrustServerCertificate=False`는 집 자체 서명 SQL 인증서에서는 자주 막힙니다. 테스트에서만 `True`로 두면 됩니다.

스키마는 API `Database:ApplySchemaOnStartup`을 `true`로 두는 것이 가장 쉽습니다. 기동 시 DDL을 적용하고, 실패하면 사이트가 뜨지 않습니다.

## 2. IIS + HTTPS

[company-deployment.md](company-deployment.md)의 IIS 절차를 집에 맞춘 버전입니다.

1. IIS 기능: ASP.NET이 아니라 **IIS + 관리 콘솔**. CLR은 Hosting Bundle이 처리합니다.
2. Hosting Bundle 설치 후 `iisreset`.
3. 애플리케이션 풀
   - .NET CLR 버전: **No Managed Code**
   - 32비트: 사용 안 함
   - ID: LocalSystem 또는 SQL에 권한을 준 계정
4. 사이트 실제 경로: `C:\SwLw\api-iis` (publish 또는 Release의 `api\iis\win-x64`)
5. HTTPS 바인딩: 포트 443, 호스트 이름은 비우거나 `localhost` / PC 이름과 맞추기

자체 서명 인증서 (관리자 PowerShell):

```powershell
$cert = New-SelfSignedCertificate `
  -DnsName "localhost", $env:COMPUTERNAME `
  -CertStoreLocation "Cert:\LocalMachine\My"

# 에이전트 HttpClient가 검증하므로 신뢰 루트에도 넣습니다
$root = "Cert:\LocalMachine\Root"
Export-Certificate -Cert $cert -FilePath $env:TEMP\swlw-iis.cer | Out-Null
Import-Certificate -FilePath $env:TEMP\swlw-iis.cer -CertStoreLocation $root

# IIS 사이트 HTTPS 바인딩에 이 인증서 연결 (사이트 이름은 본인 것으로)
New-WebBinding -Name "SwLicenseWatcher" -Protocol https -Port 443 -IPAddress "*"
# 바인딩에 인증서 연결은 IIS 관리자 SSL 인증서 드롭다운이 가장 안전합니다
```

브라우저에서 `https://localhost/health`가 인증서 경고 없이 열려야 에이전트도 통과하기 쉽습니다.

`web.config`는 산출물에 이미 있습니다 (`processPath="dotnet"`, `arguments=".\SwLicenseWatcher.Api.dll"`, `hostingModel="InProcess"`). AOT exe 폴더를 가리키면 `500.30` / `500.31`이 납니다.

로그가 필요하면 `stdoutLogEnabled="true"`로 바꾸고 사이트 폴더에 `logs`를 만듭니다.

## 3. API `appsettings.json`

[deploy/examples/appsettings.api.iis.company.json](../deploy/examples/appsettings.api.iis.company.json)을 사이트 폴더의 `appsettings.json`으로 복사한 뒤 집 값만 바꿉니다.

```powershell
cd C:\Source\sw-license-watcher
$agentToken = .\deploy\scripts\New-ApiToken.ps1
$adminToken = .\deploy\scripts\New-ApiToken.ps1
# 두 값을 메모해 둡니다. Agent와 Admin이 같으면 API가 시작하지 않습니다.
```

바꿀 키:

| 키 | 집 테스트 권장 |
| --- | --- |
| `Security:AgentToken` / `AdminToken` | 위에서 만든 32자 이상, **서로 다름** |
| `Security:RequireHttps` | `true` (IIS가 TLS 종료) |
| `Storage:SqlServer:ConnectionString` | Express 연결 문자열 |
| `Database:ApplySchemaOnStartup` | `true` |
| `Notifications:Webhook/Smtp:Enabled` | `false` |
| `Updates:Worker:RequireAuthenticode` | 서명 없는 빌드면 **`false`** |
| `Updates:Worker:PackageUrl` | 자체 패치를 안 하면 플레이스홀더 HTTPS URL로 기동은 됩니다 |
| `Kestrel` | **넣지 않음** |

풀을 재순환한 뒤:

```powershell
Invoke-RestMethod https://localhost/health
# 브라우저: https://localhost/admin  → AdminToken 입력
```

`/health`가 503이면 SQL 연결 또는 스키마입니다. stdout 로그와 Application 이벤트 로그를 봅니다.

## 4. 같은 PC에 에이전트 설치

API가 뜬 뒤에, **관리자 PowerShell**에서:

```powershell
.\deploy\scripts\Install-Agent.ps1 `
  -SourcePath D:\SwLicenseWatcher-1.0.x `
  -ServerBaseUrl "https://localhost" `
  -ApiToken $agentToken
```

`-SourcePath`는 압축 푼 Release 루트입니다. 스크립트는 `agent-worker` / `agent-watchdog`만 복사합니다.

기본 `DeviceCode`는 컴퓨터 이름입니다. 소스의 `pc-demo-001`을 쓰지 마세요. 나중에 회사 PC와 섞이면 한 자산으로 UPSERT됩니다.

확인:

```powershell
Get-Service SwLicenseWatcher.Agent.Worker, SwLicenseWatcher.Agent.Watchdog
Get-Content C:\ProgramData\SwLicenseWatcher\state\worker-health.json
Invoke-RestMethod -Headers @{ Authorization = "Bearer $adminToken" } `
  -Uri "https://localhost/api/inventory/devices"
```

대시보드 `/admin`에서 자기 PC와 설치 SW가 보이면 수집 경로가 된 것입니다.

기본 수집 주기는 **30분 ± 15분**입니다. 바로 보려면 설치 후 Worker `appsettings.json`에서:

```json
"PollInterval": "00:01:00",
"MaxJitter": "00:00:05"
```

서비스를 재시작하거나, 설치 전에 진단 1회 실행:

```powershell
dotnet run --project src\SwLicenseWatcher.Agent.Worker -- `
  --Agent:RunOnceForDiagnostics=true `
  --Agent:ServerBaseUrl=https://localhost `
  --Agent:ApiToken=$agentToken `
  --Agent:DeviceCode=$env:COMPUTERNAME
```

`dotnet run`은 LocalSystem이 아니라 현재 사용자로 레지스트리를 읽으므로, **서비스 설치 결과와 Uninstall 키 범위가 조금 다를 수** 있습니다. 회사와 같게 보려면 서비스 설치가 맞습니다.

## 5. HTTPS vs loopback HTTP

같은 PC라도 URL에 따라 규칙이 갈립니다.

- `https://localhost` / `https://내PC이름` → IIS 테스트에 적합. 인증서 SAN에 그 이름이 있어야 합니다.
- `http://127.0.0.1:5080` → 에이전트가 **loopback HTTP만** 허용. IIS가 아니라 `dotnet run` / Kestrel 개발용입니다.
- `http://192.168.x.x` 또는 공인 IP HTTP → 에이전트가 **시작 거부**.

IIS HTTP 바인딩만 열고 `http://localhost`로 에이전트를 붙이는 것도 loopback이라 기동은 됩니다. 다만 `RequireHttps`가 켜져 있어도 API는 loopback HTTP를 통과시킵니다. **IIS 실사용에 가깝게 보려면 HTTPS를 쓰는 쪽**이 맞습니다.

인증서 이름 불일치 예: 바인딩은 `localhost`인데 `ServerBaseUrl`을 `https://DESKTOP-XXXX`로 두면 에이전트 TLS가 실패합니다. 인증서 DNS와 URL을 맞추세요.

## 6. 자체 패치까지 집에서 보려면

Watchdog 기본 주기는 **4시간 ± 1시간**입니다. 테스트 시 `CheckInterval`을 줄이세요.

1. Worker ZIP(`SwLicenseWatcher.Agent.Worker-{version}.zip`)을 IIS 가상 디렉터리나 다른 HTTPS 경로에 둡니다. URL은 **HTTPS**여야 합니다.
2. API `Updates:Worker`에 `Version`, `PackageUrl`, `Sha256`을 넣습니다.

```powershell
(Get-FileHash -Algorithm SHA256 C:\SwLw\packages\SwLicenseWatcher.Agent.Worker-1.0.x.zip).Hash
```

3. CD에 서명 시크릿이 없으면 ZIP 안 EXE가 서명되지 않습니다. 집에서는 `RequireAuthenticode: false`가 현실적입니다.

Watchdog은 설치 폴더의 `appsettings.json`을 패치 후에도 유지합니다.

## 7. 자주 막히는 지점

| 증상 | 원인 |
| --- | --- |
| IIS 500.30 / 500.31 | Hosting Bundle 없음, 앱 풀이 CLR 있음, **AOT 폴더를 가리킴** |
| API가 바로 죽음 | AgentToken=AdminToken, 토큰 32자 미만, SQL 연결 실패, `ApplySchemaOnStartup` 실패 |
| 에이전트 즉시 종료 | 빈 `ApiToken`, 비-loopback HTTP URL |
| 401 | PC `ApiToken` ≠ 서버 `AgentToken` |
| TLS/SSL 오류 | 자체 서명을 Root에 안 넣음, 호스트 이름 불일치 |
| 스냅샷이 안 보임 | 아직 PollInterval, 또는 큐에 적재됨(`C:\ProgramData\SwLicenseWatcher\state\queue`) |
| IIS JSON에 Kestrel 포트 | IIS와 포트 충돌. IIS용 템플릿 사용 |

제거:

```powershell
.\deploy\scripts\Uninstall-Agent.ps1 -RemoveState
# IIS 사이트·앱 풀은 관리자에서 수동 삭제
```

## 8. 집에서 하면 안 되는 것

- 공유기에서 443을 **인터넷으로 포트포워딩**하지 마세요. 토큰만 있는 수집 API입니다.
- 토큰·연결 문자열을 git에 커밋하지 마세요.
- 알림 webhook/SMTP는 집 테스트에서 꺼 두는 것이 안전합니다.
