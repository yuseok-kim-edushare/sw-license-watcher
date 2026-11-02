# 패치 관리

Azure Artifact Signing으로 받은 패키지를 서명하는 부분은 아직 작업 중입니다. `SignClientPackages`와 `SignServerPackages` 기본값은 `false`이고, 실제 Azure 서명 확인은 회사에서 Artifact Signing을 확보한 뒤로 미룹니다.

에이전트(Worker·Watchdog)와 API 서버는 같은 GitHub Release를 받습니다. Windows API가 패키지를 받은 뒤, `Signing:Artifact`가 구성되어 있을 때만 EXE/DLL을 서명하고 그 결과를 적용합니다. 구성이 없으면 서명하지 않고 지금처럼 해시만 확인합니다. Azure 계정과 `signtool` 경로는 [artifact-signing.ko.md](artifact-signing.ko.md)에 있습니다.

서명은 API 서버에서 합니다. 에이전트 PC에는 서명 권한을 두지 않습니다. Linux API는 Windows EXE를 서명할 수 없으므로, 에이전트 패키지를 서명하려면 API가 Windows여야 합니다.

## 에이전트 패치

Watchdog이 `GET /api/updates/worker/manifest`를 읽고 Worker ZIP을 받아 교체합니다. 패키지 URL은 HTTPS여야 합니다. `SwLicenseWatcher.Agent.Worker-{version}.zip`은 Worker 실행 파일이 있는 폴더에 `watchdog-update/`를 함께 두고, Worker 실행 파일은 하나여야 합니다. Watchdog는 Worker를 멈추고 백업한 뒤 교체하고, health가 확인되면 커밋합니다. 그다음 Worker가 Watchdog를 백업하고 교체한 뒤 서비스를 다시 시작합니다. 설치 폴더의 `appsettings.json`과 `appsettings.*.json`은 유지됩니다.

`Updates:Worker`는 기동 시드입니다. 테이블에 행이 없을 때만 복사되고, 이후 핀은 DB가 권위입니다.

1. API `Updates:GitHub:Owner`와 `Repository`에 CD가 올리는 저장소를 넣습니다. 비공개면 `Token`도 넣습니다.
2. `/admin` **업데이트**에서 릴리스를 고르고 **패키지 가져오고 핀 저장**을 누릅니다.
3. 서버가 `SHA256SUMS.txt`로 Worker ZIP을 검증합니다.
4. `SignClientPackages` 기본값은 `false`입니다. 꺼져 있으면 ZIP을 그대로 캐시하고 `RequireAuthenticode`를 `false`로 저장하며 알림은 보내지 않습니다.
5. 이 값을 `true`로 켜고 Azure 서명 값이 모두 있으면, 서명 전용 백그라운드 작업이 ZIP의 EXE/DLL을 서명한 뒤 다시 압축하고 SHA-256을 다시 계산합니다. 핀의 `Sha256`은 이 값이고 `RequireAuthenticode`는 `true`입니다. 서명이 실패하거나 서명 구성이 없으면 핀은 바꾸지 않고, 웹훅이나 SMTP가 있을 때 서명 실패 알림을 보냅니다. Azure 계정으로 이 경로가 실제로 되는지는 회사에서 Artifact Signing을 확보한 뒤에 확인합니다.
6. 핀의 `PackageUrl`은 `https://<API>/api/updates/worker/package/{version}`입니다. 이 GET은 토큰 없이 ZIP을 돌려줍니다. PC가 GitHub에 나갈 필요가 없습니다.

Watchdog는 매니페스트의 SHA-256을 확인한 뒤, `RequireAuthenticode`가 `true`이면 ZIP 안 모든 EXE/DLL의 Authenticode가 신뢰될 때만 적용합니다. 적용 후 health가 실패하면 백업에서 Worker를 롤백합니다.

직접 GitHub 자산 URL을 핀에 넣을 수도 있습니다. PC가 GitHub에 닿을 때만 그렇게 하세요. 그 경로는 서버 서명을 거치지 않습니다.

## API 패치

Windows 서비스와 IIS는 업데이트 도우미가 설치되어 있어야 합니다. 도우미 설치는 [company-deployment.md](company-deployment.md)의 API 설치 절차를 따릅니다.

1. `/admin` **업데이트**에서 이 호스트용 ZIP(`SwLicenseWatcher.Api.win-x64-{version}.zip` 또는 `SwLicenseWatcher.Api.iis-win-x64-{version}.zip`)을 가져옵니다.
2. 서버가 `SHA256SUMS.txt`와 ZIP 해시를 맞춘 뒤 스테이징 `payload`에 풉니다.
3. `SignServerPackages` 기본값은 `false`입니다. 서버 패키지는 서명하지 않고, 서명 없음만으로 알림을 보내지 않습니다. 이 값을 `true`로 두고 서명 구성이 있으면 EXE/DLL을 서명한 뒤 `ready.conf`에 `Signed=true`를 기록합니다. 서명을 요청했는데 서명되지 않으면 알림을 보냅니다. 서명 실행이 실패하면 적용 준비를 끝내지 않습니다. Linux 패키지에는 Authenticode를 적용하지 않습니다.
4. 적용을 누르면, `Signed=true`인 경우 API가 payload의 Authenticode를 먼저 확인합니다. 하나라도 신뢰되지 않으면 도우미를 시작하지 않고 서비스를 멈추지 않습니다.
5. 도우미(`Apply-ApiUpdate.ps1`)가 다시 한 번 `Get-AuthenticodeSignature`가 `Valid`인지 확인한 뒤에 호스트를 멈추고 파일을 교체합니다. `appsettings*.json`과 `device-ca*.json`은 남깁니다. 시작이나 health가 실패하면 백업으로 되돌립니다.

구성이 없으면 `Signed`를 쓰지 않고, 해시가 맞는 패키지를 그대로 적용합니다.
