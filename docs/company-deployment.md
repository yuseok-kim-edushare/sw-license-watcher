# 회사 배포 안내

회사 PC에는 Worker와 Watchdog만 설치합니다. **API는 서버에서 호스팅하는 웹 앱이며, 클라이언트에 복사하지 않습니다.** 개발용 `dotnet run`은 [README.md](../README.md)를 참고하세요.

에이전트는 PC의 `appsettings.json`에 적은 서버 URL과 에이전트 토큰으로 API에 접속합니다.

```
회사 PC (클라이언트)                       회사 서버 (여기만 API)
┌──────────────────────────┐              ┌─────────────────────────┐
│ Worker   수집·전송        │  HTTPS      │ ASP.NET Core API        │
│ Watchdog 자체 패치        │  Bearer ──► │ SQL Server              │
│ appsettings.json         │              │ appsettings.json        │
└──────────────────────────┘              └─────────────────────────┘
```

```
deploy/
  examples/
    appsettings.api.company.json       서버 API Kestrel (PC에 복사하지 않음)
    appsettings.api.iis.company.json   서버 API IIS (Kestrel 바인딩 없음)
    appsettings.worker.company.json    PC Worker
    appsettings.watchdog.company.json  PC Watchdog
  scripts/
    New-ApiToken.ps1        토큰 생성 (에이전트용·관리자용을 따로 두 번 실행)
    Apply-DbSchema.ps1      SQL Server 스키마 적용 (API 또는 .sql 파일)
    Install-ApiServer.ps1   서버에 Kestrel Windows Service 설치
    Uninstall-ApiServer.ps1
    Install-Agent.ps1       PC에 Worker + Watchdog만 설치
    Uninstall-Agent.ps1
```

Release ZIP의 `api/win-x64`(AOT)와 `api/iis/win-x64`(IIS)는 서버용입니다. `Install-ApiServer.ps1`은 `api/win-x64`만 복사하고, `Install-Agent.ps1`은 `agent-worker`와 `agent-watchdog`만 복사합니다.

## 1. 구성 요소

| 구성 요소 | 어디에 | 역할 |
| --- | --- | --- |
| API | 서버만 | 스냅샷·하트비트 수신, SQL 저장, 업데이트 manifest |
| Worker | PC | Uninstall 레지스트리로 설치 SW 수집, 서버로 전송 |
| Watchdog | PC | 서버 manifest로 Worker 패키지를 받아 교체·롤백 |

소스의 에이전트 `appsettings.json`은 로컬 개발용입니다 (`pc-demo-001`, `http://localhost:5080`, 빈 토큰). 그대로 PC에 복사하면 DeviceCode가 전 장비에서 겹치거나 서비스가 시작되지 않습니다.

## 2. 사전 요구 사항

- PC: Windows x64, 관리자 PowerShell 5.1 이상. Native AOT라 대상 PC에 .NET 런타임은 필요 없습니다.
- 서버 API URL (HTTPS). HTTP는 loopback 진단만 허용됩니다.
- 서버 `Security:AgentToken`(32자 이상). PC `ApiToken`과 동일. 조회·정책 API는 `Security:AdminToken`을 씁니다. `AgentToken`/`AdminToken`이 비어 있으면 레거시 `Security:Token`이 모든 엔드포인트에 유효합니다.
- 자체 패치를 쓸 때: 서버가 가리키는 Worker ZIP(`SwLicenseWatcher.Agent.Worker-{version}.zip`)에 `SwLicenseWatcher.Agent.Worker.exe`가 정확히 하나. `RequireAuthenticode`가 `true`이면 EXE/DLL이 신뢰된 Authenticode 서명

토큰과 서명용 PFX는 저장소에 커밋하지 마세요.

### GitHub Release 서명 (선택)

CD는 다음 시크릿이 있으면 publish 산출물의 EXE/DLL에 Authenticode 서명을 합니다. 없으면 바이너리는 서명되지 않습니다.

