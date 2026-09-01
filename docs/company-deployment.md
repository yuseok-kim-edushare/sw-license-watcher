# 회사 배포 안내

회사 PC에는 Worker와 Watchdog만 설치합니다. **API는 서버에서 호스팅하는 웹 앱이며, 클라이언트에 복사하지 않습니다.** 개발용 `dotnet run`은 [README.md](../README.md)를 참고하세요.

에이전트는 PC의 `appsettings.json`에 적은 서버 URL과 공유 토큰으로 API에 접속합니다.

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
    New-ApiToken.ps1      공유 토큰 생성 (서버와 에이전트에 같은 값)
    Install-Agent.ps1     PC에 Worker + Watchdog만 설치
    Uninstall-Agent.ps1
```

Release ZIP의 `api/win-x64`(AOT)와 `api/iis/win-x64`(IIS)는 서버용입니다. `Install-Agent.ps1`은 `agent-worker`와 `agent-watchdog`만 복사합니다.

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
- 서버 `Security:Token`과 같은 32자 이상 공유 토큰
- 자체 패치를 쓸 때: 서버가 가리키는 Worker ZIP에 `SwLicenseWatcher.Agent.Worker.exe`가 정확히 하나, EXE/DLL이 신뢰된 Authenticode 서명

토큰은 저장소에 커밋하지 마세요.

## 3. 서버 API

API는 서버에서만 호스팅합니다. Windows Service가 아니고, PC 에이전트 설치 대상도 아닙니다. Release ZIP에는 두 가지 서버 산출물이 있습니다.

| 산출물 | 언제 | 설정 예시 |
| --- | --- | --- |
| `api/win-x64/` Native AOT | Kestrel로 직접 실행 | [appsettings.api.company.json](../deploy/examples/appsettings.api.company.json) |
| `api/iis/win-x64/` framework-dependent | IIS in-process (`web.config`) | [appsettings.api.iis.company.json](../deploy/examples/appsettings.api.iis.company.json) |

공통 키:

| 키 | 필수 | 설명 |
| --- | --- | --- |
| `Security:Token` | 예 | 32자 이상. 에이전트 `ApiToken`과 동일 |
| `Security:RequireHttps` | | 운영은 `true`. 원격 HTTP는 거부, loopback HTTP는 허용 |
| `Storage:SqlServer:ConnectionString` | 예 | `TrustServerCertificate=False` 권장 |
| `Storage:SqlServer:SchemaName` 및 테이블/컬럼 | 예 | 기본 예시는 `inventory.company_pc` 등. 식별자는 영문·숫자·밑줄만 |
| `Updates:Worker:PackageUrl` | 예 | 절대 URI. Watchdog 다운로드는 **HTTPS**만 허용 |
| `Updates:Worker:Sha256` | | 64자 hex. 첫 패키지 전까지 플레이스홀더라도 API는 기동함 |
| `Updates:Worker:Version` | | Worker `.version`과 비교 |
| `Updates:Worker:RequireAuthenticode` | | 운영은 `true` |
| `Kestrel:Endpoints` | AOT만 | Kestrel 단독 호스트의 HTTPS 바인딩. **IIS에서는 넣지 않습니다.** 인증서와 포트는 IIS 사이트 바인딩으로 엽니다. |

환경 변수 예: `Security__Token`, `Storage__SqlServer__ConnectionString`.

```powershell
$token = .\New-ApiToken.ps1
```

이 값을 서버 `Security:Token`과 PC `ApiToken`에 같이 넣습니다.

### Kestrel (Native AOT)

`api/win-x64`를 서버에 두고 [appsettings.api.company.json](../deploy/examples/appsettings.api.company.json)을 `appsettings.json`으로 복사한 뒤 `REPLACE_ME_*`를 채웁니다.

```powershell
& .\SwLicenseWatcher.Api.exe
```

대상 서버에 .NET 런타임은 필요 없습니다.

### IIS in-process

Native AOT는 IIS in-process를 지원하지 않습니다. `api/iis/win-x64`를 사용합니다.

1. 서버에 [.NET 10 ASP.NET Core Hosting Bundle](https://dotnet.microsoft.com/download/dotnet/10.0)을 설치합니다. (AspNetCoreModuleV2 + 공유 런타임)
2. IIS에서 애플리케이션 풀을 만듭니다. **.NET CLR 버전: No Managed Code**, 32비트 사용 안 함.
3. 사이트를 만들고 실제 경로를 `api/iis/win-x64` 폴더로 지정합니다. HTTPS 바인딩과 인증서는 IIS에서 설정합니다.
4. [appsettings.api.iis.company.json](../deploy/examples/appsettings.api.iis.company.json)을 해당 폴더의 `appsettings.json`으로 복사하고 토큰·연결 문자열을 넣습니다. `Kestrel:Endpoints`는 넣지 않습니다.
5. 게시 산출물의 `web.config`는 `hostingModel="InProcess"`, `processPath="dotnet"`, `arguments=".\SwLicenseWatcher.Api.dll"`입니다. stdout 로그를 쓰려면 `stdoutLogEnabled="true"`로 바꾸고 `logs` 폴더를 만듭니다.
6. 풀을 재순환한 뒤 사이트 URL로 `/health`를 확인합니다.

IIS가 TLS를 종료하므로 에이전트의 `ServerBaseUrl`은 사이트 HTTPS 주소입니다.

최초 기동 후 인증된 `GET /api/schema/sql`을 검토해 DB에 적용합니다. `/health`는 Bearer가 필요 없습니다.

```powershell
$headers = @{ Authorization = "Bearer $token" }
Invoke-RestMethod -Uri "https://license-watcher.contoso.local/api/schema/sql" -Headers $headers |
  Set-Content -Encoding utf8 .\schema.sql
