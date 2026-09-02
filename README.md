# sw-license-watcher

.NET 10 기반의 기업용 소프트웨어 라이선스 감시 서비스입니다. 로컬 PC 에이전트는 **Watchdog + Worker**의 투 프로세스 Windows Service 구조를 사용하고, 서버는 **ASP.NET Core API**와 SQL Server로 소프트웨어 수집과 업데이트 메타데이터를 제공합니다.

## 구현된 핵심 요구사항

- **Windows Service 2개**
  - `SwLicenseWatcher.Agent.Watchdog`: 자체 패치, SHA-256/Authenticode 검증, 백업/롤백 정책 담당
  - `SwLicenseWatcher.Agent.Worker`: 설치 소프트웨어 수집, heartbeat/snapshot 전송 담당
- **로컬 상태 저장 설계**
  - 전송 실패 스냅샷을 원자적 파일 큐에 저장하고 다음 주기에 재전송
  - 큐가 모두 전송되기 전에는 새 스냅샷을 전송하지 않고 큐에 적재하여 오래된 전체 스냅샷이 최신 스냅샷을 덮어쓰지 않도록 방지
  - 큐 페이로드는 DPAPI(`LocalMachine` 기본값)로 보호
  - `LocalState:MaxQueuedSnapshots`, `LocalState:MaxQueueBytes` 할당량 초과 시 가장 오래된 스냅샷부터 제거
- **서버 측 API**
  - 수집 API: `/api/inventory/snapshots`, `/api/agents/heartbeats`
  - 조회 API: `/api/inventory/devices`, `/api/inventory/software` (JSON 및 `?format=csv`, `?classification=` 필터)
  - SQL Server 트랜잭션 기반 PC UPSERT 및 설치 소프트웨어 교체 저장(정책 매칭 분류 포함)
  - Bearer 토큰 인증(에이전트/관리자 역할 분리), 원격 요청 HTTPS 강제, 수집 POST 본문 크기 제한(스냅샷 8 MiB, 하트비트 64 KiB)
  - 헬스체크: `/health`(인증 제외)는 SQL Server에 `SELECT 1`로 연결을 확인하고, 실패 시 503과 일반화된 사유만 반환
  - 설계/스키마 API: `/api/design`, `/api/schema/sql`
  - 업데이트 manifest API: `/api/updates/worker/manifest`
  - 소프트웨어 정책 CRUD: `/api/policies` (목록은 페이징·검색·분류 필터·CSV)
  - 블랙리스트 위반 목록: `/api/violations` (페이징·검색·기간 필터·CSV)
  - 스냅샷 수신 시 설치 SW를 정책과 매칭해 white/managed/black/unclassified로 분류하고, 분류 결과를 설치 SW 테이블에 저장하며, 블랙리스트 적발을 `company_sw_violation`에 기록
- **서버 알림 (웹훅 + SMTP)**
  - Teams/Slack incoming webhook(`{ "text": "..." }`)과 SMTP로 운영 알림 전송
  - 신규 스냅샷에서 이전에 없던 소프트웨어가 보이면 PC·이름·버전 요약 알림
  - 스냅샷에서 해당 PC에 없던 블랙리스트 위반이 새로 적발되면 PC·소프트웨어·정책 패턴 알림(기존 위반은 재알림하지 않음)
  - heartbeat가 설정된 시간(기본 24시간) 이상 두절된 PC는 PC당 1회만 알림(복구 후 다시 두절되면 재알림)
  - 알림 전송 실패는 로그만 남기고 스냅샷/heartbeat 저장에는 영향을 주지 않음
- **SQL Server 스키마 커스터마이징**
  - PC entity / PC installed software / software policy(white|managed|black) / software violation 테이블 이름과 컬럼 이름 모두 `appsettings.json`에서 변경 가능
- **수집 방식 안전장치**
  - `Win32_Product` / WMI 미사용
  - `HKLM(64)`, `HKLM(32)`, `HKCU`, 로드된 `HKEY_USERS` 사용자 SID의 `Uninstall` 키 순회
  - 접근이 거부된 레지스트리 키는 개별 건너뛰기
