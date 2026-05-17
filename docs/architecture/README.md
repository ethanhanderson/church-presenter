# ChurchPresenter Architecture

This folder defines the platform-agnostic architecture for ChurchPresenter's native desktop apps. It translates the ProPresenter reference set in `docs/reference/propresenter/` into shared models, runtime boundaries, rendering systems, persistence rules, and diagnostics that Windows and macOS hosts should implement through their native frameworks.

The architecture docs are not platform implementation guides. They describe the common product architecture that both native apps must follow. Platform-specific choices for WinUI, Windows App SDK, AppKit, SwiftUI, AVFoundation, Core Animation, packaging, and OS integration belong in separate native app docs.

## Architecture Map

- [`target-architecture.md`](target-architecture.md) - the complete platform-agnostic product architecture: feature models, runtime flow, output system, support systems, diagnostics, and parity rules.
- [`backend-application.md`](backend-application.md) - the shared application runtime: command pipeline, live session model, domain services, query surfaces, and recovery boundaries.
- [`content-management.md`](content-management.md) - content, media, generated content, support files, package, sync, migration, and audit architecture.
- [`rendering-engine-replacement.md`](rendering-engine-replacement.md) - host-neutral rendering, output composition, scene compilation, stage rendering, capture/stream consumers, and host adapters.
- [`native-hosts.md`](native-hosts.md) - shared responsibilities for native desktop shells, output hosts, editor hosts, settings, diagnostics, and platform adapters.
- [`file-access-and-cache-resilience.md`](file-access-and-cache-resilience.md) - file-access, resource stamp, cache, and stale/missing resource resilience rules.

## Platform Guides

These platform docs are subordinate to the shared architecture above. They describe native implementation choices and code organization for each host:

- [`platforms/winui-native-app.md`](platforms/winui-native-app.md) - Windows WinUI implementation guidance.
- [`platforms/winui-code-organization.md`](platforms/winui-code-organization.md) - Windows project, feature, adapter, and test organization.
- [`platforms/macos-native-app.md`](platforms/macos-native-app.md) - macOS native implementation guidance.
- [`platforms/macos-code-organization.md`](platforms/macos-code-organization.md) - macOS package, feature, adapter, and test organization.

## Source Relationship

Use the reference docs as the product behavior source:

- `docs/reference/propresenter/features.md` defines the feature map and six product models: content, cues, show, live state, output, and control.
- `docs/reference/propresenter/features/*.md` define current ProPresenter behavior by feature family.
- `docs/reference/propresenter/feature-inventory.md` maps behavior to ChurchPresenter capability targets and dependencies.
- `docs/reference/propresenter/output-system/`, `media-management/`, `library-show-management/`, and `operator-workflows/` provide deeper behavior notes.

The architecture docs define how native desktop apps should be built to reproduce those behaviors without coupling the shared model to one operating system or UI toolkit.

## Architectural Rule

The native shell expresses intent. The shared application runtime owns production truth.

That rule applies everywhere:

- Editor changes mutate portable document models through document services.
- Slide clicks, media triggers, timers, macros, remotes, devices, and keyboard shortcuts submit commands.
- The runtime resolves live state, output layers, Looks, stage layouts, media players, capture, diagnostics, and recovery.
- Native hosts apply resolved frames, expose platform UI, report health, and provide OS integrations.

## Platform Boundary

Shared architecture should define product domain models, command and query contracts, document/package schemas, render-frame and scene contracts, output routing semantics, diagnostics and recovery semantics, and portability boundaries.

Platform docs should define native UI composition, OS lifecycle, window/display APIs, media backend bindings, hardware and network integrations, packaging/install/deployment, and platform-specific testing and accessibility.

## Documentation Maintenance

When a ProPresenter feature is researched or added:

1. Document the current product behavior in `docs/reference/propresenter/features/*.md`.
2. Add or update shared architecture contracts in this folder.
3. Add platform-specific implementation guidance in the native app docs.
4. Keep source limitations in `docs/reference/propresenter/output-system/sources.md` or `feature-inventory.md`.