Invoke-RestMethod -Uri "https://license-watcher.contoso.local/health"
```

## 4. 클라이언트 appsettings.json

`Install-Agent.ps1`이 회사 템플릿을 복사한 뒤 파라미터로 덮어씁니다. 설치 후 PC의 JSON을 직접 고쳐도 됩니다. 환경 변수(`Section__Key`)와 커맨드라인(`--Section:Key=`)이 JSON보다 우선합니다. 서버 키는 PC JSON에 넣지 않습니다.

### Worker (`Agent`, `LocalState`)

| 키 | 필수 | 회사 기본 / 스크립트 동작 |
| --- | --- | --- |
| `Agent:DeviceCode` | 예 | 미지정 시 컴퓨터 이름. 전 PC가 같으면 서버에서 한 자산으로 UPSERT됩니다. |
| `Agent:DomainName` | 예 | `USERDOMAIN`, 없으면 `WORKGROUP` |
| `Agent:ServerBaseUrl` | 예 | 서버 API 주소, 예: `https://license-watcher.contoso.local` |
| `Agent:ApiToken` | 예 | 서버 `Security:Token`과 **동일**, 32자 이상 |
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

Watchdog이 서버 `GET /api/updates/worker/manifest`를 읽고 Worker만 교체합니다. 패키지 URL은 HTTPS여야 합니다.

운영자가 서버 API `appsettings.json`의 `Updates:Worker`(`Version`, `PackageUrl`, `Sha256`)를 갱신하면, PC의 Watchdog이 `CheckInterval`(+ Jitter) 후에 따라갑니다. ZIP 안에 `SwLicenseWatcher.Agent.Worker.exe`가 하나여야 합니다.

```powershell
(Get-FileHash -Algorithm SHA256 D:\packages\worker-1.0.2.zip).Hash
```

실패 시 Watchdog은 백업에서 Worker를 롤백합니다. API 프로세스 자체는 PC에 없습니다.

## 7. 문제 해결

| 증상 | 확인할 것 |
| --- | --- |
| 서비스가 바로 종료 | Event Log. 빈 토큰, 비-loopback HTTP `ServerBaseUrl` |
| `401` | PC `ApiToken`과 서버 `Security:Token`이 다름 |
| 스냅샷이 한 PC로만 보임 | 모든 에이전트가 같은 `DeviceCode`(예: 개발용 `pc-demo-001`) |
| Watchdog이 업데이트를 안 함 | 서버 manifest `Version`이 이미 `.version`과 같음, `PackageUrl`이 HTTP, SHA-256 불일치 |
| Authenticode 실패 | ZIP 안의 EXE/DLL이 회사 신뢰 루트로 서명되지 않음 |
| 업데이트 후 롤백 | Worker가 `HealthFilePath`에 버전을 못 씀. 경로가 Worker/Watchdog JSON에서 같은지 |
| IIS `500.30` / `500.31` | Hosting Bundle(.NET 10) 설치, 앱 풀 No Managed Code, `api/iis`를 쓰는지(AOT 폴더 아님) |
| IIS에서 Kestrel 포트 충돌 | IIS용 JSON에 `Kestrel:Endpoints`가 있으면 제거. HTTPS는 사이트 바인딩만 사용 |

```powershell
.\Uninstall-Agent.ps1                 # 서비스와 Program Files 에이전트만
.\Uninstall-Agent.ps1 -RemoveState    # 큐·헬스·staging·backup까지
```

## PC 설치 경로

| 항목 | 기본 경로 |
| --- | --- |
| Worker | `C:\Program Files\SwLicenseWatcher\Agent.Worker` |
| Watchdog | `C:\Program Files\SwLicenseWatcher\Agent.Watchdog` |
| 헬스 파일 | `C:\ProgramData\SwLicenseWatcher\state\worker-health.json` |
| 스냅샷 큐 | `C:\ProgramData\SwLicenseWatcher\state\queue` |
| 업데이트 staging / backup | `C:\ProgramData\SwLicenseWatcher\staging`, `backup` |
