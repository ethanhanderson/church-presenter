# Administration, Portability, and Preferences

ProPresenter is not only a live rendering engine. It has account/seat administration, activation, show building without activation, preferences, support files, sync, migration, localization, copyright display, crash/analytics settings, and machine-specific device bindings. ChurchPresenter should keep these concerns explicit so native Windows and macOS apps can share content without sharing invalid machine state.

## Source Basis

- [All features](https://renewedvision.com/propresenter/all-features)
- [Main ProPresenter page](https://renewedvision.com/propresenter)
- [Syncing Between Computers](https://support.renewedvision.com/hc/en-us/articles/360041588774-Syncing-Between-Computers-with-ProPresenter)
- [Migrating ProPresenter Data to a New Machine](https://support.renewedvision.com/hc/en-us/articles/360041339674-Migrating-ProPresenter-Data-to-a-New-Machine)
- [Media Management](https://support.renewedvision.com/hc/en-us/articles/360041815133-ProPresenter-Media-Management)
- [Media Import Presets](https://support.renewedvision.com/hc/en-us/articles/34512245702035-Media-Import-Presets-in-ProPresenter)
- [SongSelect and ProPresenter](https://support.renewedvision.com/hc/en-us/articles/360041815033-SongSelect-and-ProPresenter)
- [Planning Center](https://support.renewedvision.com/hc/en-us/articles/4408670102419-Using-Planning-Center-with-ProPresenter)
- [MultiTracks Integration](https://support.renewedvision.com/hc/en-us/articles/4412207810323-MultiTracks-Integration)
- [Resi Streaming](https://support.renewedvision.com/hc/en-us/articles/360058942613-Setting-up-Resi-Streaming-in-ProPresenter)
- [Connecting to ProPresenter Control](https://support.renewedvision.com/hc/en-us/articles/6024791423763-Connecting-to-ProPresenter-Control)
- [Library and show management](../library-show-management/libraries-playlists-integrations.md)
- [Sources](../output-system/sources.md)
- [Feature inventory](../feature-inventory.md)

## Seats, Activation, and Show Building

The public pages describe subscription plans, adding/removing seats, activating/deactivating devices, team/user account management, support access, feature releases, OS support, and the ability to edit/build shows without needing to activate a seat.

Architecture implications:

- Local document editing should not require a live output license check in the core content model.
- Activation state should control gated capabilities, not corrupt or hide local data.
- Account/team state should live outside presentation/library file formats.
- Device activation should be distinct from local machine identity, output endpoint identity, and content ownership.

Current public plan behavior:

- Plans support flexible billing, seat add/remove, and device activation/deactivation.
- Active subscriptions include 7-day support access, regular feature releases, stability improvements, and OS support.
- Team/user account management is part of the subscription surface.
- Public feature pages state that teams can edit and build shows even without needing to activate a seat.

Architecture implication: authoring, library browsing, package import, and offline preparation should be modeled separately from live-output entitlement checks.

## Shared Support Files

ProPresenter sync/migration material identifies support files/settings that travel between machines, including screen configurations, Looks, stage layouts, props, messages, timers, macros, groups/labels, and other program settings.

ChurchPresenter should define a support-configuration schema for:

- Output screens and logical screen setup.
- Looks and layer routing presets.
- Stage layouts and stage screen assignments.
- Clear groups.
- Props, message templates, timer definitions, and macro definitions.
- Group/label defaults.
- Media import defaults and shared operator settings where appropriate.

Support files should be versioned, importable, exportable, diffable where practical, and separated from machine-local bindings.

Current support-file behavior:

- Sync exposes **Support Files** as a category alongside Libraries, Media, Playlists, and Themes.
- Support Files include screen configuration, Looks, props, messages, timers, macros, and similar configuration.
- Current-version new-machine migration uses the same sync repository flow as normal sync.
- Support files can move without necessarily moving all machine-specific devices successfully; local reconciliation is still required.

Architecture implication: support configuration should have a manifest that can report unresolved local bindings after import.

## Machine-Local Bindings

Some settings should not travel blindly:

- Monitor ids and window placements.
- SDI/Blackmagic device ids and selected physical ports.
- NDI discovered source instance ids where ephemeral.
- Local audio device ids.
- Capture device ids.
- Credentials and tokens.
- Local cache paths.
- Recent files and user-specific UI state.

ChurchPresenter should map portable logical configuration to local bindings through a reconciliation step. For example, a portable "Main Room" screen can exist before the current machine has the projector attached.

## Sync and Migration

ProPresenter syncs/migrates categories such as libraries, media, playlists, themes, and support files through a repository-style workflow. Replacement options can delete or replace local files, so the operator needs clear previews.

ChurchPresenter should support:

- Selective import/export categories.
- Media inclusion choices.
- Conflict previews.
- Replace/add/skip decisions.
- Destructive operation summaries.
- Post-import diagnostics for missing media, missing devices, and unresolved integrations.

Sync and migration should be content graph operations, not file-copy shortcuts.

Current sync/migration workflow:

- Create a repository folder on local storage, external drive, or shared network folder.
- Select the repository in the Sync tab of Settings.
- Choose categories: Libraries, Media, Playlists, Themes, Support Files.
- Sync up to repository from the source machine.
- Sync down from repository on the destination machine.
- `Replace Files in Repository` is used when a repository already exists but should be overwritten from the source.
- `Replace My Files` on destination can delete or replace local presentations/media with repository contents.
- With `Replace My Files` unchecked, same-named presentations/media are replaced, but unique local files are kept.
- Media migration recommends **Manage Media Automatically** before sync; media added before enabling that setting may not move because it is still path-referenced.
- Cloud-sync folders like Dropbox, Google Drive, and OneDrive may work, but Renewed Vision does not support their service-specific behavior.

Architecture implication: sync needs a dry-run report with category inclusion, replace behavior, media copy status, conflict names, and missing-media risk.

## Preferences

The official user guide organizes preferences into areas such as general, screens, import, groups, input, network, sync, services, audio, advanced, devices, and updates. Those settings do not all have the same portability or ownership.

Recommended categories:

- Shared show configuration: screens, Looks, stage layouts, timers, macros, messages, props, groups, clear groups.
- User preference: UI density, theme, editor preferences, recent panels, local shortcuts where personal.
- Machine binding: monitors, audio/video devices, driver-specific settings, output positions.
- Service credential: Planning Center, SongSelect, MultiTracks, ProContent, Resi, account tokens.
- Cache/derived data: thumbnails, generated previews, resolved media caches, search indexes.
- Policy/admin: activation, allowed integrations, analytics/crash reporting, update channels.

This categorization should be reflected in file layout and settings services.

Current settings areas surfaced by current KB articles:

- General: house-of-worship integration visibility, CCLI reporting access, support file locations.
- Media: Manage Media Automatically, Media Search Paths, Clean Up Unlinked Media.
- Import: media scaling defaults, foreground/background behavior, image duration, video stop/loop, audio stop/loop/play-next.
- Inputs: video inputs, audio inputs, source selection, preview thumbnails, linked audio, audio input mode, delay, volume, routing.
- Audio: channel count, Inspector output, Main output, SDI/NDI output, routing, volume, delay.
- Network: enable network, IP address, port, ProPresenter Control, API documentation, Network Link.
- Integrations: Planning Center, SongSelect, MultiTracks, Resi.
- Sync: repository location, categories, sync direction, replace options.
- Devices: MIDI, DMX/Art-Net, auto reconnect, channel maps.
- Screens: screen configuration, performance statistics, bit depth, HDR output.

Architecture implication: settings storage should not be one flat JSON blob. Each category has different portability, secrecy, and failure modes.

## Import Defaults

Import preferences affect how media and content enter the system: managed versus referenced media, foreground/background defaults, duration, loop/stop behavior, scaling, group defaults, and service integrations.

ChurchPresenter should store import defaults separately from cue overrides. Changing a default should affect future imports, not silently mutate existing cues unless the operator runs an explicit migration.

Current import defaults:

- Foreground and background scaling can be set independently.
- Images can import foreground/background with no duration, fixed duration, or random duration.
- Videos can import foreground/background with stop or loop behavior.
- Audio can import with stop, loop, or play-next behavior.
- Scale + Blur is entitlement/version-gated in current docs and requires a supported active plan.

Architecture implication: import defaults may depend on entitlement and version compatibility. Cue schema should preserve unsupported-but-known settings when imported from another machine.

## Services and Credentials

SongSelect, Planning Center, MultiTracks, ProContent, Resi, account administration, and remote/control APIs all involve credentials or service state. These should be kept out of portable content packages unless explicitly exported through a secure account workflow.

Architecture implications:

- Integration identities in content should be non-secret references.
- Credentials should be platform-secure secrets.
- Missing credentials should degrade gracefully: content remains visible, refresh/reporting is unavailable.
- Service sync should record source and last-refresh state for diagnostics.

Current service credential behavior:

- Planning Center, SongSelect, MultiTracks, and Resi sign in from the Integrations tab.
- Planning Center and SongSelect are hidden until **Show House of Worship Integrations** is enabled.
- SongSelect reporting depends on signed-in account and internet connection.
- MultiTracks checks license validity over the internet.
- Resi registers the local ProPresenter computer as an encoder and can install/update a Resi plug-in.
- ProPresenter Control requires local network enablement, IP/port, and same-network connectivity rather than cloud login.

Architecture implication: integrations need separate states for hidden, not configured, signed out, signed in, offline, missing entitlement, and locally unavailable.

## Copyright Display

ProPresenter worship workflows include copyright display/reporting expectations for songs and Bibles. This is not only a footer string:

- Song imports may carry CCLI metadata.
- Bible translations may require translation/copyright display.
- Themes may decide where copyright/reference text appears.
- Reports may depend on usage during a service.

ChurchPresenter should preserve copyright metadata with generated/imported content and expose it to render themes, reports, and service summaries.

Current accessible support coverage is strongest through SongSelect and Bibles:

- SongSelect imports preserve CCLI number, title, author, publisher, and copyright year in a reporting file.
- Auto-reporting status depends on the CCLI number being present in the presentation.
- Bible generation exposes translation display, reference placement, verse references, and passage reference options.
- Bible translations may be installed/registered separately and some translations are sold separately.

The current Renewed Vision copyright-display article found during research required sign-in, so this page intentionally uses accessible current SongSelect/Bible behavior rather than older-version instructions.

Architecture implication: copyright display should be generated from structured song/scripture metadata and theme/layout placement, with reporting state tracked separately from rendered text.

## Localization

Localization affects UI text, Bible language support, date/time formats, timer/clock rendering, search behavior, and generated scripture/song metadata. Documents should avoid hardcoded locale-specific output where a runtime provider or content metadata is more appropriate.

For native Windows and macOS apps:

- Use shared invariant document data.
- Let platform UI resources localize labels.
- Store culture/language metadata for generated content.
- Resolve date/time display through runtime culture settings where appropriate.

Current public feature pages advertise Bible translations across many languages, and current timer/stage docs expose date/time formatting, 24-hour clock, and timer formatting controls. Treat those as current localization-relevant behaviors; no current accessible dedicated localization KB article was found in this pass.

## Support and Diagnostics

The public pages emphasize support access, stability improvements, OS support, crash reports, and analytics. ChurchPresenter should build diagnostics as product features:

- Output endpoint health.
- Media missing/relinked state.
- Capture/stream state.
- Integration connection state.
- Command/action failure logs.
- Package/sync warnings.
- Device/driver errors.

These diagnostics should be visible to operators and support workflows, not only written to developer logs.

Current diagnostic surfaces from current KB articles:

- Screen performance statistics can show internal and actual output frame rates.
- NDI troubleshooting uses IP/subnet checks, NDI Tools loopback, receiving-computer tests, low-bandwidth checks, and network/admin guidance.
- RTMP and Resi capture indicators use green/yellow/red status for healthy, dropped frames, and stopped/lost connection.
- Media Management exposes missing-media thumbnails and relink/search-path behavior.
- ProPresenter Control connection troubleshooting checks IP, port, network, firewall, and same-network state.
- Device integrations expose connection state, auto reconnect, and incoming MIDI note display.
- Sync/migration exposes category and replace options, but requires explicit operator care around destructive replacement.

Architecture implication: each subsystem should publish health and operator-actionable recovery hints through a common diagnostics surface.

## Cross-Feature Interactions

Administration and portability touch every feature: a show package may include presentations, media, Looks, stage layouts, timers, props, messages, macros, and themes, while excluding monitor ids, credentials, caches, and local devices. The architecture should make those boundaries visible in services and file formats before sync, migration, remote control, and multi-machine workflows expand.
