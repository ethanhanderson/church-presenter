# File Access and Cache Resilience

This document defines platform-agnostic rules for file access, resource identity, caches, stale data, and recovery. These rules apply to native Windows and macOS apps through platform-specific storage and security adapters.

## Core Rule

Portable content is the source of truth. Caches are accelerators. Machine-local bindings adapt portable content to one computer.

No cache, thumbnail, extracted bundle file, media preview, or platform-specific file bookmark should become authoritative content.

## Ownership Boundaries

The application runtime owns persistence orchestration, content health, cache invalidation, diagnostics, and recovery decisions.

Native hosts surface operator intent, apply diagnostics, and provide platform file-access adapters. They should not traverse the content graph directly or decide cache validity independently.

## Resource Identity

Every content resource should have stable identity and a resource stamp:

- document id and version,
- asset id,
- original source identity,
- resolved path or provider reference,
- storage policy,
- dependency type,
- last known size/time/hash where available,
- package/sync membership,
- missing/relinked state.

Resource stamps should feed thumbnails, previews, scene compilation, prepared cues, media relink, package export, sync, and audit.

## Cache Types

Caches may include:

- thumbnails,
- scene snapshots,
- extracted bundle resources,
- media posters/previews,
- font/resource resolution caches,
- integration response caches,
- search indexes,
- diagnostics snapshots.

Each cache entry needs owner, source resource stamp, cache schema version, invalidation policy, and rebuild path.

## Failure Categories

File and resource failures should be classified as:

- missing,
- moved,
- permission denied,
- locked/in use,
- corrupt,
- unsupported format,
- provider offline,
- credential expired,
- stale cache,
- package conflict,
- machine-binding mismatch.

Diagnostics should identify whether the issue affects content truth, preview quality, live playback, package export, sync, or only a cache.

## Recovery

Recovery actions should be explicit commands or application-service operations:

- relink asset,
- choose replacement,
- rebuild cache,
- refresh provider data,
- re-extract bundle resources,
- skip unavailable asset,
- package missing-dependency preview,
- repair manifest/reference,
- clear stale prepared cue,
- reset machine-local binding.

Native hosts provide file pickers, permission prompts, security-scoped access, and platform storage UX, but the runtime decides which resource and graph references are repaired.

## Live Operation Rules

A missing future asset should not tear down the current live frame. It should mark future cues/previews/diagnostics as degraded until repaired.

A missing currently live asset should report layer/player diagnostics and offer recovery without mutating selection state or deleting content references.

Live state, prepared cues, thumbnails, and scene caches must invalidate from resource stamps rather than direct path strings.

## Testing Boundary

Shared tests should cover resource stamps, stale cache invalidation, missing/corrupt/locked resources, relink graph updates, package missing-dependency previews, and recovery diagnostics.

Native tests should cover platform file pickers, storage permissions, security prompts, endpoint-specific resource access, and host presentation of recovery actions.