- **자체 패치 안정성**
  - 랜덤 Jitter 기반 업데이트 주기
  - HTTPS 패키지 다운로드, SHA-256 및 WinVerifyTrust Authenticode 검증
  - ZIP 경로 이탈 방지, 서비스 중지/교체/시작, 헬스체크 실패 시 자동 롤백
  - 헬스체크는 Worker가 직접 기록하는 로컬 health 신호 파일(설치 버전 + 기록 시각)을 롤백 제한 시간 내에서 확인
- **Native AOT 컴파일**
  - Worker/Watchdog와 API 단독 호스트는 `PublishAot=true`로 네이티브 바이너리 publish
  - API는 IIS in-process용으로 `PublishAot=false` 산출물도 함께 배포합니다
  - Source-generated 구성 바인딩(`EnableConfigurationBindingGenerator`)과 System.Text.Json source generator를 사용해 리플렉션 없이 동작

## 프로젝트 구조

- `/src/SwLicenseWatcher.Core`: 계약, 옵션, 레지스트리 수집기, DPAPI 보호기, SQL Server DDL 생성기
- `/src/SwLicenseWatcher.Agent.Worker`: inventory 수집 Windows Service
- `/src/SwLicenseWatcher.Agent.Watchdog`: self-update Windows Service
- `/src/SwLicenseWatcher.Api`: ASP.NET Core API
- `/deploy/examples`: 서버 API·PC 에이전트용 `appsettings.json` 템플릿
- `/deploy/scripts`: 서버 API(Kestrel Windows Service) 및 PC Worker·Watchdog 설치/제거 스크립트, SQL Server 스키마 적용 스크립트
- `/docs/company-deployment.md`: 서버 설정 예시와 회사 PC 클라이언트 배포 절차
- `.github/workflows/ci.yaml`: CI (빌드/테스트, Native AOT publish 검증, Dependabot auto-merge 트리거)
- `.github/workflows/auto-merge.yaml`: Dependabot PR 자동 머지
- `.github/workflows/cd.yaml`: CD (CI 성공 후 publish 산출물 ZIP GitHub Release)

## CI/CD

- **CI (`ci.yaml`)**: `main` 대상 push/PR에서 `windows-latest`로 솔루션 Restore/Build/Test를 검증합니다. 이와 병행해 win-x64 Native AOT publish를 수행합니다. IL trim/AOT 경고는 자체 코드(SwLicenseWatcher.*)에서 발생하면 실패하고, `Microsoft.Data.SqlClient` 등 서드파티 어셈블리 경고는 요약만 보고합니다. 산출된 native 실행 파일을 smoke-run한 뒤 `native-aot-win-x64` 아티팩트를 업로드합니다. Dependabot PR이 두 job을 모두 통과하면 auto-merge 워크플로우를 트리거합니다.
- **CD (`cd.yaml`)**: `main`에서 CI가 성공하면 `Agent.Watchdog`, `Agent.Worker`를 win-x64 **Native AOT**로, API는 **Native AOT(Kestrel)** 와 **IIS in-process** 두 가지로 publish하고 `SwLicenseWatcher-{version}.zip`으로 GitHub Release를 생성합니다. 버전은 최신 태그의 patch 자동 증가이며, 커밋 메시지에 `Update Version To x.y.z`를 포함해 재정의할 수 있습니다.

Release ZIP 구조:

```
SwLicenseWatcher-{version}/
  agent-watchdog/win-x64/   self-update Windows Service
  agent-worker/win-x64/     inventory 수집 Windows Service
  api/win-x64/              API Native AOT (Kestrel 단독)
  api/iis/win-x64/          API IIS in-process (web.config 포함)
```

## 커스터마이징 포인트

서버 테이블/컬럼 매핑은 `/src/SwLicenseWatcher.Api/appsettings.json`의 `Storage:SqlServer`에서 바꿀 수 있습니다.

예:

- `PcTable.TableName`
- `PcTable.DeviceCodeColumn`
- `InstalledSoftwareTable.TableName`
- `InstalledSoftwareTable.DisplayNameColumn`
- `InstalledSoftwareTable.ClassificationColumn`
- `SoftwarePolicyTable.TableName`
- `SoftwarePolicyTable.ClassificationColumn`
- `SoftwareViolationTable.TableName`
- `StaleHeartbeatNotificationTable.TableName`

현재 기본 예시는 다음처럼 커스텀되어 있습니다.

