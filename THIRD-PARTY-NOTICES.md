# Third-Party Notices

sw-license-watcher는 아래의 서드파티 소프트웨어를 사용합니다. 각 구성 요소는 해당 라이선스 조건에 따라 배포됩니다.

## NuGet 패키지

이 저장소에 명시된 NuGet 패키지는 버전 정보를 생략합니다. 버전이 포함되면 Dependabot 자동 업데이트 시 이 문서가 오래된 상태로 남기 쉬워서 의도적으로 제외합니다.

| 패키지 | 라이선스 | 저작권 | 출처 |
| --- | --- | --- | --- |
| Microsoft.Extensions.Hosting | MIT | © .NET Foundation and Contributors | https://github.com/dotnet/runtime |
| Microsoft.Extensions.Hosting.WindowsServices | MIT | © .NET Foundation and Contributors | https://github.com/dotnet/runtime |
| Microsoft.Extensions.Http | MIT | © .NET Foundation and Contributors | https://github.com/dotnet/runtime |
| Microsoft.Toolkit.Uwp.Notifications | MIT | © .NET Foundation and Contributors | https://github.com/CommunityToolkit/WindowsCommunityToolkit |
| BouncyCastle.Cryptography | MIT | © The Legion of the Bouncy Castle Inc. | https://github.com/bcgit/bc-csharp |
| MySqlConnector | MIT | © Bradley Grainger and contributors | https://github.com/mysql-net/MySqlConnector |
| Npgsql | PostgreSQL License | © The Npgsql Development Team | https://github.com/npgsql/npgsql |

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

## Bouncy Castle License

`BouncyCastle.Cryptography`는 MIT 라이선스로 배포됩니다. 이 패키지에는 Apache License 2.0으로 배포되는 수정된 Bzip2 라이브러리가 포함되어 있습니다. https://www.apache.org/licenses/LICENSE-2.0

```
MIT License (https://opensource.org/licenses/MIT)

Copyright (c) 2000-2026 The Legion of the Bouncy Castle Inc. (https://www.bouncycastle.org).

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

## PostgreSQL License

`Npgsql`은 PostgreSQL License로 배포됩니다. https://github.com/npgsql/npgsql/blob/main/LICENSE

```
PostgreSQL License

Copyright (c) The Npgsql Development Team

Permission to use, copy, modify, and distribute this software and its
documentation for any purpose, without fee, and without a written agreement
is hereby granted, provided that the above copyright notice and this
paragraph and the following two paragraphs appear in all copies.

IN NO EVENT SHALL THE NPGSQL DEVELOPMENT TEAM BE LIABLE TO ANY PARTY FOR
DIRECT, INDIRECT, SPECIAL, INCIDENTAL, OR CONSEQUENTIAL DAMAGES, INCLUDING
LOST PROFITS, ARISING OUT OF THE USE OF THIS SOFTWARE AND ITS
DOCUMENTATION, EVEN IF THE NPGSQL DEVELOPMENT TEAM HAS BEEN ADVISED OF THE
POSSIBILITY OF SUCH DAMAGE.

THE NPGSQL DEVELOPMENT TEAM SPECIFICALLY DISCLAIMS ANY WARRANTIES,
INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY
AND FITNESS FOR A PARTICULAR PURPOSE. THE SOFTWARE PROVIDED HEREUNDER IS
ON AN "AS IS" BASIS, AND THE NPGSQL DEVELOPMENT TEAM HAS NO OBLIGATIONS TO
PROVIDE MAINTENANCE, SUPPORT, UPDATES, ENHANCEMENTS, OR MODIFICATIONS.
```

## Windows API (ESENT, DPAPI 등)

본 프로젝트는 Windows 운영체제가 제공하는 시스템 구성 요소(ESENT, DPAPI, 레지스트리 API 등)를 호출합니다. 해당 구성 요소는 Microsoft Windows 라이선스 조건에 따라 사용자 시스템에 포함되어 있으며, 본 저장소에는 재배포되지 않습니다.
