---
name: architecture-doc-refresh
description: Refresh ChurchPresenter architecture docs after native Windows or macOS feature work. Use when a hook or user asks to distill recent app changes into docs/architecture while preserving ProPresenter reference docs as read-only behavioral sources.
---

# Architecture Doc Refresh

Use this skill after native app feature work when the architecture docs may need to capture durable design decisions, runtime contracts, platform adapter guidance, or cross-platform implementation implications.

## Continual-Learning Pattern

Follow the continual-learning strategy: keep the parent orchestration light and delegate the actual documentation pass to one focused subagent.

The parent should:

1. Read this skill.
2. Launch exactly one subagent for the docs refresh.
3. Provide the subagent with the hook-detected changed app paths and the current user goal.
4. Return the subagent result.

Do not mine transcripts or perform the architecture-doc editing in the parent flow when this skill is invoked from the hook.

## Subagent Task

Ask the subagent to:

1. Review the current session context and git diff for the implemented feature.
2. Read the relevant shared architecture docs under `docs/architecture/`.
3. Read the relevant platform docs under `docs/architecture/platforms/`.
4. Read the relevant ProPresenter reference docs under `docs/reference/propresenter/` for behavior alignment.
5. Update only the relevant architecture docs when the implementation introduces durable architecture, runtime, data-model, adapter, or cross-platform decisions.
6. Leave `docs/reference/propresenter/**` unchanged.
7. Report if the architecture docs are already accurate and no edit is needed.

## Documentation Boundaries

- Shared product/runtime decisions belong in `docs/architecture/`.
- Windows WinUI / Windows App SDK implementation guidance belongs in `docs/architecture/platforms/winui-native-app.md` or `docs/architecture/platforms/winui-code-organization.md`.
- macOS SwiftUI / AppKit / AVFoundation implementation guidance belongs in `docs/architecture/platforms/macos-native-app.md` or `docs/architecture/platforms/macos-code-organization.md`.
- ProPresenter behavior references under `docs/reference/propresenter/**` are read-only for this workflow.
- Keep docs architectural and implementation-oriented. Do not write a session log, changelog, or raw diff summary.

## Product Alignment

Preserve these invariants:

- Native hosts express intent; the shared application/runtime owns production truth.
- ProPresenter reference docs define behavior expectations, but ChurchPresenter architecture docs define maintainable native implementation.
- Windows and macOS guidance should stay aligned when the feature affects both native apps, even if code currently changed only one host.
- If ProPresenter behavior is unclear from the reference docs, ask the user for clarification instead of inventing behavior.

## Platform Documentation Sources

- Use Microsoft Learn MCP tools as needed for Windows, WinUI, Windows App SDK, .NET, or C# implementation details.
- Use Apple/Sosumi MCP tools as needed for macOS, SwiftUI, AppKit, AVFoundation, Core Animation, sandboxing, signing, or Apple platform details.
- For docs-only edits, no WinUI build is required.

## Validation

Before finishing, verify:

- No files under `docs/reference/propresenter/**` changed.
- Edits are limited to relevant architecture docs.
- The final response states whether docs were updated or were already aligned.
