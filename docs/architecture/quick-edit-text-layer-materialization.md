# Quick Edit Text Layer Materialization

This document defines the platform-agnostic architecture for quick text edits during live operation.

## Purpose

Quick Edit lets an operator fix text quickly without entering a full editor workflow. It should be a document mutation workflow, not a renderer shortcut.

## Architecture Rule

Quick Edit operates through the same document services, validation, resource stamps, scene invalidation, and live-edit rules as the full editor.

The native host may provide an inline flyout, popover, sheet, or inspector, but it should only collect operator intent and display a transient preview. It should not create renderer-only text objects or bypass document services.

## Flow

1. The host requests a quick-edit draft for a selected slide/text target.
2. The application runtime materializes editable text fields from the document model and theme context.
3. The host renders the draft and accepts operator edits.
4. Commit writes through document services.
5. The runtime invalidates future prepared cues, thumbnails, scene snapshots, and diagnostics using resource stamps.
6. Current live output remains stable unless the operator explicitly retakes or replaces the live payload.

## Live-State Rule

Quick Edit changes future content. It does not imply slide activation, clear, retake, or output-frame replacement.

If the edited slide is currently live, the runtime may mark the live payload as older than the document and offer a retake/update command. It should not silently mutate the live frame in place unless that behavior is explicitly modeled as a command.

## Rendering Rule

Quick Edit previews may clone scene snapshots with draft text for operator feedback. Draft preview state is host-local and transient.

Committed text changes update the portable presentation document. Scene compilation then produces new immutable scene versions for thumbnails, previews, editor, and future live takes.

## Testing Boundary

Shared tests should cover draft creation, commit validation, document mutation, cache invalidation, current-live stability, and diagnostics when a target is unsupported.

Native tests should cover the platform-specific inline editing surface, focus behavior, keyboard behavior, and accessibility.