| 시크릿 | 필수 | 설명 |
| --- | --- | --- |
| `SIGNING_CERTIFICATE_PFX_BASE64` | 서명할 때 | PFX의 Base64 |
| `SIGNING_CERTIFICATE_PASSWORD` | 서명할 때 | PFX 암호 |
| `SIGNING_TIMESTAMP_URL` | | 없으면 `http://timestamp.digicert.com` |

## 3. 서버 API

API는 서버에서만 호스팅합니다. PC 에이전트 설치 대상이 아닙니다. Kestrel(Native AOT)은 `Install-ApiServer.ps1`로 Windows Service로 등록하고, IIS in-process는 아래 수동 절차를 따릅니다. Release ZIP에는 두 가지 서버 산출물이 있습니다.

| 산출물 | 언제 | 설정 예시 |
| --- | --- | --- |
| `api/win-x64/` Native AOT | Kestrel Windows Service (`Install-ApiServer.ps1`) | [appsettings.api.company.json](../deploy/examples/appsettings.api.company.json) |
| `api/iis/win-x64/` framework-dependent | IIS in-process (`web.config`) | [appsettings.api.iis.company.json](../deploy/examples/appsettings.api.iis.company.json) |

공통 키:

| 키 | 필수 | 설명 |
| --- | --- | --- |
| `Security:AgentToken` | 권장 | 32자 이상. 스냅샷·하트비트·manifest만. PC `ApiToken`과 동일 |
| `Security:AdminToken` | 권장 | 32자 이상. 조회·정책·위반·design/schema. `AgentToken`과 달라야 함 |
| `Security:Token` | 레거시 | 32자 이상. Agent/Admin이 비어 있으면 모든 엔드포인트 |
| `Security:RequireHttps` | | 운영은 `true`. 원격 HTTP는 거부, loopback HTTP는 허용 |
| `Storage:SqlServer:ConnectionString` | 예 | `TrustServerCertificate=False` 권장 |
| `Storage:SqlServer:SchemaName` 및 테이블/컬럼 | 예 | 기본 예시는 `inventory.company_pc`, `inventory.company_stale_heartbeat_notification` 등. 식별자는 영문·숫자·밑줄만 |
| `Database:ApplySchemaOnStartup` | | 기본 `false`. `true`면 API 기동 시 idempotent DDL을 적용하고, 실패하면 기동하지 않음 |
| `Updates:Worker:PackageUrl` | 예 | 절대 URI. Watchdog 다운로드는 **HTTPS**만 허용 |
| `Updates:Worker:Sha256` | | 64자 hex. 첫 패키지 전까지 플레이스홀더라도 API는 기동함 |
| `Updates:Worker:Version` | | Worker `.version`과 비교 |
| `Updates:Worker:RequireAuthenticode` | | 운영은 `true` |
| `Kestrel:Endpoints` | AOT만 | Kestrel 단독 호스트의 HTTPS 바인딩. **IIS에서는 넣지 않습니다.** 인증서와 포트는 IIS 사이트 바인딩으로 엽니다. |

환경 변수 예: `Security__AgentToken`, `Security__AdminToken`, `Storage__SqlServer__ConnectionString`, `Database__ApplySchemaOnStartup`.

```powershell
$agentToken = .\New-ApiToken.ps1
$adminToken = .\New-ApiToken.ps1
```

`$agentToken`은 서버 `Security:AgentToken`과 PC `ApiToken`에, `$adminToken`은 서버 `Security:AdminToken`에 넣습니다.

### Kestrel (Native AOT) Windows Service

`Install-ApiServer.ps1`이 `api/win-x64`를 `C:\Program Files\SwLicenseWatcher\Api`에 복사하고, `SwLicenseWatcher.Api` 서비스를 자동 시작·실패 시 재시작으로 등록합니다. API는 `AddWindowsService`로 SCM에 상태를 보고하고, 서비스로 실행 중이면 Application 이벤트 로그(원본 `SwLicenseWatcher.Api`)에 기록합니다. 콘텐츠 루트는 실행 파일 폴더이므로 `appsettings.json`도 그 위치에서 읽습니다. Native AOT(`CreateSlimBuilder`)에서도 `UseKestrelHttpsConfiguration`이 켜져 있어 회사 템플릿의 `Kestrel:Endpoints:Https`가 적용됩니다. 회사 템플릿 [appsettings.api.company.json](../deploy/examples/appsettings.api.company.json)에 연결 문자열과 토큰을 넣습니다. 대상 서버에 .NET 런타임은 필요 없습니다.

