---
name: churchpresenter-glossary
description: Shared vocabulary for ChurchPresenter Windows and macOS apps — product systems, runtime pipeline, output model, and platform roles. Use when explaining, implementing, reviewing, or debugging native app work under apps/windows or apps/macos, or when the user asks what a term means.
---

# ChurchPresenter Glossary

Use this skill to keep explanations and status updates aligned on **what system** is being discussed. Terms are shared across Windows and macOS unless marked **(Windows only)** or **(macOS only)**.

## When to apply

- Any coding, design, review, or debugging task under `apps/windows/**` or `apps/macos/**`.
- Explaining architecture, output behavior, live state, content, or platform boundaries to the operator.
- Writing status updates — name the **workflow area**, **system**, and **glossary term** before file or API names.

## How to use

1. Read [glossary.md](glossary.md) when the task touches a system you will reference by name.
2. Prefer glossary terms over synonyms. If docs or code use a different label, map it to the glossary term once.
3. Ground explanations in **where in the app** (workflow area) + **which system** (glossary section) + **what changed**.
4. For platform-only concepts, use the platform term from the glossary and note the counterpart on the other platform when one exists.
5. Do not invent new product terms for concepts already defined here. If a gap appears, note it and use the closest glossary entry.

## Explanation pattern

```text
[Workflow area] → [System] → [Term]: plain-language statement of what is happening and why.
```

Example: *Show → Output system → Slide layer: taking this slide live puts its prepared content into the slide output slot; media on the media layer keeps playing.*

## Canonical sources

Glossary entries derive from:

- `docs/architecture/` (target architecture, backend, content, native hosts)
- `docs/reference/propresenter/` (ProPresenter behavior reference)

When behavior is ambiguous, prefer architecture docs over informal code names.

## Related rule

The project rule `.cursor/rules/native-app-plain-language.mdc` requires contextual, plain-language communication. This skill supplies the **shared vocabulary** that rule expects.
