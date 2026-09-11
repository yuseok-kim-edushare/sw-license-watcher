# 설치 관리자 사용 안내

직원에게 PowerShell이나 `appsettings.json`을 가르치지 않습니다. IT가 **회사 Setup.exe 하나**를 만들고, 직원은 그 파일만 실행합니다.

역할이 셋으로 갈립니다.

| 누가 | 무엇을 |
| --- | --- |
| IT | `Packager.exe`로 서버 주소와 에이전트 키를 넣어 Setup.exe를 만듦 |
| 직원 | Setup.exe를 관리자로 실행. 자산번호는 알면 넣고, 모르면 비움 |
| 관리자 (`/admin`) | 대시보드에서 수집 확인. **제거만** 승인/거절 |

직원에게 주면 안 되는 것:

- `SwLicenseWatcher-{version}.zip` 전체 (API·패키저·에이전트 원본)
- `SwLicenseWatcher.Packager-{version}.zip` 전체 (패키저·에이전트 원본)
- 페이로드가 없는 런처 스텁 `SwLicenseWatcher-Setup.exe` (릴리스 안의 그 파일)
- 서버 `AdminToken`, SQL 연결 문자열

직원이 받는 것은 패키저가 **새로 만든** `SwLicenseWatcher-Setup.exe` 하나뿐입니다. 이 파일 안에 에이전트 키가 들어 있으므로 USB·공지 첨부도 내부망에서만 다루세요.

Intune/SCCM이 있으면 이 문서 대신 [company-deployment.md](company-deployment.md)의 `Install-Agent.ps1`을 씁니다. 집 PC 한 대 재현은 [hands-on-practice.ko.md](hands-on-practice.ko.md)를 참고하세요.

## 1. 만들기 전에

1. 서버 API가 떠 있어야 합니다. 브라우저에서 `/health`가 200인지, `/admin`이 열리는지 확인합니다.
2. `Security:AgentToken`과 `Security:AdminToken`이 서로 다르고 각 32자 이상이어야 합니다. [New-ApiToken.ps1](../deploy/scripts/New-ApiToken.ps1)을 두 번 실행해 만듭니다.
3. 회사 PC용 `ServerBaseUrl`은 **HTTPS**입니다. `http://127.0.0.1` 같은 loopback은 집 진단만 됩니다. `http://192.168.x.x`는 에이전트가 시작을 거부합니다.

패키저·Setup·에이전트는 self-contained / Native AOT라 **직원 PC에 .NET SDK를 깔지 않습니다.**

## 2. IT: 회사 Setup.exe 만들기

GitHub Release에서 **전체 ZIP** `SwLicenseWatcher-{version}.zip`을 받아 풉니다. API 설치와 회사 Setup 만들기를 한 묶음으로 할 수 있습니다. API가 이미 있고 패키저만 필요하면 더 작은 `SwLicenseWatcher.Packager-{version}.zip`도 있습니다.

풀면 패키저가 보는 쪽은 대략 이렇게 있습니다. 전체 ZIP에는 여기에 `api/win-x64`, `api/iis/win-x64`가 더 있습니다.

```
Packager.exe
SwLicenseWatcher-Setup.exe          ← 빈 런처 스텁. 직원에게 주지 마세요
setup-ui/SwLicenseWatcher.Setup.exe
agent-worker/win-x64/
agent-watchdog/win-x64/
```

`Packager.exe`를 실행합니다. SDK는 필요 없습니다.

| 칸 | 필수 | 넣을 값 |
| --- | --- | --- |
| 서버 주소 | 예 | `https://license-watcher.회사.local` (끝 슬래시 없어도 됨) |
| 에이전트 키 | 예 | 서버 `Security:AgentToken`. 32자 이상. **AdminToken이 아님** |
| 공식 Release ZIP | 아니오 | 전체 ZIP이나 Packager ZIP을 폴더 구조 그대로 풀었으면 비움. 에이전트만 다른 ZIP에서 가져올 때만 지정 |
| 저장 위치 | 예 | 기본은 바탕화면의 `SwLicenseWatcher-Setup.exe` |

**회사 설치본 만들기**를 누르면 저장 위치에 파일이 생깁니다. 이 파일이 직원용입니다. 스텁과 이름이 같아도, **덮어쓸 대상은 바탕화면(또는 고른 경로)이지 Packager 폴더의 스텁이 아니어야** 합니다. 스텁에 이미 페이로드가 붙어 있으면 패키저가 거부합니다.