- PC 테이블: `company_pc`
- 설치 SW 테이블: `company_pc_installed_sw`
- 정책 테이블: `company_sw_policy`
- 위반 테이블: `company_sw_violation`
- 하트비트 두절 알림 상태 테이블: `company_stale_heartbeat_notification`

## 회사 배포

서버 API는 [deploy/examples/appsettings.api.company.json](deploy/examples/appsettings.api.company.json)(Kestrel) 또는 [deploy/examples/appsettings.api.iis.company.json](deploy/examples/appsettings.api.iis.company.json)(IIS)으로 맞추고, 회사 PC에는 Worker·Watchdog만 설치합니다. 절차는 [docs/company-deployment.md](docs/company-deployment.md)를 참고하세요.

Kestrel(Native AOT, `api/win-x64`)은 서버에서 [deploy/scripts/Install-ApiServer.ps1](deploy/scripts/Install-ApiServer.ps1)로 Windows Service(`SwLicenseWatcher.Api`)로 등록합니다. API는 `AddWindowsService`로 자신을 호스팅하므로 SCM에서 시작해도 콘텐츠 루트는 실행 파일 폴더이고, Application 이벤트 로그 원본은 `SwLicenseWatcher.Api`입니다. Native AOT slim builder에서도 `UseKestrelHttpsConfiguration`으로 `Kestrel:Endpoints:Https`가 적용됩니다. IIS in-process(`api/iis/win-x64`)는 Hosting Bundle과 사이트 바인딩을 쓰는 수동 절차입니다.

```powershell
$agentToken = .\deploy\scripts\New-ApiToken.ps1
$adminToken = .\deploy\scripts\New-ApiToken.ps1
.\deploy\scripts\Install-ApiServer.ps1 `
  -SourcePath D:\SwLicenseWatcher-1.0.1 `
  -ConnectionString $env:Storage__SqlServer__ConnectionString `
  -AgentToken $agentToken `
  -AdminToken $adminToken `
  -ListenUrl "https://0.0.0.0:443" `
  -FirewallPort 443
```

제거는 `.\deploy\scripts\Uninstall-ApiServer.ps1`입니다. 설치 디렉터리와 방화벽 규칙까지 지울 때만 `-RemoveFiles -RemoveFirewall`을 붙입니다.

## 클라이언트 원격 서버 설정

Worker/Watchdog 클라이언트가 접속할 서버 주소는 설정 파일로 지정합니다. Native AOT 빌드에서도 source-generated 구성 바인딩으로 동일하게 동작합니다.

- Worker: `src/SwLicenseWatcher.Agent.Worker/appsettings.json`의 `Agent:ServerBaseUrl`
- Watchdog: `src/SwLicenseWatcher.Agent.Watchdog/appsettings.json`의 `Watchdog:ServerBaseUrl`

원격 서버 예:

```json
{
  "Agent": {
    "ServerBaseUrl": "https://license-watcher.contoso.com"
  }
}
```

환경 변수(`Agent__ServerBaseUrl`, `Watchdog__ServerBaseUrl`) 또는 커맨드라인 인자(`--Agent:ServerBaseUrl=...`)로도 재정의할 수 있습니다. 원격 주소는 HTTPS여야 하며 HTTP는 loopback 진단에만 허용됩니다.

## 필수 보안 및 저장소 설정

토큰과 SQL Server 연결 문자열은 소스에 저장하지 말고 환경 변수 또는 비밀 저장소로 주입합니다. 설정한 토큰은 각각 32자 이상이어야 합니다. `New-ApiToken.ps1`을 두 번 실행해 에이전트용과 관리자용을 따로 만드세요.

권장: 역할을 분리합니다. `AgentToken`은 에이전트 수집·하트비트·업데이트 manifest만, `AdminToken`은 조회·정책 CRUD·위반·CSV·`/api/design`·`/api/schema/sql`을 포함한 모든 엔드포인트를 허용합니다. `AgentToken`과 `AdminToken`(또는 레거시 `Token`)이 같으면 API는 시작하지 않습니다.

하위 호환: `AgentToken`/`AdminToken`을 비워 두고 `Security:Token`만 두면 이전처럼 모든 엔드포인트에서 그 토큰이 동작합니다.

