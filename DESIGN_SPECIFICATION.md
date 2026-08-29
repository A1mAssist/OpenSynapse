# OpenSynapse Design Specification

## Scope

OpenSynapse is a lightweight Windows 11 controller for Razer devices. The
application layer owns presentation state and orchestration; device protocol
and transport code remains in `OpenSynapse.Windows`, and portable models and
profile persistence remain in `OpenSynapse.Core`.

`MainViewModel` is a UI aggregate. Device operations are kept in partial
files grouped by workflow (for example, shortcut-cycle persistence in
`MainViewModel.Shortcuts.cs`). A partial file may share the aggregate's
private state, but it must contain one cohesive workflow and must not create a
second source of truth.

## Resource-key rules

1. User-visible text in C# and XAML code must use a resource key. Protocol
   names, enum members, device identifiers, and diagnostic category IDs are
   technical identifiers and remain invariant.
2. New keys are stable English `PascalCase` names. A key is an identifier, not
   a sentence, and must not change when its translation changes. Existing
   `Text_XXXXXXXX` hash keys are migration-compatibility records; do not add
   new hash keys.
3. Every key must be present in both
   `src/OpenSynapse.App/Strings/en-US/Resources.resw` and
   `src/OpenSynapse.App/Strings/zh-CN/Resources.resw`.
4. Use `AppStrings.Text("Key")` for one value,
   `AppStrings.Texts("KeyA", "KeyB")` for option lists, and
   `AppStrings.FormatText("Key", args...)` for formatted text. Do not add new
   `AppStrings.Get("中文文案")` calls; the legacy hash lookup exists only for
   migration compatibility. XAML fallback text is allowed only when paired
   with `Localized.Uid`, so the generated resource key remains the runtime
   source of truth.
5. Keep format placeholders identical across locales and preserve their
   ordering. Do not concatenate translated fragments when a formatted resource
   can express the complete sentence.
6. Status and enum display text is resolved at the ViewModel boundary. Core and
   Windows layers return invariant values or typed results, not localized UI
   strings.
7. A resource change is complete only when the two locale files, the calling
   code, and the relevant automated check are committed together.

## Review gate

Before merging:

- scan application source for user-visible CJK string literals;
- verify the key sets of both `.resw` files are equal;
- run the Core/Windows test project;
- build the x64 Release application with
  `Platform=x64` and `RuntimeIdentifier=win-x64`;
- review the staged file list and exclude generated output, captures, local
  tools, secrets, and release bundles.