버전 문자열은 에이전트 폴더의 `.version`에서 읽습니다. 새 릴리스로 다시 포장하면 그 버전이 Setup과 ARP 표시 버전에 들어갑니다.

## 3. USB · 그룹웨어 공지

첨부(또는 USB 루트)는 패키저가 만든 exe 하나만 둡니다. 공지 본문 예:

```text
SW 라이선스 수집 에이전트 설치

1. 첨부 SwLicenseWatcher-Setup.exe 를 실행합니다. (관리자 권한 허용)
2. 자산번호를 알고 있으면 입력합니다. 모르면 비워 둡니다. 추측해서 쓰지 마세요.
3. 설치를 누릅니다.

서버 주소와 키는 설치본에 들어 있습니다. 다시 묻지 않습니다.
제거는 설정 앱에서 할 수 있지만, IT가 /admin에서 승인한 뒤에만 끝납니다.
문제가 있으면 IT에 문의하세요.
```

직원에게 키를 타이핑하게 하지 마세요. 공백·줄바꿈이 섞입니다. 키는 패키저에만 넣습니다.

## 4. 직원: 설치

1. Setup.exe를 실행합니다. UAC가 뜨면 허용합니다. (관리자 권한이 있어야 서비스를 등록합니다.)
2. 화면에 서버 주소가 읽기 전용으로 보입니다.
3. **PC 관리 식별자 / 자산번호**는 선택입니다.
   - 비우면 컴퓨터 이름이 등록 이름이 됩니다. 미리보기(`등록 이름: …`)를 확인합니다.
   - 회사 자산번호가 있으면 그대로 넣습니다. 같은 번호를 두 대가 쓰면 서버에서 **한 자산으로 합쳐집니다.**
4. **설치**를 누릅니다.

설치 결과:

| 항목 | 위치 |
| --- | --- |
| Worker | `C:\Program Files\SwLicenseWatcher\Agent.Worker` |
| Watchdog | `C:\Program Files\SwLicenseWatcher\Agent.Watchdog` |
| 회사 Setup 복사본 | `C:\Program Files\SwLicenseWatcher\SwLicenseWatcher-Setup.exe` |
| 서비스 | `SwLicenseWatcher.Agent.Worker`, `SwLicenseWatcher.Agent.Watchdog` (**LocalSystem**) |
| 상태 | `C:\ProgramData\SwLicenseWatcher\` |
| 설정 앱 | SW License Watcher |

도메인 이름은 묻지 않습니다. `USERDOMAIN`(없으면 `WORKGROUP`)을 넣습니다.

IT 확인: `/admin`에서 해당 PC가 보이는지. 기본 수집 주기는 30분 ± 15분입니다.

## 5. 업그레이드

새 GitHub Release가 나오면 IT가 **같은 패키저 절차**로 Setup.exe를 다시 만듭니다. 서버 주소와 에이전트 키는 그대로 두면 됩니다. 키가 바뀌었으면 새 키를 넣습니다.

직원은 새 Setup.exe를 다시 실행합니다. 이미 Worker/Watchdog 서비스가 있으면 화면이 **업그레이드**로 바뀝니다.

- 제거 요청을 서버에 보내지 않습니다. `/admin` 승인 대기와 `pc_uninstall_request` 기록도 없습니다.
- **0.1.0 이상:** 로컬 ML-DSA 장치 키로 증명을 만들고, 서버에 등록된 공개키·인증서를 가져와 확인한 뒤에만 서비스를 멈춥니다.
- **0.1.0 이전:** 비대칭키가 없던 설치본이므로 그 확인을 건너뛰고 바로 파일을 갈아끼웁니다.
- 자산번호는 기존 `appsettings.json` 값을 미리 채웁니다. 칸을 비워 두면 기존 값을 유지합니다.
- `C:\ProgramData\SwLicenseWatcher\state\`의 ML-DSA 장치 키(`device-identity.bin`)와 서버 지정 이름(`assigned-host-name.json`)은 그대로 둡니다. 서버가 인증서를 돌려주면 로컬 신원 파일에 반영합니다. 키가 없던 예전 설치본이면 Worker가 기동 후 한 쌍을 만듭니다.
- 새 설치본의 서버 주소·에이전트 키·현재 필수 설정은 덮어씁니다.

Watchdog 자체 패치(Worker ZIP만 교체)는 이 설치기와 별개입니다. 회사 HTTPS에 Worker 패키지를 올리고 `/admin` **업데이트**에서 핀을 바꾸는 절차는 [company-deployment.md](company-deployment.md) 7절입니다.

## 6. 제거

직원이 설정 앱에서 제거를 누르면 Setup이 서버에 제거 요청을 보냅니다. 서비스는 그 시점에 아직 남습니다.

1. 직원: 설정 → 앱 → SW License Watcher → 제거. UAC가 뜨면 허용합니다. 화면이 **관리자 승인 대기**로 바뀝니다.
2. 관리자: `https://서버/admin` → **제거 요청** → 해당 PC **승인** 또는 **거절**.
3. 승인이면 그 PC가 일회용 코드를 받아 서비스를 지웁니다. 대시보드에는 코드를 보여 주지 않습니다. Setup 화면은 최대 2시간까지 승인을 기다리고, 승인 후 코드 유효 시간은 15분입니다.
4. 거절·만료·대기 취소면 서비스는 그대로입니다.

