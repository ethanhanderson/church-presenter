# ChurchPresenter Term Glossary

Terms are **shared** unless marked **(Windows only)** or **(macOS only)**. Platform rows name the native implementation role, not a different product concept.

---

## App layers (codebase structure)

| Term | Meaning |
|---|---|
| **Native host** | The platform-specific desktop app shell: windows, pages, dialogs, gestures, output windows, and platform adapters. Expresses operator intent; does not own live production truth. |
| **Application runtime** | Shared production engine: commands, live session, layer routing, frame composition, playback coordination, diagnostics, recovery. Owns what is live and how it routes. **(Windows)** largely `ChurchPresenter.Application`. **(macOS)** planned equivalent runtime package. |
| **Portable content layer** | Documents, libraries, playlists, themes, media metadata, support files, and package schemas — church-portable data independent of one machine. **(Windows)** `ChurchPresenter.Core` + content services. |
| **Machine binding layer** | Monitor bindings, endpoint bindings, device ids, credentials, local caches, recents, activation — stays on one computer. |
| **Host adapter** | Narrow bridge that applies resolved frames and players to native surfaces (`ApplyFrame`, `EnsureSurfaces`, `BindPlayer`, health reporting). Same contract on both platforms; different underlying APIs. |
| **Document service** | Service boundary for reading/writing presentation and support documents. Editor and Reflow commits go through here, not ad hoc UI serialization. |

---

## Workflow areas (operator surfaces)

| Term | Meaning |
|---|---|
| **Show** | Live operation surface: playlist, slide deck, take-live, clears, media/audio drawers, transport, Show Controls, previews, output health. |
| **Editor** | Slide and object authoring: canvas editing, notes, actions, validation, live-edit-safe saves. |
| **Reflow** | Text-first editing over groups and arrangements; commits preserve slide ids and provenance where possible. |
| **Themes** | Reusable visual templates and variants; separate from output Looks. |
| **Output Setup** | Logical screens, endpoint bindings, Looks, stage layouts, capture/stream configuration. |
| **Settings / Support** | Integrations, sync/migration, packages, diagnostics, repair, machine vs portable scope. |

---

## Six product models

Independent but composable systems that describe the whole product.

| Term | Meaning |
|---|---|
| **Content model** | What exists: libraries, presentations, slides, groups, arrangements, themes, generated/imported items, media assets, support files. |
| **Cue model** | What can be triggered: media/audio cues, slide actions, timers, messages, props, announcements, masks, macros, capture commands, device actions. |
| **Show model** | What the operator selects and runs: service playlists, templates, headers, placeholders, plan links, arrangement choice, show-view projections. |
| **Live-state model** | What is currently running: active layer payloads, players, timers, messages, props, Look, stage layout, capture, endpoint health. |
| **Output model** | What gets rendered and where: screens, layers, Looks, stage layouts, masks, endpoints, transports. |
| **Control model** | How intent enters the runtime: UI clicks, keyboard, remotes, APIs, macros, markers, schedules, hardware controllers. |

---

## Content and library terms

| Term | Meaning |
|---|---|
| **Library** | Owns presentations. The canonical home for presentation documents. |
| **Presentation** | A document with slides, groups, arrangements, theme refs, actions, timelines, and resource refs. Owned by one library. |
| **Slide** | One screen of content in a presentation — scene objects, notes, actions, transitions. Content, not live state until taken live. |
| **Group** | Named section within a presentation (e.g. verse, chorus). Used in arrangements and Reflow. |
| **Arrangement** | Ordered subset or reordering of groups/slides for a specific service use. |
| **Theme** | Reusable visual style for slide content (fonts, colors, layouts). Applied to presentations; distinct from a **Look**. |
| **Playlist** | Ordered list of **occurrence references** to library-owned presentations (and other show items). Not a copy of the presentation. |
| **Playlist occurrence** | One entry in a playlist pointing at a presentation. Duplicates allowed; remove affects only that occurrence. |
| **Playlist template** | Structural skeleton for a service before all content exists. |
| **Provenance** | Record of where generated/imported content came from and how it may be refreshed, merged, or reported. |
| **Content graph** | The linked model of libraries, presentations, playlists, themes, assets, and references. |
| **Content change bus** | Publishes invalidation events when documents or assets change so caches and live payloads can refresh. |
| **Portable content** | Data meant to move between machines: presentations, playlists, themes, support files, packages. |
| **Machine-local binding** | Data tied to one computer: display ids, window positions, credentials, caches, device selections. |

---

## Media and cue terms

