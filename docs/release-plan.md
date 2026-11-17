# Issue 5: release plan and verification

Objective: provide a GitHub release archive that can be installed without an
installer or administrator rights into
`%APPDATA%\Autodesk\ApplicationPlugins\ACadSmartLeader.bundle`, with automatic
loading in AutoCAD 2020–2024. Work is confined to PR #6, branch
`issue-5-ee54ffcc0e3e`.

## Requirements and work sequence

Issue #5, its complete comments, its sub-issue list, and all three PR #6 comment
types were read; no additional comments or required sub-issues were present.
The most recent merged PR #4 and TeplovPrint's release workflow, packager,
manifest and manual installation instructions were reviewed.

| Requirement | Implementation | Verification |
| --- | --- | --- |
| AutoCAD 2020–2024 | Separate 2020 build against SDK 23.1/.NET 4.7, retain 2021–2024 SDK 24.0/.NET 4.8 | Compile all targets in Windows CI; inspect assembly references; assert manifest routing for each year |
| User profile installation without installer/admin | Root `ACadSmartLeader.bundle` and Russian `INSTALL.md` in a named manual ZIP | Validate archive paths, referenced DLL/menu/JSON/notices, installation/update/removal instructions |
| Automatic loading | Win64 release ranges R23.1, R24.0–R24.3; startup/command registration | Manifest regression checks; licensed CAD runtime check remains separately identified |
| Files in every release | Versioned manual ZIP, SHA256 manifest, GitHub release job gated by successful tests/build/package | CI artifact verification and workflow review; publication runs after merging/version bump or matching tag |
| Preserve existing features | Retain 2021–2024 and 2025+ builds, embedded dependencies, CUIX, rules/schema, all commands | Existing Core/WPF tests and isolated DLL checks for all three assemblies |

1. Implement all build, manifest, packaging, release workflow and documentation changes.
2. Run focused Python regression checks locally; leave Windows compilation and
   existing integration tests to CI to conserve container memory.
3. Inspect actual current-commit CI logs/artifacts and the complete PR diff;
   update PR title/body, commit/push all changes and mark PR ready.

## Research and assumptions

- [Autodesk's compatibility table](https://help.autodesk.com/cloudhelp/2025/ENU/AutoCAD-Customization/files/GUID-A6C680F2-DE2E-418A-A182-E4884073338A.htm)
  assigns SDK 23.1/.NET 4.7 to 2020, SDK 24.0/.NET 4.8 to 2021 and SDK 25/.NET 8
  to 2025. Merely lowering `SeriesMin` on the old 2021 DLL would not add 2020 support.
- [Autodesk's package installation guide](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-Customization/files/GUID-5E50A846-C80B-4FFD-8DD3-C20B22098008.htm)
  supports manual copying to the user ApplicationPlugins folder and startup discovery.
- [RuntimeRequirements](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-Customization/files/GUID-1591CA01-EF87-48CD-952B-772FE26037F1.htm)
  selects components by release. Keep the existing 2025+ range while adding 2020
  separately so a host discovers exactly one DLL and matching menu.
- The reference project also targets nanoCAD and supplies convenience copy
  scripts. Issue #5 requests AutoCAD and manual installation; copying a complete
  bundle is sufficient and needs no script or installer.
- No release is published from a PR branch. Publishing the prepared next release
  belongs to the post-merge release workflow; the PR supplies the identical ZIP
  as a CI artifact for review.
- This Linux environment and hosted CI do not contain licensed AutoCAD. Assembly
  and package checks cannot prove actual CAD startup loading. Runtime checks must
  be reported accurately; earlier issue #3 CAD acceptance remains unchanged.

## Progress

- [x] Complete issue/PR/context review; upstream main is already an ancestor.
- [x] Implement all requirements and regression checks.
- [ ] Focused local checks and current-commit CI/artifact verification.
- [ ] Final diff review, clean working tree, PR metadata and ready state.
- [ ] Licensed AutoCAD 2020–2024 startup loading (requires an external CAD host).

## Verification evidence

- The new regression failed against the original manifest: `package-before.log`
  lines 5–14 report the missing 2020 component. The same five Python tests and
  standalone manifest check pass after the change (`ci-logs/package-after.log`).
- Actionlint 1.7.12 validates the release workflow without findings.
- Runs [44067973474](https://github.com/veb86/ACadSmartLeader/actions/runs/44067973474)
  and [44067963566](https://github.com/konard/veb86-ACadSmartLeader/actions/runs/44067963566)
  are for commit `c1ac4d2`, after its 2026-11-17 10:46:45 UTC commit time. All
  three plugin targets compiled with zero warnings/errors and all 13 Core tests
  passed. The new SDK-reference check failed (`ci-logs/build-44067973474.log`
  lines 227–229; fork log lines 211–213) because it matched assembly names with
  case-sensitive strings. The SDK DLL is named `AcMgd.dll` but its identity is
  `acmgd`. Match identities case-insensitively and include all references in
  failure diagnostics; retain the strict expected API version check.
