# Azure Artifact Signing

이 기능은 아직 작업 중입니다. 기본 플래그는 꺼져 있고, Azure Artifact Signing으로 서명이 실제로 되는지는 회사에서 계정과 인증서 프로필을 확보한 뒤에 확인합니다. 그 확인 전에는 운영에서 켜지 않습니다.

Windows API가 GitHub에서 받은 에이전트 ZIP과 API ZIP의 EXE/DLL을 서명할 때 쓰는 구성입니다. 패치를 적용하는 순서는 [patch-management.ko.md](patch-management.ko.md)에 있습니다.

`Signing:Artifact`가 없거나 `Enabled`가 `false`이거나, Endpoint·계정·프로필·도구 경로 중 하나라도 비어 있으면 서명하지 않습니다. API는 그대로 기동합니다. `SignClientPackages`와 `SignServerPackages` 기본값은 모두 `false`입니다. 플래그가 꺼져 있으면 서명하지 않고 알림도 보내지 않습니다. 클라이언트 플래그를 `true`로 켰는데 서명이 실패하면 핀은 바꾸지 않고 알림을 보냅니다. 서버 플래그는 아직 켜지 않습니다. Azure에서 서명이 실제로 되는지는 회사에서 Artifact Signing 계정과 인증서 프로필을 확보한 뒤에 확인합니다. 저장소의 서명 테스트는 그 확인과 별개이며 Azure를 호출하지 않습니다.

`signtool`은 HTTP 요청 안이 아니라 서명 전용 백그라운드 작업에서만 실행합니다. 그 작업도 API 서비스 계정으로 돕니다. 서명 역할을 다른 Windows 계정에 두려면 이 작업과 계정을 분리해야 합니다.

Linux API 호스트는 이 서명을 수행하지 못합니다. 에이전트 패키지를 서명하려면 API가 Windows 서비스 또는 IIS여야 합니다.

## Azure에서 준비할 것

1. Artifact Signing 계정과 인증서 프로필을 만듭니다. 인증서는 짧은 기간만 유효하므로 타임스탬프가 필요합니다. 서버는 `http://timestamp.acs.microsoft.com`을 기본으로 씁니다.
2. API 프로세스가 쓰는 ID에 **Artifact Signing Certificate Profile Signer** 역할을 줍니다. Windows 서비스(LocalSystem)는 VM의 관리 ID를 쓸 수 있습니다. IIS는 그 앱 풀 ID가 같은 역할을 가져야 합니다.
3. 클라이언트 비밀은 `appsettings.json`에 넣지 않습니다. `signtool`이 로드하는 `Azure.CodeSigning.Dlib.dll`은 프로세스의 `DefaultAzureCredential`로 Azure에 접속합니다. 관리 ID를 쓸 수 없으면 서비스 또는 앱 풀 환경 변수로 자격 증명을 줍니다. 예시 키는 `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET`입니다. 값은 이 문서에 적지 않습니다.

## 서버에 둘 도구

API는 Native AOT라 서명 SDK를 프로세스 안에 넣지 않습니다. 아래를 서버에 두고 경로를 설정합니다.

- x64 `signtool.exe` (Windows SDK)
- x64 `Azure.CodeSigning.Dlib.dll` (Artifact Signing 클라이언트)
- dlib이 요구하는 .NET 8 런타임. API 자체에는 런타임이 없어도 서명 도구에는 필요합니다.

## appsettings

```json
"Signing": {
  "Artifact": {
    "Enabled": true,
    "SignClientPackages": true,
    "SignServerPackages": false,
    "Endpoint": "https://eus.codesigning.azure.net/",
    "AccountName": "contoso-signing",
    "CertificateProfileName": "contoso-profile",
    "TimestampUrl": "http://timestamp.acs.microsoft.com",
    "SignToolPath": "C:\\Tools\\Windows Kits\\10\\bin\\10.0.26100.0\\x64\\signtool.exe",
    "DlibPath": "C:\\Tools\\ArtifactSigning\\x64\\Azure.CodeSigning.Dlib.dll",
    "Timeout": "00:10:00"
  }
}
```

`Endpoint`는 계정이 있는 지역의 `https://<region>.codesigning.azure.net/`입니다. `Timeout`은 GitHub 다운로드 제한과 따로 둡니다. 비어 있으면 10분입니다.

## 호출

서명이 구성되면 API는 임시 `metadata.json`을 만들고 `signtool`을 실행한 뒤 파일을 지웁니다. 파일 내용은 다음 세 필드입니다.

- `Endpoint`
- `CodeSigningAccountName` (`AccountName`)
- `CertificateProfileName`

실행 형태는 다음과 같습니다.

```text
signtool sign /fd SHA256 /tr http://timestamp.acs.microsoft.com /td SHA256 /dlib <DlibPath> /dmdf <metadata.json> <file>
```

패키지의 EXE/DLL마다 호출합니다. 끝난 뒤 WinVerifyTrust로 각 파일이 신뢰되는지 확인합니다. 이미 다른 인증서로 서명된 파일이 와도 이 서명이 그 파일의 서명이 됩니다. 에이전트는 그 뒤 매니페스트 해시와 Authenticode를 확인하고 적용합니다. API 도우미는 `Signed=true`일 때 `Get-AuthenticodeSignature`가 `Valid`인 파일만 교체합니다.