```text
Security__AgentToken=<agent-token>
Security__AdminToken=<admin-token>
Storage__SqlServer__ConnectionString=<sql-server-connection-string>
Agent__ApiToken=<agent-token>
Watchdog__ApiToken=<agent-token>
```

레거시 단일 토큰:

```text
Security__Token=<shared-token>
Agent__ApiToken=<shared-token>
Watchdog__ApiToken=<shared-token>
```

운영 SQL Server 연결에서는 서버 인증서를 검증하고 `TrustServerCertificate=False`를 유지하십시오.

수집 POST 본문은 경로별로 상한을 둡니다. 스냅샷(`/api/inventory/snapshots`)은 8 MiB, 하트비트(`/api/agents/heartbeats`)는 64 KiB입니다. Native AOT slim builder는 MVC `RequestSizeLimit`를 쓰지 않으므로, 인증 미들웨어가 `IHttpMaxRequestBodySizeFeature`로 적용합니다. 한도를 넘기면 413입니다.

스키마는 `GET /api/schema/sql`이 반환하는 idempotent DDL입니다. SSMS에서 손으로 실행하는 대신 [deploy/scripts/Apply-DbSchema.ps1](deploy/scripts/Apply-DbSchema.ps1)로 적용합니다. `-WhatIf`로 배치를 미리 볼 수 있습니다. API가 아직 기동 전이면 스크립트에 로컬 `.sql` 파일을 넘기거나, 기동 후 API에서 DDL을 받아 적용합니다.

```powershell
# 실행 중인 API에서 DDL을 받아 적용 (AdminToken 또는 레거시 Token)
.\deploy\scripts\Apply-DbSchema.ps1 `
  -ConnectionString $env:Storage__SqlServer__ConnectionString `
  -ApiBaseUrl http://127.0.0.1:5080 `
  -ApiToken $env:Security__AdminToken

# 로컬 파일로 적용
.\deploy\scripts\Apply-DbSchema.ps1 `
  -Server sql.contoso.local `
  -Database SwLicenseWatcher `
  -SqlPath .\schema.sql

# 미리보기 (DB에 쓰지 않음)
.\deploy\scripts\Apply-DbSchema.ps1 -ConnectionString $cs -SqlPath .\schema.sql -WhatIf
```

API가 스스로 적용하게 하려면 `Database:ApplySchemaOnStartup`을 `true`로 둡니다(기본 `false`). 켜면 기동 시 `SqlServerSchemaScriptBuilder` DDL을 연결 문자열의 DB에 적용한 뒤 요청을 받습니다. 실패하면 로그를 남기고 기동하지 않습니다. DDL이 이미 있으면 건너뛰므로 재기동해도 안전합니다. 컬럼 확장 같은 기존 DB 마이그레이션은 이 옵션이 대신하지 않습니다.

```text
Database__ApplySchemaOnStartup=true
```

기존 스키마를 사용 중이라면 `discovery_scope` 컬럼이 `NVARCHAR(256)`으로 확장되었으므로 다음과 같이 마이그레이션합니다(테이블/컬럼 이름은 설정값에 맞게 변경).

```sql
ALTER TABLE [inventory].[pc_installed_sw] ALTER COLUMN [discovery_scope] NVARCHAR(256) NOT NULL;
```

설치 소프트웨어 분류(`classification`: `white` | `managed` | `black` | `unclassified`)를 저장하려면 기존 DB에 컬럼을 추가합니다. 이미 있는 행은 다음 스냅샷이 들어올 때까지 `unclassified`로 둡니다.

```sql
ALTER TABLE [inventory].[pc_installed_sw] ADD [classification] NVARCHAR(32) NOT NULL CONSTRAINT [DF_pc_installed_sw_classification] DEFAULT (N'unclassified');
```

하트비트 두절 알림은 PC별로 한 행만 유지하는 `stale_heartbeat_notification` 테이블에 기록합니다. API를 재시작해도 같은 두절에 대해 알림을 다시 보내지 않고, 해당 PC가 하트비트를 보내 정상으로 돌아오면 행을 지워 이후 재두절 때 새 알림이 나갑니다. 기존 DB에는 `Database:ApplySchemaOnStartup` 또는 `Apply-DbSchema.ps1` / `GET /api/schema/sql`로 테이블을 만들면 됩니다(이미 있으면 `IF OBJECT_ID` 가드가 건너뜁니다). 기본 식별자는 다음과 같습니다.