관리자 PowerShell에서:

```powershell
.\Install-ApiServer.ps1 `
  -SourcePath D:\SwLicenseWatcher-1.0.1 `
  -ConnectionString $env:Storage__SqlServer__ConnectionString `
  -AgentToken $agentToken `
  -AdminToken $adminToken `
  -ListenUrl "https://0.0.0.0:443" `
  -FirewallPort 443
```

같은 스크립트를 다시 실행하면 서비스를 멈춘 뒤 파일을 교체하고 다시 시작합니다. `-SourcePath`는 압축을 푼 Release 폴더이거나 `api\win-x64` 자체입니다. IIS 산출물(`api\iis\win-x64`)은 거부합니다.

확인:

```powershell
Get-Service SwLicenseWatcher.Api
Invoke-RestMethod -Uri "https://license-watcher.contoso.local/health"
```

브라우저에서 `https://license-watcher.contoso.local/admin` 에 접속합니다. 데이터 조회에는 `AdminToken`이 필요합니다.

`-ListenUrl`을 생략하면 템플릿의 `Kestrel:Endpoints:Https:Url`을 유지합니다. `-FirewallPort`를 주면 인바운드 TCP 허용 규칙(`SW License Watcher API`)을 만듭니다. `-ApplySchemaOnStartup`을 주면 기동 시 스키마를 적용합니다.

HTTPS 인증서는 스크립트가 설치하지 않습니다. 서버 인증서를 `LocalMachine\My`에 넣고, `appsettings.json`의 `Kestrel:Endpoints:Https:Certificate:Subject`(예: `CN=license-watcher.contoso.local`)와 맞춥니다. LocalSystem이 개인 키를 읽을 수 있어야 합니다. 진단용으로 콘솔에서 직접 실행할 때만:

```powershell
& "C:\Program Files\SwLicenseWatcher\Api\SwLicenseWatcher.Api.exe"
```

제거:

```powershell
.\Uninstall-ApiServer.ps1                 # 서비스만 삭제, 파일·방화벽 규칙 유지
.\Uninstall-ApiServer.ps1 -RemoveFiles -RemoveFirewall
```

### IIS in-process

Native AOT는 IIS in-process를 지원하지 않습니다. `api/iis/win-x64`를 사용합니다.

