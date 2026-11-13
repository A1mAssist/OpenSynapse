# Contributing to OpenSynapse

OpenSynapse controls real hardware. Keep changes small, traceable, and backed by tests or hardware evidence.

## Licensing

OpenSynapse is licensed under AGPL-3.0-only. By submitting a contribution, you agree that it is licensed under the same terms.

## Device support

If you own a device that needs new or additional support, read [docs/DEVICE_ADAPTATION.md](docs/DEVICE_ADAPTATION.md) and use the [device support issue form](https://github.com/A1mAssist/OpenSynapse/issues/new?template=device-support.yml). The top of the guide is a three-step owner checklist; the lower sections are maintainer reference.

### Protocol verification

Before changing a Razer capability, report, parser, or macro, lighting, battery, or mapping behavior, read the local extracted JS bundle for the device PID under `artifacts/razer-mw-full-cdn/bundles/<PID>/`. Check the device-level `main*.js` (`runtimeData`, `deviceUI`, `useFeature`), `onLoad*.js`, and the branch that loads any shared chunk. A shared chunk present in the bundle does not mean that device enables the capability. Record the PID, files, fields or branch checked, and whether real hardware verified the change.

## What belongs in Git

Commit only files needed to build, test, document, or license the project:

- source code and project files;
- device manifests and verified runtime configuration;
- documentation, localized screenshots, product artwork, and third-party license files;

The private test tree and device-specific fixtures are kept locally and are
not published in this repository.

Do not commit:

- `bin`, `obj`, `artifacts`, `dist`, `TestResults`, coverage output, installers, portable archives, NuGet packages, executables, DLLs, PDBs, or generated WinUI files;
- `.vs`, `.idea`, `.vscode`, user settings, caches, temporary files, dumps, logs, ETW traces, USB captures, or local diagnostics;
- Ghidra projects, extracted proprietary application files, reverse-engineering workspaces, or unreviewed protocol captures;
- API tokens, passwords, private keys, signing certificates, `.env` files, device paths, usernames, or absolute paths tied to one workstation;
- Razer binaries, drivers, firmware, or other proprietary files. These are external runtime prerequisites for some Blade Fn features, but users must install them from Razer or the device support package; they are not checked into or redistributed by this repository.
- repository-local build or release automation under `scripts/`; keep such scripts outside the repository.
- new user-visible strings hard-coded in C# or XAML. Add a stable English PascalCase key to both locale resource files and use `AppStrings.Text`, `AppStrings.Texts`, or `AppStrings.FormatText`; XAML fallback text is allowed only with `Localized.Uid`. Do not add new `AppStrings.Get("中文文案")` calls.

Release binaries belong on GitHub Releases, not in Git history.

## Screenshots and documentation

- Store README screenshots in `screenshots/`.
- Use explicit language suffixes such as `-zh.png` and `-en.png`.
- Remove personal information, notifications, account identifiers, device serial numbers, and unrelated windows before committing.
- Keep the English `README.md` and Chinese `README.zh-CN.md` aligned, while using screenshots from the matching language only.
- Reuse `src/OpenSynapse.App/Assets/OpenSynapseLogo.svg` as the canonical product logo.

## Before every commit

Run the smallest relevant tests, then run these repository checks from PowerShell:

```powershell
git status --short --untracked-files=all
git diff --check
dotnet build OpenSynapse.slnx -c Release
```

For application or packaging changes, also run:

```powershell
dotnet build src/OpenSynapse.App/OpenSynapse.App.csproj -c Release -p:Platform=x64
```

For every Velopack application or packaging change, also verify the deployed
executable with a non-application working directory (the way `Update.exe` can
restart it). The application entry point must set `Environment.CurrentDirectory`
to `AppContext.BaseDirectory` before WinUI or native initialization. A publish
directory smoke test alone is insufficient.

The machine-wide installer is the MSI built with `vpk 1.2.161` or newer using
`--msi --instLocation PerMachine --runtime win-x64`. It installs the app under
`Program Files` and registers the OpenSynapse service from the installed
`current\OpenSynapse.App.exe`. The per-user installer does not register the
service. For an MSI build, verify a silent install, the service
path and account, one UI toggle without a UAC prompt, and that uninstall removes
the service. Also upgrade a per-user install and verify that profiles and
settings survive, the old directory no longer contains `current`, and the HKCU
startup entry points to the Program Files executable. User data must stay in
`%LOCALAPPDATA%\A1mAssist\OpenSynapse`, not under the Velopack-managed
`%LOCALAPPDATA%\OpenSynapse` directory, because MSI cleanup removes the latter.
The patch script also makes same-version products eligible for
`RemoveExistingProducts` and guards the action with `NOT Installed`, so beta
builds sharing the numeric `ProductVersion` replace each other instead of
stacking and leaving a stale `MSI:OpenSynapse` ARP entry.

Velopack release assets must be generated as one set. Keep the generated feed
files (`*.nupkg`, `RELEASES`, `releases.win.json`, `assets.win.json`) unchanged.
The three user-facing installers are published under versioned names after
packing: `OpenSynapse-<version>-win-x64-Setup.msi`,
`OpenSynapse-<version>-win-AutoUpdate-PerUser.exe`, and
`OpenSynapse-<version>-win-Portable.zip`. The MSI is the recommended download;
label it as the auto-update/per-user package in the release notes. Before
uploading, compare every renamed installer with the file produced
by the same `vpk pack` invocation for size and SHA-256, keep the feed filenames
untouched, then launch the exact packaged executable from its deployed
`current` directory. See the release checklist in `README.md`.

### Test builds

A test build handed to a device owner for verification is always a pre-release.
It must never be a published release, and it must never be the latest release.
Keep local build output under `artifacts/test-packages/<build-name>/`; do not
create beta, test, or publish directories directly under `artifacts/`.

```powershell
gh release create <tag> <testbuild.zip> --draft --title "<title>" --notes "<notes>"
# when the issue reporter needs a download, publish it as a pre-release:
gh release edit <tag> --draft=false --prerelease
```

Do not attach the per-user installer, `*.nupkg`, or anything produced by `vpk pack` to a test
build: those assets feed the Velopack update channel, and a user would silently
auto-update onto an unverified build.

GitHub issue comments cannot host file uploads, so the test build has to live in
a release asset and the issue links to it. Give the tag an issue-specific name
such as `issue4-test-20261020`, keep the build self-contained so no reporter has
to install .NET, and verify the download URL returns HTTP 200 and the expected
content length before posting it. A draft release answers 404 for everyone but
collaborators, so publish it as a pre-release instead.

State plainly in the issue which parts are verified. A build that compiles and a
build that passed tests are not evidence that the reporter's hardware behaves
correctly, and saying so is what makes the report trustworthy.

Review the staged file list before committing:

```powershell
git diff --cached --name-status
```

If the list contains a generated binary, capture, log, secret, local tool directory, or unrelated change, remove it from the commit. Never use `git add .` without reviewing the result.

## Commit rules

- One commit should describe one coherent change.
- Use an imperative, specific subject such as `fix: preserve Fn key release state`.
- Do not commit an unverified hardware write as production-ready.
- Do not publish a test build as a release; see [Test builds](#test-builds).
- Do not silently ignore unknown protocol fields or unsupported device identifiers.
- Keep `DESIGN_SPECIFICATION.md` resource-key rules and both `Resources.resw` files synchronized with localization changes.
- Preserve unrelated user changes in a dirty worktree.
- Do not rewrite public history or move a published version tag unless correcting a confirmed release error.
- Commit as the repository owner's own Git identity (`A1mAssist <196530767+A1mAssist@users.noreply.github.com>`). Never invent a project author such as "OpenSynapse Dev": an address that maps to no GitHub account still shows up as a separate contributor with a default avatar.
- Never add a `Co-Authored-By` trailer for the assistant or any tooling. It registers a second contributor that does not belong to the project.