장치가 아직 한 번도 스냅샷을 올리지 않았으면 제거 요청이 거절됩니다(등록된 PC만 요청 가능). 먼저 설치·수집이 된 뒤 제거하세요.

로컬 해제 키는 없습니다. 로컬 관리자가 `sc.exe delete`로 서비스를 지울 수는 있습니다. 그때 서버 자산은 하트비트 두절로 남습니다.

PowerShell로 같은 흐름을 쓰려면 [Uninstall-Agent.ps1](../deploy/scripts/Uninstall-Agent.ps1)입니다.

## 7. 자주 막히는 지점

| 증상 | 확인할 것 |
| --- | --- |
| “회사 설치 페이로드를 읽지 못했습니다” | 릴리스 안의 **빈 스텁**을 실행한 것입니다. 패키저가 만든 파일을 실행하세요. |
| 패키저가 “이미 페이로드가 붙어 있습니다” | 스텁을 회사본으로 덮어썼습니다. 릴리스 ZIP을 다시 풀고, 저장 위치는 다른 폴더로 하세요. |
| 설치 직후 서비스가 죽음 | 서버 URL이 비-loopback HTTP이거나, 심은 키가 서버 `AgentToken`과 다름. Application 이벤트 로그 |
| `/admin`에 PC가 안 보임 | 아직 PollInterval, 또는 TLS(자체 서명 인증서를 직원 PC 신뢰 루트에 안 넣음) |
| 여러 PC가 한 줄로 합쳐짐 | 자산번호를 같게 넣었거나, 개발용 `pc-demo-001`을 씀 |
| Watchdog Access Denied | 서비스 계정이 LocalSystem이 아님. Setup을 다시 실행 |
| 제거 중 Program Files 접근 실패 | 설정 앱 제거/Setup이 관리자로 안 뜬 상태. UAC를 허용했는지 확인하고, 설치·제거 EXE 모두 `requireAdministrator` 매니페스트가 있어야 함 |
| 제거가 안 끝남 | `/admin`에서 승인했는지, 승인 후 15분이 지나 코드가 만료되지 않았는지. PC가 아직 inventory에 없는지도 확인 |

## 8. 집 또는 랩에서 패키저만 시험할 때

API가 `http://127.0.0.1:5080`이면 패키저 서버 주소에 그 URL을 넣으면 됩니다. HTTPS가 아닌 loopback은 진단용으로만 통과합니다.

소스에서 패키저 묶음을 직접 만들 때:

```powershell
dotnet publish src\SwLicenseWatcher.Setup.Launcher\SwLicenseWatcher.Setup.Launcher.csproj `
  -r win-x64 -c Release -o C:\SwLw\packager
dotnet publish src\SwLicenseWatcher.Setup\SwLicenseWatcher.Setup.csproj `
  -r win-x64 -c Release --self-contained true `
  -p:PublishSingleFile=true -p:PublishAot=false -p:PublishTrimmed=false `
  -o C:\SwLw\packager\setup-ui
dotnet publish src\SwLicenseWatcher.Packager\SwLicenseWatcher.Packager.csproj `
  -r win-x64 -c Release --self-contained true `
  -p:PublishSingleFile=true -p:PublishAot=false -p:PublishTrimmed=false `
  -o C:\SwLw\packager
```

런처 산출 이름은 `SwLicenseWatcher-Setup.exe`입니다. 그 옆에 Worker/Watchdog `win-x64` 폴더를 `agent-worker\win-x64`, `agent-watchdog\win-x64`로 복사한 뒤 `Packager.exe`를 실행하면 전체 Release ZIP 루트와 같은 배치가 됩니다.
