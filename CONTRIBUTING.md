# Contributing to OpenSynapse

OpenSynapse controls real hardware. Keep changes small, traceable, and backed by tests or hardware evidence.

## What belongs in Git

Commit only files needed to build, test, document, or license the project:

- source code and project files;
- tests and deterministic test data;
- device manifests and verified runtime configuration;
- documentation, localized screenshots, product artwork, and third-party license files;
- release/build scripts that contain no credentials or machine-specific paths.

Do not commit:

- `bin`, `obj`, `artifacts`, `dist`, `TestResults`, coverage output, installers, portable archives, NuGet packages, executables, DLLs, PDBs, or generated WinUI files;
- `.vs`, `.idea`, `.vscode`, user settings, caches, temporary files, dumps, logs, ETW traces, USB captures, or local diagnostics;
- Ghidra projects, extracted proprietary application files, reverse-engineering workspaces, or unreviewed protocol captures;
- API tokens, passwords, private keys, signing certificates, `.env` files, device paths, usernames, or absolute paths tied to one workstation;
- Razer binaries, drivers, firmware, or other proprietary files that the project is not licensed to redistribute.

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
dotnet test OpenSynapse.slnx -c Release
```

For application or packaging changes, also run:

```powershell
dotnet build src/OpenSynapse.App/OpenSynapse.App.csproj -c Release -p:Platform=x64
```

Review the staged file list before committing:

```powershell
git diff --cached --name-status
```

If the list contains a generated binary, capture, log, secret, local tool directory, or unrelated change, remove it from the commit. Never use `git add .` without reviewing the result.

## Commit rules

- One commit should describe one coherent change.
- Use an imperative, specific subject such as `fix: preserve Fn key release state`.
- Do not commit an unverified hardware write as production-ready.
- Do not silently ignore unknown protocol fields or unsupported device identifiers.
- Preserve unrelated user changes in a dirty worktree.
- Do not rewrite public history or move a published version tag unless correcting a confirmed release error.
