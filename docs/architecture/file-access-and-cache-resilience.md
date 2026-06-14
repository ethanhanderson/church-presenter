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
- diagnostics snapshots,
- prepared cue snapshots (one per `(ContentRef, LayerKind, RoutingHash)`),
- decoded resource handles (textures, surfaces, opened media items, audio buffers) that back active or imminent prepared cues.

Each cache entry needs owner, source resource stamp, cache schema version, invalidation policy, rebuild path, and a size cost so tiered budgets can evict deterministically.

## Tiered Cache Model

The unified output pipeline depends on a single tiered cache contract owned by the application layer. Three tiers exist, with explicit promotion/eviction rules:

- **Hot (memory):** decoded surfaces, opened media items/players, prepared cue snapshots, scene graphs ready for the host adapter. Tier budget is configured per profile and enforced by size cost; eviction prefers the largest non-active entry whose owning cue is neither live nor a high-confidence look-ahead.
- **Warm (disk):** decoded thumbnails, posters, pre-roll segments, scene caches, compiled fonts, integration response caches. Tier budget is enforced by disk quota with LRU eviction; entries are keyed by resource stamp + cache schema version.
- **Cold (origin):** the original portable content path or provider response. Always authoritative.

The runtime asks the pipeline for cues by `ContentRef`. The cache resolves the highest tier with a current stamp; misses cascade through warm to cold and re-populate higher tiers as resources allow. Re-population must never block the active frame composer thread.

### Performance Budgets

Budgets are platform-tuned but defined as application-layer contracts so behaviour stays portable:

- Take-live latency for a warm prepared cue: visually instant (target under ~50 ms from command enqueue to first composed frame on the active surface).
- Take-live latency for a cold prepared cue: bounded; the host shows the prior payload until the new cue's first usable frame is gated as ready (see `backend-application.md` Playback Coordination).
- Loop boundary gap: indistinguishable from a normal frame interval; achieved via host gapless playback constructs, not seek-to-zero.
- Snapshot diff application: per-slot work only; unaffected layers must not re-render or restart playback.

The runtime records `prepareTimeMs`, `routeTimeMs`, `composeTimeMs`, `applyTimeMs`, `mediaReadyTimeMs`, `loopGapMs`, and cache hit tier per action so operators and tests can observe regressions.

### Cache Coherence

- Every cache entry stores the resource stamp that produced it. Stamps include content version, theme version (when applicable), provider revision, and any override hash that influenced decoding.
- A `ContentInvalidated` signal (see `content-management.md`) marks affected entries stale and schedules re-preparation if the cue is live or hinted.
- Stale entries are evicted only after the replacement is ready, so live frames never collapse to a placeholder during a benign source edit.

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
