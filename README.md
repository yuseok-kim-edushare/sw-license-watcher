# sw-license-watcher

.NET 10 기반의 기업용 소프트웨어 라이선스 감시 설계/스캐폴드입니다. 로컬 PC 에이전트는 **Watchdog + Worker**의 투 프로세스 Windows Service 구조를 사용하고, 서버는 **ASP.NET Core API**로 소프트웨어 수집과 업데이트 메타데이터를 제공합니다.

## 구현된 핵심 요구사항

- **Windows Service 2개**
  - `SwLicenseWatcher.Agent.Watchdog`: 자체 패치, SHA-256/Authenticode 검증, 백업/롤백 정책 담당
  - `SwLicenseWatcher.Agent.Worker`: 설치 소프트웨어 수집, heartbeat/snapshot 전송 담당
- **로컬 상태 저장 설계**
  - ESENT `.edb` 파일 경로/테이블 이름 구성
  - DPAPI 보호기(`DpapiLocalStateProtector`) 포함
- **서버 측 API**
  - 수집 API: `/api/inventory/snapshots`, `/api/agents/heartbeats`
  - 설계/스키마 API: `/api/design`, `/api/schema/sql`
  - 업데이트 manifest API: `/api/updates/worker/manifest`
- **SQL Server 스키마 커스터마이징**
  - PC entity / PC installed software / software policy(white|managed|black) 테이블 이름과 컬럼 이름 모두 `appsettings.json`에서 변경 가능
- **수집 방식 안전장치**
  - `Win32_Product` / WMI 미사용
  - `HKLM(64)`, `HKLM(32)`, `HKCU` `Uninstall` 키만 순회
- **자체 패치 안정성**
  - 랜덤 Jitter 기반 업데이트 주기
  - Heartbeat 복구 실패 시 자동 Rollback 설계

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
- **CD (`cd.yaml`)**: `main`에서 CI가 성공하면 `Agent.Watchdog`, `Agent.Worker`, `Api`를 win-x64로 publish하고 `SwLicenseWatcher-{version}.zip`으로 GitHub Release를 생성합니다. 버전은 최신 태그의 patch 자동 증가이며, 커밋 메시지에 `Update Version To x.y.z`를 포함해 재정의할 수 있습니다.

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