```sql
IF OBJECT_ID(N'[inventory].[stale_heartbeat_notification]', N'U') IS NULL
BEGIN
    CREATE TABLE [inventory].[stale_heartbeat_notification] (
        [stale_heartbeat_notification_id] BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [pc_id] BIGINT NOT NULL,
        [notified_at_utc] DATETIMEOFFSET NOT NULL,
        CONSTRAINT [FK_stale_heartbeat_notification_pc_entity] FOREIGN KEY ([pc_id]) REFERENCES [inventory].[pc_entity]([pc_id]) ON DELETE CASCADE,
        CONSTRAINT [UX_stale_heartbeat_notification_pc_id] UNIQUE ([pc_id])
    );
END
```

회사 예시 매핑(`company_pc` / `company_stale_heartbeat_notification`)을 쓰는 기존 DB는 테이블·컬럼·FK 이름을 설정값에 맞게 바꿉니다.

Watchdog에는 Worker 실행 파일이 설치된 `Watchdog__WorkerInstallDirectory`와 업데이트 후 확인할 `Watchdog__WorkerHealthFilePath`도 설정합니다. 이 경로는 Worker의 `Agent__HealthFilePath`와 동일해야 하며, Worker 서비스 계정이 쓰고 Watchdog 서비스 계정이 읽을 수 있어야 합니다. 업데이트 ZIP에는 정확히 하나의 `SwLicenseWatcher.Agent.Worker.exe`가 있어야 하며 모든 EXE/DLL이 신뢰된 Authenticode 서명을 가져야 합니다.

## 서버 알림 (웹훅 / SMTP)

API 서버는 수집 결과를 바탕으로 Teams/Slack incoming webhook과 SMTP 메일로 알림을 보낼 수 있습니다. 두 채널은 독립적으로 켜고 끌 수 있으며, 둘 다 꺼 두면 알림을 보내지 않습니다. 자격 증명과 webhook URL은 소스에 넣지 말고 환경 변수로 주입하세요.

설정 예시는 [deploy/examples/appsettings.api.json](deploy/examples/appsettings.api.json)을 참고하세요.

```json
{
  "Notifications": {
    "Webhook": {
      "Enabled": true,
      "Url": "https://outlook.office.com/webhook/REPLACE_ME",
      "Timeout": "00:00:10"
    },
    "Smtp": {
      "Enabled": true,
      "Host": "smtp.contoso.local",
      "Port": 587,
      "EnableSsl": true,
      "UserName": "sw-license-watcher",
      "Password": "",
      "From": "sw-license-watcher@contoso.local",
      "Recipients": [ "it-ops@contoso.local" ]
    },
    "Events": {
      "NewSoftware": true,
      "StaleHeartbeat": true,
      "BlacklistViolation": true
    },
    "StaleHeartbeatThreshold": "1.00:00:00",
    "StaleHeartbeatCheckInterval": "00:15:00"
  }
}
```

| 키 | 설명 |
| --- | --- |
| `Notifications:Webhook:Enabled` | Teams/Slack incoming webhook 사용 여부. 페이로드는 `{ "text": "..." }` |
| `Notifications:Webhook:Url` | webhook이 켜져 있을 때 필수. HTTP 또는 HTTPS 절대 URI |
| `Notifications:Webhook:Timeout` | HTTP 타임아웃. 기본 `00:00:10` |
| `Notifications:Smtp:Enabled` | SMTP 메일 사용 여부 |
| `Notifications:Smtp:Host` / `Port` / `EnableSsl` | SMTP가 켜져 있을 때 호스트·포트·SSL |
| `Notifications:Smtp:UserName` / `Password` | 비워 두면 익명(또는 서버 기본 자격 증명) 릴레이 |
| `Notifications:Smtp:From` / `Recipients` | SMTP가 켜져 있을 때 발신자와 수신자 목록(1명 이상) |
| `Notifications:Events:NewSoftware` | 이전에 없던 소프트웨어가 스냅샷에 나타나면 알림 |
| `Notifications:Events:StaleHeartbeat` | heartbeat 두절 PC 알림. 발송 여부는 DB에 영속화되어 API 재시작 후에도 같은 두절을 재알림하지 않음 |
| `Notifications:Events:BlacklistViolation` | 해당 PC에 없던 블랙리스트 위반이 새로 적발되면 알림. 이미 기록된 위반은 재알림하지 않음 |
| `Notifications:StaleHeartbeatThreshold` | 두절로 볼 경과 시간. 기본 24시간(`1.00:00:00`) |
| `Notifications:StaleHeartbeatCheckInterval` | 백그라운드 검사 주기. 기본 15분 |

