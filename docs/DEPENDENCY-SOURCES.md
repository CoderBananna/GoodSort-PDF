# Dependency source access — release 1.5.1

The release includes `GoodSort.PDF.1.5.1.source.zip`, an archive of the exact application commit used to build the executable, including build scripts, SDK selection, dependency locks and licence notices. The source archives below provide the upstream code for the unmodified open-source dependencies. Preserve these references and source availability when redistributing the executable.

| Component | Version | Upstream source archive |
| --- | --- | --- |
| PDFsharp | 6.2.4 | [Source at release v6.2.4](https://github.com/empira/PDFsharp/archive/9c3863a71b12067ff36e68a92c1a1f260114e2d1.zip) |
| Microsoft.Extensions.Logging.Abstractions | 8.0.3 | [Source at package repository commit](https://github.com/dotnet/runtime/archive/eba546b0f0d448e0176a2222548fd7a2fbf464c0.zip) |
| Microsoft.Extensions.DependencyInjection.Abstractions | 8.0.2 | [Source at package repository commit](https://github.com/dotnet/runtime/archive/81cabf2857a01351e5ab578947c7403a5b128ad1.zip) |
| .NET / Windows Desktop runtime and System.Security.Cryptography.Pkcs | 10.0.14 | [Source at package repository commit](https://github.com/dotnet/dotnet/archive/0427b9abbead3fa64c6655eb4290c537397131fa.zip) |
| C#/WinRT runtime | 2.2.0.241111.1 | [Source at runtime informational-version commit](https://github.com/microsoft/CsWinRT/archive/8649ee3eeb2445ca2a36d80d878ef60b96a6c65d.zip) |

Microsoft package commits come from their NuGet metadata. PDFsharp's commit is the upstream `v6.2.4` tag; the C#/WinRT commit also matches its `2.2.0.241111.1` tag. These source archives contain upstream build instructions and licences. See [development instructions](DEVELOPMENT.md) for building GoodSort itself.

`Microsoft.Windows.SDK.NET.dll` is a separate Windows SDK component under [Microsoft's terms](licenses/windows-sdk-license.rtf), not relicensed under GPL or MIT. Its package is `Microsoft.Windows.SDK.NET.Ref` 10.0.19041.57. The [licensing check and pending enquiries](LICENSING-CHECK.md) describe its treatment. Word and PowerPoint are supplied and licensed separately by the user.
