# Code signing policy

## Current status

Public release **1.5.1 is unsigned**. The project is preparing an application to SignPath Foundation; acceptance and signing are not yet configured. A successful build does not mean an executable is signed.

If the application is accepted and signed releases are available, the signing attribution will be: **Free code signing provided by [SignPath.io](https://signpath.io), certificate by [SignPath Foundation](https://signpath.org).** This is a proposed attribution, not a claim of current sponsorship.

## Responsibilities

| Role | Responsible maintainer |
| --- | --- |
| Author / committer | [Andrew (@CoderBananna)](https://github.com/CoderBananna) |
| Reviewer of outside contributions | [Andrew (@CoderBananna)](https://github.com/CoderBananna) |
| Release and signing approver | [Andrew (@CoderBananna)](https://github.com/CoderBananna) |

Every outside contribution must be reviewed before inclusion in a release. Build scripts, dependency changes and workflows are part of that review. CODEOWNERS records the responsible maintainer; it does not by itself enforce branch protection.

All people with repository or signing access must use multi-factor authentication. This is a policy requirement; account settings must be verified before the application and again when access changes. Each signing request requires explicit manual approval by the designated approver. Approval of a source change or a green build alone is not approval to sign.

## Build and release policy

1. Build the reviewed source commit on a standard GitHub-hosted Windows runner using the [Windows build workflow](../.github/workflows/build.yml), the pinned SDK and checked-in dependency locks.
2. Require the core and Windows mode checks to pass. Complete applicable desktop Office, page-editor and printing checks before a release; the hosted build does not replace those checks.
3. Record the commit, version, SDK, executable metadata and SHA-256 hash. Manual builds retain evidence for one day; executable uploads require the optional manual input. Unsigned maintenance releases may be published while the documented licensing enquiries remain pending; this does not imply signing approval or a resolved legal interpretation. When enabled, the unsigned executable is retained as a GitHub Actions artifact for one day. Routine builds retain logs and a summary without a downloadable executable.
4. After SignPath acceptance and integration, submit that exact workflow artifact through SignPath's GitHub connector. Do not substitute a locally built executable. Restrict signing to the approved repository, workflow and release source, and to the product name **GoodSort PDF** with matching version metadata.
5. The approver reviews the source commit, checks, version and artifact before manually authorising each signing request in SignPath.
6. Verify the returned Authenticode signature, certificate, timestamp and product/version metadata before publishing. Record a new SHA-256 hash for the signed executable and publish its matching source and licence. Signing changes the executable hash.

The [proposed artifact configuration](signing/artifact-configuration.xml) covers only `GoodSort PDF.exe` inside the uploaded archive. For example, version `1.5.0` corresponds to file version `1.5.0.0`. Bundled third-party components retain their own licences and are not separately signed as GoodSort PDF.

The current workflow does **not** request signing, publish releases or contain signing credentials. Setup and approval in SignPath remain prerequisites.

## Privacy policy

GoodSort PDF itself does not transfer documents or usage information to other networked systems unless the user requests an action that does so, such as choosing a network printer, network folder or web link. See the [privacy policy](PRIVACY.md) for local storage, removal instructions and the separate behaviour of Microsoft Office, Windows, printers and cloud folders.

## References

- [SignPath Foundation terms](https://signpath.org/terms.html)
- [SignPath GitHub origin verification](https://docs.signpath.io/trusted-build-systems/github)
- [Application preparation and remaining checks](SIGNPATH-APPLICATION.md)