| Term | Meaning |
|---|---|
| **Media asset** | Durable identity for a media file: path, metadata, availability, relink state. One file, one asset record. |
| **Audio asset** | Same as media asset but for audio-only content and audio routing. |
| **Media cue** | A usage of a media asset with playback overrides (in/out, loop, role, scaling, transition). Many cues can reference one asset. |
| **Audio cue** | A usage of an audio asset with routing, gain, loop, and category (music/effect/voiceover). |
| **Media Bin** | Cue library for visual media triggers — not a raw file browser. |
| **Audio Bin** | Cue library for audio triggers. |
| **Media drawer** | Show UI catalog of available media assets. Cards appear from manifest data immediately; thumbnails are progressive adorners. |
| **Slide action** | Action attached to a slide (or slide trigger) that may take slides live, fire media/audio, run macros, change Looks, etc. |
| **Live video input** | Camera or capture device treated as a cue source on the live video layer. |
| **Playback marker** | Timeline point on media that can fire actions. |
| **Resource stamp** | Version fingerprint of resolved bytes/decoded resources used to detect stale caches and cue replacements. |

---

## Runtime and command terms

| Term | Meaning |
|---|---|
| **Live command** | The only boundary for changing live production state. Carries kind, source, target scope, correlation id. |
| **Command bus** | Single serial queue that executes live commands in order against the latest live session snapshot. |
| **Command source** | Origin of intent: UI, keyboard, remote, macro, marker, schedule, API, etc. |
| **Action batch** | Normalized set of runtime actions produced from one command (e.g. take slide + start timer + switch Look). |
| **Live session snapshot** | Immutable picture of all live production state at one moment: layer slots, players, Look, stage layout, selection vs live distinction, diagnostics. |
| **Selection** | Operator cursor/context (selected slide, playlist item, editor focus). **Not** the same as live state. |
| **Layer router** | The only writer of audience layer slots in the live session snapshot. Applies prepared cues and clears per layer. |
| **Prepared cue** | Immutable, fully resolved payload ready for a layer slot — content, theme, scene, playback descriptor, resource stamp. |
| **Cue preparation** | Background work that resolves content, theme, resources, and scene into a prepared cue. |
| **Prepared cue cache** | Memory-budgeted store of prepared cues, warmed by selection, hover, drawer open, look-ahead. |
| **Cue request** | Input to preparation: layer kind, content ref, overrides, routing hints, transition. |
| **Replace layer payload** | Runtime action that swaps a live slot's prepared cue when underlying content changed, without restarting other layers. |
| **Clear override** | Runtime state remembering that a layer was cleared until new content replaces it. |
| **Prior payload** | Previous live content kept for optional recovery after a clear. |

---

## Output system terms

Four separate decisions (from ProPresenter mental model):

1. What content is live?
2. Which **layer** does it occupy?
3. Which **screen** should render it?
4. Which **endpoint** delivers that screen?

| Term | Meaning |
|---|---|
| **Output screen** (logical screen) | Internal render target — Main, Stream, Lobby, Confidence, etc. "One render out" before it hits hardware. |
| **Output endpoint** | Delivery target: local display, placeholder, NDI, SDI, Syphon **(macOS)**, capture consumer, stream. |
| **Screen mapping** | Which endpoint(s) receive a logical screen. |
| **Audience screen** | Crowd- or program-facing logical screen. Routing controlled by **Looks**. |
| **Stage screen** | Performer/operator confidence display. Routing controlled by **stage layouts**, not Looks. |
| **Layer** | Fixed compositing slot in the output stack. Not user-defined arbitrary layers. |
| **Layer kind** | Identifies which slot: Audio, Messages, Props, Announcements, Slide, Media, Live Video, Mask. |
| **Layer slot** | One layer's live state on the runtime side: current prepared cue, transition, clear state, prior payload. |
| **Look** (Look preset) | Saved routing matrix for audience screens: which layers appear on which screens, optional per-screen theme variants. Reroutes; does not change slide content. |
| **Stage layout** | Dashboard composition for a stage screen: current/next text, timers, clocks, notes, previews, stage messages. |
| **Stage data provider** | Binding that supplies live data to a stage layout element (e.g. current slide text, timer value). |
| **Mask** | Compositing constraint that clips or shapes output on a screen — not ordinary content. |
| **Screen fallback** | Background color/surface below all layers (usually black on local displays). Shown when layers clear. |
| **Clear group** | Named set of layers cleared together (e.g. Clear All). Configurable support record. |
| **Clear layer** | Remove live content from one layer slot without affecting others. |
| **Transport lane** | Separate playback control context: presentation/media files, audio files, or announcements. Each has its own transport snapshot. |
| **Transport snapshot** | Read model for one lane: cue name, playing state, position, duration, markers. Host controls bind here, not to raw platform players. |

### Fixed output layers (canonical order for composition)

| Layer | Role |
|---|---|
| **Audio** | Audio playback and routing; participates in clears/transport though not visually stacked like slide/media. |
| **Messages** | Short operator-triggered audience text overlays (alerts, countdowns). |
| **Props** | Persistent reusable overlays; show/hide/toggle. |
| **Announcements** | Separate presentation lane (e.g. lobby loop) running alongside main slide content. |
| **Slide** | Main presentation slide content: lyrics, scripture, text objects. Slide/presentation background colors live here. |
| **Media** | Media bin or slide-attached media actions (images/video). |
| **Live video** | Camera or live input on the audience stack. |
| **Mask** | Per-screen clip/shape constraint applied after layer ordering. |