환경 변수 예:

```text
Notifications__Webhook__Url=<teams-or-slack-incoming-webhook>
Notifications__Smtp__Password=<smtp-password>
```

Webhook이 켜진 상태에서 URL이 없거나, SMTP가 켜진 상태에서 Host/From/Recipients가 비어 있으면 API는 시작 시 실패합니다. 알림은 백그라운드 큐로 보내며, SMTP는 Native AOT를 위해 `System.Net.Mail.SmtpClient`를 사용합니다(MailKit 전체 패키지는 AOT 비호환).

## 수동 실행 예시

```bash
dotnet build SwLicenseWatcher.slnx
dotnet run --project src/SwLicenseWatcher.Api
```

API 실행 후:

- `GET /api/design`: 전체 설계 요약
- `GET /api/schema/sql`: 현재 설정 기준 SQL Server DDL
- `GET /health`: SQL Server 연결 확인(인증 불필요). 실패 시 503
- `GET /api/inventory/devices`: 수집된 PC 목록 (페이징·검색·stale heartbeat 필터)
- `GET /api/inventory/software`: 소프트웨어별 설치 PC 수 집계 (`?classification=`으로 분류 필터)

조회 API는 `AdminToken`(또는 레거시 `Token`)이 필요합니다. 에이전트 `AgentToken`으로는 호출할 수 없습니다. `?format=csv`를 붙이면 UTF-8 BOM이 포함된 CSV를 내려받아 Excel에서 한글을 깨지지 않게 열 수 있습니다.

| 메서드 | 경로 | 설명 | 주요 쿼리 |
| --- | --- | --- | --- |
| GET | `/api/inventory/devices` | PC 목록 (자산코드, 호스트명, 도메인, OS, 에이전트 버전, 마지막 heartbeat/inventory 시각) | `skip`, `take`, `search`(호스트명 또는 자산코드), `staleAfterHours`, `format=csv` |
| GET | `/api/inventory/devices/{deviceCode}` | 단일 PC 상세와 설치 소프트웨어 전체(항목별 `classification`) | `classification`, `format=csv` |
| GET | `/api/inventory/software` | SW 이름/버전/분류별 설치 PC 수 | `skip`, `take`, `search`(이름), `classification`, `format=csv` |
| GET | `/api/inventory/software/{name}/devices` | 해당 SW가 설치된 PC 목록 | `skip`, `take`, `classification`, `format=csv` |

페이징 기본값은 JSON `take=100`, CSV `take=10000`이며 최대 10000입니다. `staleAfterHours`는 마지막 heartbeat가 없거나 지정 시간보다 오래된 PC만 남깁니다. `search`는 SQL `LIKE` 와일드카드가 이스케이프된 부분 일치입니다. `classification`은 `white` | `managed` | `black` | `unclassified`이며, 설치 SW 행에 저장된 분류로 필터링합니다(예: `?classification=unclassified`).

스냅샷 접수 `Location`은 `/api/inventory/devices/{deviceCode}`를 가리키며, 이전 경로인 `GET /api/inventory/snapshots/{deviceCode}`도 같은 상세 응답을 반환합니다.

## 소프트웨어 정책과 위반

정책은 `company_sw_policy`(이름은 설정으로 변경 가능)에 저장합니다. 스냅샷 `POST /api/inventory/snapshots`를 받을 때 설치 SW 이름·버전을 정책과 매칭해 `white` / `managed` / `black` / `unclassified`로 분류하고, 그 결과를 설치 SW 테이블 `classification` 컬럼에 저장합니다. 블랙리스트에 걸린 항목은 `company_sw_violation`에 기록하며, 같은 PC+소프트웨어 이름은 한 행만 유지합니다(최초 적발 시각은 유지하고 마지막 발견 시각만 갱신). 더 이상 설치되어 있지 않거나 블랙이 아니면 해당 PC의 위반 행을 삭제합니다. 스냅샷 저장 후 해당 PC에 없던 위반이 새로 생기면 `Notifications:Events:BlacklistViolation`이 켜져 있을 때 webhook/SMTP 알림을 보내며, 이미 적발된 위반은 다시 알리지 않습니다.

