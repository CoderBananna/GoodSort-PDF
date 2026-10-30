# SignPath application preparation

Maintainer notes, not an application submission. No SignPath account, certificate or signing service has been connected yet.

## Project details

| Item | Detail |
| --- | --- |
| Project | GoodSort PDF |
| Repository / homepage | https://github.com/CoderBananna/GoodSort-PDF |
| Downloads | https://github.com/CoderBananna/GoodSort-PDF/releases/latest |
| Maintainer | Andrew, GitHub `CoderBananna` |
| Format | Single self-contained Windows x64 executable; no installer |
| Current public version | 1.5.1, unsigned |
| Signing policy | [Code signing policy](CODE-SIGNING.md) |
| Privacy and removal | [Privacy policy](PRIVACY.md) |
| Automated build | [Windows build](../.github/workflows/build.yml) |
| Usage and development | [User guide](USER-GUIDE.md), [development guide](DEVELOPMENT.md) |

GoodSort PDF converts Word documents, PowerPoint presentations and images into PDFs, individually or in batches. It combines files, prepares school or community newsletters, compresses PDFs, arranges individual pages and optionally prints saved outputs. It processes documents locally and is free to use, with no subscription or paid feature tiers.

It started with preparing fortnightly school newsletters and grew into a general document toolkit. Describe actual use and provide genuine references if SignPath asks about project reputation; do not treat a public repository or test results as proof of adoption.

## Before applying