**Background color rule:** Slide/presentation background sits above the media layer in the stack. Enabled background color can cover background media; foreground media still shows. The composer may warn when this hides live media.

---

## Pipeline terms (unified output path)

| Term | Meaning |
|---|---|
| **Unified output pipeline** | Single path for all live content types: command → prepare → route → compose → host apply → endpoint. |
| **Frame composer** | Builds immutable audience and stage frame sets from the live session snapshot and Look routing. |
| **Audience frame set** | Resolved frames for all audience screens — one frame per screen with ordered layer payloads. |
| **Stage frame set** | Resolved frames for all stage screens from layout providers. |
| **Scene compiler** | Turns presentation/theme/tokens into a host-neutral scene graph (text, shapes, images, media nodes). |
| **Scene snapshot** | Immutable compiled scene with stable node ids, z-order, transforms, resource deps. |
| **Layer host surface** | Persistent native visual slot per layer per screen. Payload swaps; surface stays alive. **(Windows)** XAML element + composition visual. **(macOS)** `NSView`/`CALayer` or Metal layer. |
| **Output host** | Native window/surface that paints audience or stage output and applies frame diffs. |
| **Operator preview** | Scaled view of a resolved logical screen — same payloads and transport as audience output, not a second engine. |
| **Endpoint stability** | Unchanged screen/endpoint/bounds must not reattach content, recreate slots, or rebind players. |

Pipeline flow:

```text
Command source → cue request → cue preparation → prepared cue
  → layer router → live session snapshot → frame composer
  → audience/stage frame set → host adapter → endpoint
```

---

## Show Controls and support records

| Term | Meaning |
|---|---|
| **Show Controls** | Live operational panels: audio bin, stage, timers, messages, props, macros — usable while slides/media run. |
| **Timer** | Generated runtime countdown/elapsed state; consumed by messages, stage, headers, actions. |
| **Message** | Operator-triggered audience overlay from a template. |
| **Prop** | Reusable persistent overlay; show/hide/toggle on audience screens. |
| **Macro** | Named action batch the operator or automation can run. |
| **Support file** | Portable configuration record: Looks, screens, stage layouts, clear groups, timers, messages, props, macros, masks, labels. |

---

## Platform roles (same concept, different native names)

Use the **shared term** in product discussion. Use platform names only when discussing implementation.

| Shared concept | Windows implementation | macOS implementation |
|---|---|---|
| UI thread marshaling | `DispatcherQueue` **(Windows only)** | `MainActor` **(macOS only)** |
| Declarative UI markup | XAML **(Windows only)** | SwiftUI views **(macOS only)** |
| View + code wiring | Code-behind (`.xaml.cs`) **(Windows only)** | SwiftUI view + coordinator **(macOS only)** |
| UI state layer | ViewModel (MVVM) | Observable view model / model type |
| Command queue | `Channel<LiveCommand>` + worker | `AsyncStream` / actor |
| Media playback | `MediaPlayer` + `MediaPlaybackList` | `AVPlayer` + `AVPlayerLooper` |
| 2D/scene rendering | Win2D + Composition | Core Graphics / Metal + Core Animation |
| Output window | `AppWindow` + presenter | `NSWindow` + screen monitoring |
| In-memory cache | `IMemoryCache` | `NSCache` |
| Local video transport | (NDI, etc.) | Syphon **(macOS only)** |
| Packaged app identity | MSIX / App SDK package **(Windows only)** | App bundle + entitlements **(macOS only)** |

---

## Terms to keep distinct (common confusions)

| Do not conflate | Why |
|---|---|
| **Selection** vs **live state** | Selected slide ≠ slide currently on the slide layer. |
| **Theme** vs **Look** | Theme styles slide content; Look routes layers to screens. |
| **Screen** vs **endpoint** | Screen is what to render; endpoint is where it is sent. |
| **Media asset** vs **media cue** | Asset is the file identity; cue is one configured use of it. |
| **Playlist remove** vs **presentation delete** | Removing a playlist entry is non-destructive; deleting a library presentation is destructive. |
| **Preview** vs **audience output** | Preview observes the same resolved frame; it is not authoritative live state. |
| **Clear button active state** vs **recently pressed** | Active means a target layer currently has live content. |
| **Native host** vs **application runtime** | Host expresses intent and paints frames; runtime owns truth. |

---

## Status update vocabulary

When reporting progress, prefer this structure:

1. **Workflow area** — Show, Editor, Output Setup, etc.
2. **System** — content graph, unified pipeline, output host, media service, etc.
3. **Term** — layer slot, prepared cue, Look, transport lane, etc.
4. **Change** — what happened or is blocked, in plain language.
5. **Next** — next step in the same framing.

Avoid leading with file paths or type names without the workflow/system frame.
