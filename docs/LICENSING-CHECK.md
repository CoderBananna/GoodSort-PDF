# Dependency licensing check — 30 October 2026

Evidence check of source commit `a92136aa603fbb707aea0fb0e86bd044118e3ecd` (1.5.1), its publish dependency manifest, package metadata and the sources below. This records findings and remaining decisions; it is not a legal opinion or an approval from Microsoft, the FSF or SignPath. Clarification enquiries were sent on 30 October 2026 as recorded below. No signing application has been submitted.

## Enquiry status

| Recipient / channel | Sent | Question | Status |
| --- | --- | --- | --- |
| SignPath support, by email | 30 October 2026 | Foundation eligibility of the bundled Windows SDK projection under the System Libraries exception, and external Office integration | Awaiting response |
| FSF Licensing and Compliance, by email | 30 October 2026 | GPLv3 System Libraries classification, single-file bundling and the differing guide/FAQ wording | Awaiting response |
| Microsoft WindowsAppSDK discussion | 30 October 2026 | Intended way for a portable executable to satisfy the Windows SDK user/distributor agreement requirement | [Posted follow-up](https://github.com/microsoft/WindowsAppSDK/discussions/6847#discussioncomment-19294813); awaiting response |

The Microsoft post identifies this as the Windows Platform SDK projection and requests referral if the discussion is not the appropriate channel. An enquiry being sent does not establish acceptance, compliance or a commitment to reply. Responses must be assessed against the actual package and distribution before changing the conclusions below.

## Findings

| Area | Finding | Status |
| --- | --- | --- |
| Project licence | GPL-3.0-only; full text and copyright/no-warranty notice embedded in new builds | Implemented |
| PDFsharp and application NuGet dependencies | Package metadata identifies MIT licences; notices retained | No incompatible licence identified |
| .NET / Windows Desktop runtime and C#/WinRT runtime | MIT with applicable upstream notices | No incompatible licence identified in the reviewed material |
| Windows SDK .NET projection | Proprietary SDK terms; explicitly on Microsoft's redistribution list | Redistributable subject to conditions; see below |
| Word / PowerPoint | Separate user-installed applications, controlled through COM; no Office binaries or Office interop assemblies in the reviewed publish manifest | No Office redistribution issue identified |
| SignPath | System Libraries are allowed, but this particular component has not been accepted | Confirmation outstanding |

## Windows SDK: what is established

The actual publish manifest includes `Microsoft.Windows.SDK.NET.dll` and `WinRT.Runtime.dll` from `Microsoft.Windows.SDK.NET.Ref` **10.0.19041.57**. The package's `RuntimeList.xml` records projection assembly version **10.0.19041.38**, file version **10.0.19041.55**, and C#/WinRT runtime **2.2.0.0**. Package, assembly and file versions are different identifiers.

Microsoft's [Windows SDK redistribution list](https://learn.microsoft.com/en-us/legal/windows-sdk/redist#microsoftwindowssdknetref) expressly lists the projection and runtime. It permits their unmodified distribution as part of an application calling WinRT APIs, subject to the SDK terms. GoodSort uses `Windows.Data.Pdf` for previews and printing. The library is therefore not simply prohibited from redistribution.

The projection package points to the [Windows SDK licence](https://aka.ms/WinSDKLicenseURL), preserved [here](licenses/windows-sdk-license.rtf) and in the app's third-party notices. It must not be relabelled MIT merely because the [C#/WinRT generator/runtime](https://github.com/microsoft/CsWinRT/blob/master/LICENSE) is MIT. A Microsoft maintainer also distinguishes the projection from the Windows App SDK in [this answer](https://github.com/microsoft/WindowsAppSDK/discussions/4368); the package's own metadata is the primary evidence for this version.

Section 2(a) requires, among other things, substantial application functionality, unchanged notices, protective terms agreed to by distributors/end users, and indemnification of Microsoft for specified claims. Distribution is restricted to Microsoft operating systems. The excluded-licence clause prevents making the SDK code itself subject to source-disclosure/modification obligations. These requirements are not met merely by applying GPL to GoodSort's own code.

**Concrete outstanding implementation question:** the app displays the SDK licence in About, but the reviewed code has no explicit acceptance mechanism. This check has not established that an About notice alone satisfies the SDK's agreement requirement. Determine and implement an adequate arrangement limited to Microsoft's component, or remove that bundled component. This issue also applies to earlier downloads containing it; it was not created by choosing GPL or applying for signing.

## GPL System Libraries assessment

[GPLv3 section 1](https://opensource.org/license/gpl-3.0) defines System Libraries by their relationship to an operating-system component, compiler or interpreter. The projection's role as the SDK-supplied bridge to Windows APIs supports that classification; this is an assessment, not an explicit classification of this DLL by the licence authors.

Bundling alone is not a blanket GPLv3 prohibition: the FSF's [GPLv3 guide](https://www.gnu.org/licenses/quick-guide-gplv3.html) expressly permits distribution with qualifying incompatible System Libraries. The [Windows-runtime FAQ](https://www.gnu.org/licenses/gpl-faq.en.html#WindowsRuntimeAndGPL) contains more restrictive DLL-bundling wording. That wording does not establish this projection's status, and should not be presented as a definitive GPLv3 ban without resolving the discrepancy with the licence and guide.

**Remaining question:** establish the projection's classification for this self-contained executable before relying on that exception. A narrow additional permission from the relevant copyright holders is another possible GPL-side route, but would not waive Microsoft's terms or determine SignPath eligibility. No exception or licence change has been made.

## Office integration assessment

`OfficeExporter.cs` obtains `Word.Application` or `PowerPoint.Application` through late-bound COM, opens a document, requests PDF export and releases the objects. Office remains separately installed and licensed. The PDF-only and image features work independently of Office.

This architecture supports treating Office as an external application rather than code redistributed inside GoodSort. The FSF's [separate-program guidance](https://www.gnu.org/licenses/gpl-faq.en.html#MereAggregation) considers both communication mechanism and semantics; separate processes are evidence, not an automatic exemption for every COM integration. No concrete Office-related GPL conflict was identified here. Users still need their own appropriately licensed Office installation. This finding does not cover hosting Office conversion as an unattended service.

## Decisions before a new public binary

1. Resolve the SDK agreement requirement and record how the app/distribution meets it, or replace the bundled projection.
2. Record a supported GPL System Libraries conclusion or an appropriate alternative. Do not treat SignPath acceptance as legal clearance.
3. Ask SignPath to confirm eligibility of this disclosed SDK component for signing. Its [terms](https://signpath.org/terms.html) allow System Libraries but do not name this package. Ordinary unsigned releases do not require SignPath permission.
4. For a GPL release, provide the exact corresponding source, build scripts and dependency source/access information alongside the binary; a moving main-branch link alone is insufficient. Preserve licences and notices.

The executable-upload default remains off while these specific SDK points are open. This is a project precaution, not a rejection or publication ban imposed by SignPath. Source development and testing can continue. No existing release has been removed or replaced by this check.
