# sw-license-watcher

.NET 10 기반의 기업용 소프트웨어 라이선스 감시 서비스입니다. 로컬 PC 에이전트는 **Watchdog + Worker**의 투 프로세스 Windows Service 구조를 사용하고, 서버는 **ASP.NET Core API**와 SQL Server로 소프트웨어 수집과 업데이트 메타데이터를 제공합니다.

## 구현된 핵심 요구사항

- **Windows Service 2개**
  - `SwLicenseWatcher.Agent.Watchdog`: 자체 패치, SHA-256/Authenticode 검증, 백업/롤백 정책 담당
  - `SwLicenseWatcher.Agent.Worker`: 설치 소프트웨어 수집, heartbeat/snapshot 전송 담당
- **로컬 상태 저장 설계**
  - 전송 실패 스냅샷을 원자적 파일 큐에 저장하고 다음 주기에 재전송
  - 큐 페이로드는 DPAPI(`LocalMachine` 기본값)로 보호
- **서버 측 API**
  - 수집 API: `/api/inventory/snapshots`, `/api/agents/heartbeats`
  - SQL Server 트랜잭션 기반 PC UPSERT 및 설치 소프트웨어 교체 저장
  - 공유 API 토큰 인증 및 원격 요청 HTTPS 강제
  - 설계/스키마 API: `/api/design`, `/api/schema/sql`
  - 업데이트 manifest API: `/api/updates/worker/manifest`
- **SQL Server 스키마 커스터마이징**
  - PC entity / PC installed software / software policy(white|managed|black) 테이블 이름과 컬럼 이름 모두 `appsettings.json`에서 변경 가능
- **수집 방식 안전장치**
  - `Win32_Product` / WMI 미사용
  - `HKLM(64)`, `HKLM(32)`, `HKCU`, 로드된 `HKEY_USERS` 사용자 SID의 `Uninstall` 키 순회
  - 접근이 거부된 레지스트리 키는 개별 건너뛰기
- **자체 패치 안정성**
  - 랜덤 Jitter 기반 업데이트 주기
  - HTTPS 패키지 다운로드, SHA-256 및 WinVerifyTrust Authenticode 검증
  - ZIP 경로 이탈 방지, 서비스 중지/교체/시작, 헬스체크 실패 시 자동 롤백
- **Native AOT 컴파일**
  - Worker/Watchdog/Api 모두 `PublishAot=true`로 네이티브 바이너리 publish
  - Source-generated 구성 바인딩(`EnableConfigurationBindingGenerator`)과 System.Text.Json source generator를 사용해 리플렉션 없이 동작

## 프로젝트 구조

- `/src/SwLicenseWatcher.Core`: 계약, 옵션, 레지스트리 수집기, DPAPI 보호기, SQL Server DDL 생성기
- `/src/SwLicenseWatcher.Agent.Worker`: inventory 수집 Windows Service
- `/src/SwLicenseWatcher.Agent.Watchdog`: self-update Windows Service
- `/src/SwLicenseWatcher.Api`: ASP.NET Core API
- `.github/workflows/ci.yaml`: CI (빌드/테스트, Dependabot auto-merge 트리거)
- `.github/workflows/auto-merge.yaml`: Dependabot PR 자동 머지
- `.github/workflows/cd.yaml`: CD (CI 성공 후 publish 산출물 ZIP GitHub Release)

## CI/CD

- **CI (`ci.yaml`)**: `main` 대상 push/PR에서 `windows-latest`로 솔루션 Restore/Build를 검증합니다. `tests/` 아래 테스트 프로젝트가 있으면 자동으로 실행합니다. Dependabot PR이 CI를 통과하면 auto-merge 워크플로우를 트리거합니다.
- **CD (`cd.yaml`)**: `main`에서 CI가 성공하면 `Agent.Watchdog`, `Agent.Worker`, `Api`를 win-x64 **Native AOT**로 publish하고 `SwLicenseWatcher-{version}.zip`으로 GitHub Release를 생성합니다. 버전은 최신 태그의 patch 자동 증가이며, 커밋 메시지에 `Update Version To x.y.z`를 포함해 재정의할 수 있습니다.

Release ZIP 구조:

```
SwLicenseWatcher-{version}/
  agent-watchdog/win-x64/   self-update Windows Service
  agent-worker/win-x64/     inventory 수집 Windows Service
  api/win-x64/              ASP.NET Core API 서버
```

## 커스터마이징 포인트

서버 테이블/컬럼 매핑은 `/src/SwLicenseWatcher.Api/appsettings.json`의 `Storage:SqlServer`에서 바꿀 수 있습니다.

예:

- `PcTable.TableName`
- `PcTable.DeviceCodeColumn`
- `InstalledSoftwareTable.TableName`
- `InstalledSoftwareTable.DisplayNameColumn`
- `SoftwarePolicyTable.TableName`
- `SoftwarePolicyTable.ClassificationColumn`

현재 기본 예시는 다음처럼 커스텀되어 있습니다.

- PC 테이블: `company_pc`
- 설치 SW 테이블: `company_pc_installed_sw`
- 정책 테이블: `company_sw_policy`

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

토큰과 SQL Server 연결 문자열은 소스에 저장하지 말고 환경 변수 또는 비밀 저장소로 주입합니다. API 토큰은 32자 이상이어야 하며 API, Worker, Watchdog에 동일한 값을 설정합니다.

```text
Security__Token=<random-token>
Storage__SqlServer__ConnectionString=<sql-server-connection-string>
Agent__ApiToken=<same-random-token>
Watchdog__ApiToken=<same-random-token>
```

운영 SQL Server 연결에서는 서버 인증서를 검증하고 `TrustServerCertificate=False`를 유지하십시오. 최초 실행 전에 인증된 `GET /api/schema/sql` 결과를 검토하여 데이터베이스에 적용해야 합니다.

Watchdog에는 Worker 실행 파일이 설치된 `Watchdog__WorkerInstallDirectory`와 업데이트 후 확인할 `Watchdog__WorkerHealthUrl`도 설정합니다. 업데이트 ZIP에는 정확히 하나의 `SwLicenseWatcher.Agent.Worker.exe`가 있어야 하며 모든 EXE/DLL이 신뢰된 Authenticode 서명을 가져야 합니다.

## 수동 실행 예시

```bash
dotnet build SwLicenseWatcher.slnx
dotnet run --project src/SwLicenseWatcher.Api
```

API 실행 후:

- `GET /api/design`: 전체 설계 요약
- `GET /api/schema/sql`: 현재 설정 기준 SQL Server DDL

Worker/Watchdog는 진단용으로 1회 실행 모드도 지원합니다.

```bash
dotnet run --project src/SwLicenseWatcher.Agent.Worker -- --Agent:RunOnceForDiagnostics=true
dotnet run --project src/SwLicenseWatcher.Agent.Watchdog -- --Watchdog:RunOnceForDiagnostics=true
```

## 라이선스

이 프로젝트는 [MIT License](LICENSE)로 배포됩니다. 사용된 서드파티 구성 요소의 라이선스 정보는 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)를 참고하세요.