이름 패턴은 정확 일치이며, `*` / `?` 와일드카드로 prefix·부분 일치도 됩니다(`Google Chrome*`, `*Torrent*`). 선택적 `versionPattern`은 정확/와일드카드(`16.*`)이거나 비교식(`>=17.0`, `<18.0`)이고, 쉼표로 AND 조건을 연결할 수 있습니다(`>=17.0,<18.0`). 게시자(`publisher`)가 있으면 이름과 같은 방식으로 매칭합니다. 여러 정책이 동시에 맞으면 **black > managed > white** 순으로 더 강한 분류를 씁니다.

정책·위반 API도 `AdminToken`(또는 레거시 `Token`)이 필요합니다. 목록 JSON은 인벤토리 조회와 같이 `skip`, `take`, `totalCount`, `items`를 반환합니다. 쿼리 파라미터를 생략하면 JSON `take=100`, CSV `take=10000`(최대 10000)으로 동작합니다.

| 메서드 | 경로 | 설명 | 주요 쿼리 |
| --- | --- | --- | --- |
| GET | `/api/policies` | 정책 목록 | `skip`, `take`, `search`(정책 이름·버전 패턴·게시자), `classification`, `format=csv` |
| GET | `/api/policies/{id}` | 정책 단건 | |
| POST | `/api/policies` | 정책 생성 (`201` + `Location`) | |
| PUT | `/api/policies/{id}` | 정책 수정 | |
| DELETE | `/api/policies/{id}` | 정책 삭제(관련 위반은 FK CASCADE로 함께 삭제) | |
| GET | `/api/violations` | 현재 블랙리스트 위반 목록 | `skip`, `take`, `search`(디바이스 코드·호스트명·소프트웨어 이름), `since`(최초 적발 시각 ISO 8601), `format=csv` |

목록 `search`는 인벤토리 조회와 같이 SQL `LIKE` 와일드카드가 이스케이프된 부분 일치입니다. 정책 목록 `classification`은 `white` | `managed` | `black`입니다.

`POST` / `PUT` 본문 예:

```json
{
  "productName": "uTorrent*",
  "publisher": null,
  "versionPattern": null,
  "classification": "black",
  "notes": "P2P 금지",
  "enabled": true
}
```

`classification`은 `white` | `managed` | `black` 입니다. 기존 DB에는 `Apply-DbSchema.ps1` 또는 `GET /api/schema/sql`의 위반 테이블 `CREATE` 문을 적용하세요.

```powershell
$token = $env:Security__AdminToken
if (-not $token) { $token = $env:Security__Token }
$headers = @{ Authorization = "Bearer $token" }
Invoke-RestMethod -Headers $headers -Uri "http://127.0.0.1:5080/api/inventory/devices?search=PC-01&staleAfterHours=24"
Invoke-RestMethod -Headers $headers -Uri "http://127.0.0.1:5080/api/inventory/software?classification=unclassified"
Invoke-WebRequest -Headers $headers -Uri "http://127.0.0.1:5080/api/inventory/software?format=csv" -OutFile software.csv
Invoke-RestMethod -Headers $headers -Uri "http://127.0.0.1:5080/api/policies?search=Torrent&classification=black"
Invoke-RestMethod -Headers $headers -Uri "http://127.0.0.1:5080/api/violations?search=PC-01&since=2026-01-01T00:00:00Z"
Invoke-WebRequest -Headers $headers -Uri "http://127.0.0.1:5080/api/policies?format=csv" -OutFile policies.csv
```

Worker/Watchdog는 진단용으로 1회 실행 모드도 지원합니다.

```bash
dotnet run --project src/SwLicenseWatcher.Agent.Worker -- --Agent:RunOnceForDiagnostics=true
dotnet run --project src/SwLicenseWatcher.Agent.Watchdog -- --Watchdog:RunOnceForDiagnostics=true
```

## 라이선스

이 프로젝트는 [MIT License](LICENSE)로 배포됩니다. 사용된 서드파티 구성 요소의 라이선스 정보는 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)를 참고하세요.