1. 서버에 [.NET 10 ASP.NET Core Hosting Bundle](https://dotnet.microsoft.com/download/dotnet/10.0)을 설치합니다. (AspNetCoreModuleV2 + 공유 런타임)
2. IIS에서 애플리케이션 풀을 만듭니다. **.NET CLR 버전: No Managed Code**, 32비트 사용 안 함.
3. 사이트를 만들고 실제 경로를 `api/iis/win-x64` 폴더로 지정합니다. HTTPS 바인딩과 인증서는 IIS에서 설정합니다.
4. [appsettings.api.iis.company.json](../deploy/examples/appsettings.api.iis.company.json)을 해당 폴더의 `appsettings.json`으로 복사하고 토큰·연결 문자열을 넣습니다. `Kestrel:Endpoints`는 넣지 않습니다.
5. 게시 산출물의 `web.config`는 `hostingModel="InProcess"`, `processPath="dotnet"`, `arguments=".\SwLicenseWatcher.Api.dll"`입니다. stdout 로그를 쓰려면 `stdoutLogEnabled="true"`로 바꾸고 `logs` 폴더를 만듭니다.
6. 풀을 재순환한 뒤 사이트 URL로 `/health`를 확인하고, 브라우저에서 `/admin` 대시보드가 열리는지 봅니다.

IIS가 TLS를 종료하므로 에이전트의 `ServerBaseUrl`은 사이트 HTTPS 주소입니다.

스키마는 다음 중 한 가지로 적용합니다. `/health`는 Bearer가 필요 없습니다.

1. API를 기동한 뒤 [Apply-DbSchema.ps1](../deploy/scripts/Apply-DbSchema.ps1)이 `GET /api/schema/sql`을 받아 DB에 실행합니다. 스키마가 없을 때 `/health`는 503일 수 있습니다.
2. `Database:ApplySchemaOnStartup`을 `true`로 두면 API가 기동 시 idempotent DDL을 적용합니다. 실패하면 기동하지 않습니다.
3. 이미 저장해 둔 `.sql`이 있으면 `-SqlPath`로 적용합니다. `-WhatIf`로 배치를 검토할 수 있습니다.

```powershell
$headers = @{ Authorization = "Bearer $adminToken" }
.\Apply-DbSchema.ps1 `
  -ConnectionString $env:Storage__SqlServer__ConnectionString `
  -ApiBaseUrl "https://license-watcher.contoso.local" `
  -ApiToken $adminToken `
  -WhatIf
.\Apply-DbSchema.ps1 `
  -ConnectionString $env:Storage__SqlServer__ConnectionString `
  -ApiBaseUrl "https://license-watcher.contoso.local" `
  -ApiToken $adminToken
Invoke-RestMethod -Uri "https://license-watcher.contoso.local/health"
```

로컬 파일로 적용:

```powershell
Invoke-RestMethod -Uri "https://license-watcher.contoso.local/api/schema/sql" -Headers $headers |
  Set-Content -Encoding utf8 .\schema.sql
.\Apply-DbSchema.ps1 -Server sql.contoso.local -Database SwLicenseWatcher -SqlPath .\schema.sql
```

## 4. 클라이언트 appsettings.json

`Install-Agent.ps1`이 회사 템플릿을 복사한 뒤 파라미터로 덮어씁니다. 설치 후 PC의 JSON을 직접 고쳐도 됩니다. 환경 변수(`Section__Key`)와 커맨드라인(`--Section:Key=`)이 JSON보다 우선합니다. 서버 키는 PC JSON에 넣지 않습니다.

### Worker (`Agent`, `LocalState`)

| 키 | 필수 | 회사 기본 / 스크립트 동작 |
| --- | --- | --- |
| `Agent:DeviceCode` | 예 | 미지정 시 컴퓨터 이름. 전 PC가 같으면 서버에서 한 자산으로 UPSERT됩니다. |
| `Agent:DomainName` | 예 | `USERDOMAIN`, 없으면 `WORKGROUP` |
| `Agent:ServerBaseUrl` | 예 | 서버 API 주소, 예: `https://license-watcher.contoso.local` |
| `Agent:ApiToken` | 예 | 서버 `Security:AgentToken`(또는 레거시 `Security:Token`)과 **동일**, 32자 이상 |
| `Agent:SnapshotPath` | | `/api/inventory/snapshots` |
| `Agent:HeartbeatPath` | | `/api/agents/heartbeats` |
| `Agent:PollInterval` / `MaxJitter` | | `00:30:00` / `00:15:00` |
| `Agent:HealthFilePath` | 예 | `C:\ProgramData\SwLicenseWatcher\state\worker-health.json` |
| `LocalState:QueueDirectory` | 예 | `C:\ProgramData\SwLicenseWatcher\state\queue` |
| `LocalState:DpapiScope` | | `LocalMachine` (LocalSystem 서비스) |
| `LocalState:MaxQueuedSnapshots` / `MaxQueueBytes` | | 48 / 64MiB |

환경 변수 예: `Agent__ApiToken`, `Agent__ServerBaseUrl`.

시작 시 실패하는 경우: 빈 `ApiToken`, 비-loopback HTTP `ServerBaseUrl`, 빈 `DeviceCode`.

### Watchdog (`Watchdog`)

Worker와 **같은** `DeviceCode`, `ServerBaseUrl`, `ApiToken`을 넣습니다.

| 키 | 필수 | 회사 기본 / 스크립트 동작 |
| --- | --- | --- |
| `Watchdog:ManifestPath` | | `/api/updates/worker/manifest` |
| `Watchdog:WorkerServiceName` | | `SwLicenseWatcher.Agent.Worker` |
| `Watchdog:WorkerInstallDirectory` | 예 | Worker 설치 경로와 일치해야 함 |
| `Watchdog:WorkerHealthFilePath` | 예 | Worker `HealthFilePath`와 동일 |
| `Watchdog:StagingDirectory` / `BackupDirectory` | 예 | `C:\ProgramData\SwLicenseWatcher\staging` / `backup` |
| `Watchdog:CheckInterval` / `MaxJitter` | | `04:00:00` / `01:00:00` |

환경 변수 예: `Watchdog__ApiToken`, `Watchdog__WorkerInstallDirectory`.

템플릿: [appsettings.worker.company.json](../deploy/examples/appsettings.worker.company.json), [appsettings.watchdog.company.json](../deploy/examples/appsettings.watchdog.company.json).

## 5. PC에 에이전트 설치

서버 API가 이미 떠 있고, 토큰과 HTTPS URL을 알고 있어야 합니다.

Worker를 먼저 기동하고 Watchdog을 올립니다. 재실행하면 서비스를 멈춘 뒤 에이전트 파일만 갱신합니다. `api` 폴더는 복사하지 않습니다.

```powershell
.\Install-Agent.ps1 `
  -SourcePath D:\SwLicenseWatcher-1.0.1 `
  -ServerBaseUrl "https://license-watcher.contoso.local" `
  -ApiToken $token
```

`-DeviceCode`와 `-DomainName`을 생략하면 컴퓨터 이름과 `USERDOMAIN`을 씁니다. 자산 코드 체계가 있으면 `-DeviceCode`를 명시하세요.

확인:

```powershell
Get-Service SwLicenseWatcher.Agent.Worker, SwLicenseWatcher.Agent.Watchdog
Get-Content C:\ProgramData\SwLicenseWatcher\state\worker-health.json
Get-WinEvent -FilterHashtable @{ LogName = "Application"; StartTime = (Get-Date).AddMinutes(-15) } -MaxEvents 30
```

### Intune / SCCM 무인 설치

MSI는 제공하지 않습니다. Win32 앱 설치 명령으로 스크립트를 호출합니다.

```powershell
powershell.exe -ExecutionPolicy Bypass -File Install-Agent.ps1 -SourcePath D:\SwLicenseWatcher-1.0.1 -ServerBaseUrl https://license-watcher.contoso.local -ApiToken <token>
```

제거:

```powershell
powershell.exe -ExecutionPolicy Bypass -File Uninstall-Agent.ps1
```

큐와 백업까지 지울 때만 `-RemoveState`를 붙입니다.

## 6. Worker 자체 패치 (클라이언트 동작)

Watchdog이 서버 `GET /api/updates/worker/manifest`를 읽고 Worker만 교체합니다. 패키지 URL은 HTTPS여야 합니다. Release의 `SwLicenseWatcher.Agent.Worker-{version}.zip`은 ZIP 루트에 Worker 산출물(exe, `.version`, dll)이 있고, 실행 파일은 하나여야 합니다.

운영 절차:

1. GitHub Release의 `SHA256SUMS.txt` 또는 릴리스 노트의 `Updates:Worker` 스니펫에서 `Sha256`을 가져옵니다.
2. Worker ZIP을 회사 HTTPS 서버에 올립니다. PC가 GitHub에 닿으면 릴리스 자산 URL(`https://github.com/<owner>/<repo>/releases/download/{version}/SwLicenseWatcher.Agent.Worker-{version}.zip`)을 그대로 쓸 수 있습니다.
3. 서버 API `appsettings.json`의 `Updates:Worker`에 `Version`, `PackageUrl`, `Sha256`을 넣습니다. Watchdog은 `CheckInterval`(+ Jitter) 후에 따라갑니다.

Watchdog은 설치 디렉터리를 패키지 내용으로 교체하기 전에 PC의 `appsettings.json`과 `appsettings.*.json`을 보존하고, 복사 후 다시 덮어씁니다. 패키지에 들어 있는 개발용 `appsettings.json`은 이미 설치된 PC에서는 쓰이지 않습니다.

서명이 없는 릴리스(`SIGNING_CERTIFICATE_PFX_BASE64` 없음)는 ZIP 안의 EXE/DLL을 회사 인증서로 서명한 뒤 SHA-256을 다시 계산하거나, `Updates:Worker:RequireAuthenticode`를 `false`로 둡니다. 후자는 권장하지 않습니다.

```powershell
(Get-FileHash -Algorithm SHA256 D:\packages\SwLicenseWatcher.Agent.Worker-1.0.2.zip).Hash
```

실패 시 Watchdog은 백업에서 Worker를 롤백합니다. API 프로세스 자체는 PC에 없습니다.

## 7. 문제 해결

| 증상 | 확인할 것 |
| --- | --- |
| 서비스가 바로 종료 | Event Log. 빈 토큰, 비-loopback HTTP `ServerBaseUrl` |
| `401` | PC `ApiToken`과 서버 `Security:AgentToken`(또는 레거시 `Security:Token`)이 다름 |
| 스냅샷이 한 PC로만 보임 | 모든 에이전트가 같은 `DeviceCode`(예: 개발용 `pc-demo-001`) |
| Watchdog이 업데이트를 안 함 | 서버 manifest `Version`이 이미 `.version`과 같음, `PackageUrl`이 HTTP, SHA-256 불일치 |
| Authenticode 실패 | ZIP 안의 EXE/DLL이 회사 신뢰 루트로 서명되지 않음 |
| 업데이트 후 롤백 | Worker가 `HealthFilePath`에 버전을 못 씀. 경로가 Worker/Watchdog JSON에서 같은지 |
| IIS `500.30` / `500.31` | Hosting Bundle(.NET 10) 설치, 앱 풀 No Managed Code, `api/iis`를 쓰는지(AOT 폴더 아님) |
| IIS에서 Kestrel 포트 충돌 | IIS용 JSON에 `Kestrel:Endpoints`가 있으면 제거. HTTPS는 사이트 바인딩만 사용 |
| API 서비스가 바로 종료 | Event Log. 빈/짧은 토큰, AgentToken=AdminToken, 연결 문자열, `Kestrel` 인증서 Subject가 LocalMachine\My와 다른지 |
| 서비스 시작 1053 오류 | Application 이벤트 로그(원본 `SwLicenseWatcher.Api`). `appsettings.json`이 실행 파일과 같은 폴더(`C:\Program Files\SwLicenseWatcher\Api`)에 있는지, 토큰·연결 문자열·Kestrel HTTPS 인증서를 확인 |
| `Install-ApiServer.ps1`이 IIS 폴더를 거부 | `api\win-x64`(Native AOT)를 넘기세요. IIS는 위 IIS in-process 절을 따릅니다 |

```powershell
.\Uninstall-ApiServer.ps1                         # API 서비스만
.\Uninstall-ApiServer.ps1 -RemoveFiles -RemoveFirewall
.\Uninstall-Agent.ps1                             # 서비스와 Program Files 에이전트만
.\Uninstall-Agent.ps1 -RemoveState                # 큐·헬스·staging·backup까지
```

## 설치 경로

| 항목 | 기본 경로 |
| --- | --- |
| API (Kestrel Windows Service) | `C:\Program Files\SwLicenseWatcher\Api` |
| API 서비스 이름 | `SwLicenseWatcher.Api` |
| Worker | `C:\Program Files\SwLicenseWatcher\Agent.Worker` |
| Watchdog | `C:\Program Files\SwLicenseWatcher\Agent.Watchdog` |
| 헬스 파일 | `C:\ProgramData\SwLicenseWatcher\state\worker-health.json` |
| 스냅샷 큐 | `C:\ProgramData\SwLicenseWatcher\state\queue` |
| 업데이트 staging / backup | `C:\ProgramData\SwLicenseWatcher\staging`, `backup` |