- [x] **Select an OSI-approved project licence.** Andrew chose GPL version 3 (`GPL-3.0-only`). The full licence is in `LICENSE`, with a copyright/no-warranty notice and full text embedded in new app builds. [Licensing details](LICENSING.md) distinguish the existing download and the remaining dependency review.
- [x] **Confirm GitHub MFA.** Andrew confirmed it is enabled on 30 October 2026; it was not independently exposed by the API. Enable MFA for SignPath when the account is created.
- [x] **Confirm a successful hosted build.** [Run 41059551427](https://github.com/CoderBananna/GoodSort-PDF/actions/runs/41059551427) passed on source commit `9a21986a1aeb8bf77ad2251b9643960a150605b3`: 50 core checks, 49 Windows mode checks and executable metadata verification. Two conditional animation checks were skipped because animations were disabled on the hosted runner. [Saved evidence](verification/signpath-build-2026-10-30.json) records the hash, SDK and results. A manual run retains one-day build evidence. Executable upload is an optional manual input, off by default and enabled explicitly for release candidates. The unsigned 1.5.1 maintenance release proceeds while the documented enquiries remain pending.
- [x] **Check dependency evidence.** The [30 October findings](LICENSING-CHECK.md) confirm Microsoft redistribution permission and record the remaining SDK conditions. No concrete Office redistribution conflict was identified.
- [ ] **Resolve SDK distribution terms and GPL treatment.** Establish adequate agreement to the Microsoft terms and a supported System Libraries classification or alternative.
- [ ] **Confirm the Windows SDK exception with SignPath.** See the exact disclosure below. Acceptance must not be assumed.
- [ ] **Review the policy and application facts.** Ensure roles and contact details are current, and supply any identity or account information privately in the application rather than in this repository.

The policy, dependency locks, SDK pin, hosted workflow, proposed artifact configuration and privacy/removal documentation are prepared. These items do not themselves establish eligibility or acceptance.

## Dependency disclosure

The app is self-contained. `packages.lock.json` captures application package versions and hashes; SDK framework/runtime packs are selected by the exact SDK in `global.json`. Framework packs do not all appear in the NuGet lock files. Review the publish dependency manifest and [third-party notices](../THIRD-PARTY-NOTICES.txt) together when updating the SDK or dependencies.

| Component | Version at preparation | Licence / treatment |
| --- | --- | --- |
| PDFsharp | 6.2.4 | MIT |
| Microsoft.Extensions.Logging.Abstractions | 8.0.3 | MIT |
| Microsoft.Extensions.DependencyInjection.Abstractions | 8.0.2 | MIT |
| System.Security.Cryptography.Pkcs | 10.0.14 | MIT |
| .NET / Windows Desktop runtime | 10.0.14, selected by SDK 10.0.403 | MIT plus upstream notices |
| C#/WinRT runtime (`WinRT.Runtime.dll`) | Supplied by Windows SDK projection pack | MIT |
| Windows SDK .NET projection (`Microsoft.Windows.SDK.NET.dll`) | Pack 10.0.19041.57 | Microsoft Windows SDK licence; explicitly redistributable subject to terms; GPL/SignPath treatment outstanding |
| Microsoft Word and PowerPoint | User-installed desktop applications | Proprietary, separately installed, not distributed with GoodSort PDF |

Build-only tooling, such as Microsoft.NET.ILLink.Tasks, is not distributed as a separate application feature. Windows APIs and printer drivers are supplied by the operating system/user. Review the actual candidate if any of these facts change.

### Windows SDK question for the application

> GoodSort PDF is a self-contained .NET Windows desktop application. It bundles the Microsoft Windows SDK .NET projection, including `Microsoft.Windows.SDK.NET.dll` from `Microsoft.Windows.SDK.NET.Ref` 10.0.19041.57, under the Microsoft Windows SDK licence. This enables Windows' native PDF rendering for page previews and printing. Microsoft's official Windows SDK REDIST list explicitly permits including the unmodified projection in applications calling WinRT APIs. May this component, bundled inside our single executable, be included under your System Libraries exception? The C#/WinRT runtime has its own MIT licence. Word and PowerPoint are separately installed applications accessed through COM automation; they are not bundled. Please also confirm that this external Office integration is acceptable.

Supporting files: [Windows SDK licence](licenses/windows-sdk-license.rtf), [application project](../src/NewsletterCombiner.App/NewsletterCombiner.App.csproj), [preview implementation](../src/NewsletterCombiner.App/PdfPreviewSession.cs), [printing implementation](../src/NewsletterCombiner.App/PdfPrinter.cs), and [Office exporter](../src/NewsletterCombiner.App/OfficeExporter.cs).

A preliminary eligibility enquiry covering these facts was emailed to SignPath support on 30 October 2026; a response is pending. This is not a submitted application. See the [enquiry status](LICENSING-CHECK.md#enquiry-status) for the separate FSF and Microsoft enquiries. It asks about signing eligibility. The separate [dependency check](LICENSING-CHECK.md) records the Microsoft agreement requirement and GPL classification question; SignPath acceptance would not settle those obligations.

## After acceptance, before the first signed release

1. Enable SignPath MFA and configure Andrew's committer, reviewer and approver responsibilities. Keep manual approval enabled for every release.
2. Install the SignPath GitHub connector with access limited to this repository, and associate it with the SignPath project.
3. Have SignPath review the [artifact configuration](signing/artifact-configuration.xml). Match the product name and both version fields. A future three-part release version requires its corresponding four-part file version.
4. Configure origin restrictions for this repository and approved workflow/source. All jobs leading to a signing request must use GitHub-hosted runners.
5. Add the SignPath organisation/project/policy identifiers and a least-privilege submitter token through GitHub settings. Do not commit secrets. Integrate the official submit-signing-request action, pinned to a reviewed commit, using the upload step's artifact ID.
6. Request signing only from a reviewed release build. Fork and pull-request builds must never obtain signing credentials or submit signing requests. A manual workflow dispatch alone is not manual signing approval.
7. Verify the returned signature, timestamp, metadata and hash, and run the appropriate Windows checks. Publish the matching source and licence alongside the release. Only then change the public status from unsigned and make the SignPath attribution unconditional.

## Cost controls

The build uses standard `windows-2022` GitHub-hosted runners for this public repository. It has a 20-minute timeout and no dependency cache. Routine push/PR runs do not upload executables; manual main-branch runs retain small evidence files for one day. Executable uploads require an explicit manual input; the default remains off to avoid retaining unnecessary large artifacts. Several manual runs in a day still consume storage: check account-wide Actions/Packages usage and delete unneeded artifacts before reaching the allowance. This repository workflow does not configure account billing or guarantee unused storage elsewhere on the account.

## Sources

- [SignPath Foundation terms](https://signpath.org/terms.html)
- [Apply to SignPath Foundation](https://signpath.org/apply.html)
- [SignPath GitHub integration](https://docs.signpath.io/trusted-build-systems/github)
- [Artifact configuration examples](https://docs.signpath.io/artifact-configuration/examples)
- [GitHub Actions billing](https://docs.github.com/en/billing/concepts/product-billing/github-actions)
