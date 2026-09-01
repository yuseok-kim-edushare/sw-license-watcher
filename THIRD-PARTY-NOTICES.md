# Third-Party Notices

sw-license-watcher는 아래의 서드파티 소프트웨어를 사용합니다. 각 구성 요소는 해당 라이선스 조건에 따라 배포됩니다.

## NuGet 패키지

이 저장소에 명시된 NuGet 패키지는 버전 정보를 생략하고, .NET Foundation 소속 패키지는 MIT 라이선스로 표기합니다. 버전이 포함되면 Dependabot 자동 업데이트 시 이 문서가 오래된 상태로 남기 쉬워서 의도적으로 제외합니다.

| 패키지 | 라이선스 | 저작권 | 출처 |
| --- | --- | --- | --- |
| Microsoft.Extensions.Hosting | MIT | © .NET Foundation and Contributors | https://github.com/dotnet/runtime |
| Microsoft.Extensions.Hosting.WindowsServices | MIT | © .NET Foundation and Contributors | https://github.com/dotnet/runtime |
| Microsoft.Extensions.Http | MIT | © .NET Foundation and Contributors | https://github.com/dotnet/runtime |
| System.Security.Cryptography.ProtectedData | MIT | © .NET Foundation and Contributors | https://github.com/dotnet/runtime |

## 런타임 / 프레임워크

| 구성 요소 | 라이선스 | 저작권 | 출처 |
| --- | --- | --- | --- |
| .NET Runtime / SDK (.NET 10) | MIT | © .NET Foundation and Contributors | https://github.com/dotnet/runtime |
| ASP.NET Core | MIT | © .NET Foundation and Contributors | https://github.com/dotnet/aspnetcore |

## MIT License (위 구성 요소들에 적용)

```
The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors

All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Windows API (ESENT, DPAPI 등)

본 프로젝트는 Windows 운영체제가 제공하는 시스템 구성 요소(ESENT, DPAPI, 레지스트리 API 등)를 호출합니다. 해당 구성 요소는 Microsoft Windows 라이선스 조건에 따라 사용자 시스템에 포함되어 있으며, 본 저장소에는 재배포되지 않습니다.
