# Libraries, Playlists, and Generated Content

ProPresenter separates durable content libraries from the service playlists that arrange content for a live event. It also generates or imports content from Bible search, SongSelect, MultiTracks, Planning Center, ProContent, text/file imports, and packages. ChurchPresenter should preserve source identity and ownership so content remains portable, editable, and recoverable.

## Source Basis

- [Main ProPresenter page](https://renewedvision.com/propresenter)
- [Media & Slides](https://renewedvision.com/propresenter/media-and-slides)
- [Automations & Control](https://renewedvision.com/propresenter/automations)
- [Quick Search](https://support.renewedvision.com/hc/en-us/articles/9808905773715-How-to-Use-Quick-Search-within-ProPresenter)
- [SongSelect and ProPresenter](https://support.renewedvision.com/hc/en-us/articles/360041815033-SongSelect-and-ProPresenter)
- [MultiTracks Integration](https://support.renewedvision.com/hc/en-us/articles/4412207810323-MultiTracks-Integration)
- [Using Planning Center](https://support.renewedvision.com/hc/en-us/articles/4408670102419-Using-Planning-Center-with-ProPresenter)
- [Using Bibles](https://support.renewedvision.com/hc/en-us/articles/360041347594-Using-Bibles-in-ProPresenter)
- [Themes in ProPresenter](https://support.renewedvision.com/hc/en-us/articles/11910559859603-Themes-in-ProPresenter)
- [Syncing Between Computers](https://support.renewedvision.com/hc/en-us/articles/360041588774-Syncing-Between-Computers-with-ProPresenter)
- [Migrating ProPresenter Data to a New Machine](https://support.renewedvision.com/hc/en-us/articles/360041339674-Migrating-ProPresenter-Data-to-a-New-Machine)
- [Library and show management](../library-show-management/libraries-playlists-integrations.md)
- [Feature inventory](../feature-inventory.md)

## Libraries

Libraries are durable containers for presentations. A single service playlist can use presentations from multiple libraries. Adding a presentation to a playlist should create a reference to the library-owned presentation, not a new owner of the presentation file.

ChurchPresenter should model:

- `Library` as the owner of presentation documents.
- `Presentation` as a durable content document with stable identity and path/bundle metadata.
- Copy between libraries as a new owned presentation.
- Move between libraries as an ownership transfer.
- Library deletion as a destructive operation that traverses playlist references, cache state, thumbnails, prepared cues, render snapshots, and external links.

This ownership model prevents common service-planning errors: removing an item from a playlist should not delete the source song, while deleting a library-owned song should remove every reference to that song.

## Service Playlists

Playlists are ordered operational structures. They can contain presentations, standalone media, headers, placeholders, generated items, external-plan items, and future automation-only cues.

Important behavior:

- A presentation may appear multiple times in the same playlist.
- Duplicate occurrences are independent and may later have different arrangement, destination, notes, plan-link, or cue context.
- Reordering and deleting should target the selected occurrence unless the operator explicitly chooses all matching occurrences.
- Playlist selection and library selection are independent UI concerns.

For ChurchPresenter, `PlaylistItem` should be a typed record with identity, item kind, target reference, executable state, occurrence metadata, and optional external provenance.

## Playlist Templates

Playlist templates let teams define recurring service structures before all content exists. A template can include section headers, placeholders, recurring presentations, and planned order. Creating a playlist from a template should preserve the structure and allow later linking or replacement of placeholders.

Architecture implications:

- Headers and placeholders are valid playlist items.
- A playlist template is not just a copied playlist; it is a reusable structure that may contain unresolved slots.
- Template expansion should create new playlist item identities while preserving the semantic section names.

## Quick Search

Quick Search gives operators one place to find content across libraries and connected sources such as SongSelect and MultiTracks. Search results can preview text or slide cues and can be added to the selected playlist.

ChurchPresenter should keep search results source-aware:

- Local library result.
- Importable integration result.
- Generated Bible/search result.
- Media asset result.
- Previously linked external-plan result.

Adding a search result to a playlist should either add a reference to existing local content or create/import generated content with provenance.

Current Quick Search behavior:

- Open from the toolbar magnifying glass or `Command+F` / `Control+F`.
- Search scopes are all ProPresenter libraries, SongSelect, and MultiTracks.
- Library search is alphabetical and can preview a selected document beside the search list.
- Library previews support **Text View** for document text and **Grid View** for slide cues as they appear in the document.
- `Command+Enter` / `Control+Enter` adds the selected library search result to the selected playlist.
- SongSelect search preview intentionally shows only a few lines before import so the preview does not count as a SongSelect usage.
- SongSelect import requires CCLI/SongSelect login; MultiTracks search requires the relevant MultiTracks subscription.

Architecture implication: Quick Search is a federated search/import/link command surface. A result should carry source, preview mode, add/import/link capabilities, and subscription/auth state.

## SongSelect and CCLI

The public pages emphasize SongSelect by CCLI import and automatic usage reporting. This is more than text import:

- Search by title, author, keywords, or CCLI number.
- Import lyrics into the local library.
- Preserve song identity and reporting metadata.
- Report usage after service workflows when enabled.

ChurchPresenter should track imported song provenance, licensing/reporting fields, and whether local edits should remain detached or refreshable from the source.

Current SongSelect behavior:

- Requires **Show House of Worship Integrations** to be enabled in Settings.
- Login happens from the Integrations tab or first search in Quick Search.
- Search supports title, author, keywords, and CCLI number.
- Import previews lyrics and imports the song into the local library.
- An Advanced SongSelect subscription plus CCLI license is required for full access.
- Downloaded songs are stored in a CCLI reporting file with download date, title, author, publisher, copyright year, and CCLI number.
- Auto-reporting can be enabled from the SongSelect login state in Integrations.
- Reporting state uses visible statuses: green reported, yellow pending/in progress, red not signed in or offline, white selected when auto-reporting was not enabled.
- Auto-reporting happens once per day per song when a slide is triggered in a presentation with a CCLI number and a valid signed-in SongSelect account.

Architecture implication: CCLI reporting is tied to live usage, source metadata, and account/network state. It should not be modeled as only import metadata.

## MultiTracks

The Automations page describes MultiTracks import of lyrics, chord charts, MIDI cues, and more. That affects several domains at once:

- Presentation text and arrangements.
- Stage-display chord/chart content.
- Slide actions or automation cues.
- Device/MIDI-related command data.

The architecture should not flatten MultiTracks data into anonymous slides. Imported content should retain enough source metadata to support refresh, diagnostics, and future reporting.

Current MultiTracks behavior:

- MultiTracks Search requires ProPresenter 7.8 or later with the appropriate active plan and internet access for license validation.
- Lyrics Search requires a ChartPro subscription.
- Chord Import requires ChartPro with the ProPresenter add-on.
- Automation requires ChartPro with the ProPresenter add-on and Cloud Pro Plus.
- Search results show preview, album artwork, artist, album, MultiTracks id, supported feature icons, and original key.
- Import can include lyrics, chords, and automation depending on subscription.
- Text can import preformatted with 1, 2, or 4 lines when chords are not used.
- Chord import can feed stage layouts such as **MultiTracks Chords + Lyrics**.
- Stage text boxes can enable chords and choose notation such as chords, numbers, or numerals.
- Automation imports can be triggered by Playback over network MIDI notes.

Architecture implication: one integration can produce presentation text, stage data, and automation/device cues. The import pipeline needs multi-artifact transactions and clear diagnostics when a subscription lacks one part.

## Planning Center

Planning Center integration turns an external order of service into a local playlist. The existing support-derived reference identifies key behavior:

- Login can be gated behind house-of-worship integrations.
- Imported services can check for plan updates.
- Plan items can match existing library presentations.
- Items can be linked, unlinked, hidden, or unhidden.
- Song sequences can become ProPresenter arrangements when group names match.
- Attachments can download or upload depending on item type and settings.

ChurchPresenter should separate external identity from local content identity:

- External service, plan, and item ids.
- Local content reference, if linked.
- Arrangement/sequence mapping.
- Hidden/skipped state.
- Last known external update state.
- Upload/download intent.

This lets a playlist item remain linked to a service plan without forcing the local presentation to become disposable.

Current Planning Center behavior:

- Requires **Show House of Worship Integrations** and login from the Integrations tab.
- Options include automatically checking for plan updates, matching presentations in the library, showing historical plans, making arrangements from sequences, automatically uploading linked presentations/media, and automatically downloading presentations/media.
- A Planning Center service is added as a playlist from the `+` button in the library/playlist section.
- Unlinked plan items expose four actions: add an attachment from Planning Center, create a new ProPresenter presentation, import a local file, or search ProPresenter/SongSelect/MultiTracks and link/import.
- Attachment manager can download files attached to the Planning Center item.
- File import can link ProPresenter presentations, text files that generate presentations, or media files.
- Search can link existing library presentations or import/link from SongSelect or MultiTracks.
- Linked items can be unlinked from the item bar.
- Plan items can be hidden and later unhidden from the playlist.

Architecture implication: a Planning Center playlist item has both external item state and local link state. Link choices are workflows, not a single import flag.

## Bibles

Bible support is a generated-content workflow. The main ProPresenter page advertises dynamic scripture lookup with 130+ translations across 25+ languages, with some translations sold separately. Existing support-derived docs add behavior such as passage/range/keyword search, theme selection, verse numbering, break-on-new-verse, translation display, reference placement, saving as a presentation, copying into the current presentation, and adding to a playlist.

ChurchPresenter should treat Bible output as generated presentation content:

- Translation and language metadata.
- Passage/range/query source.
- Generated slide options and theme id.
- Copyright/license/citation metadata.
- Whether regenerated content should replace or preserve local edits.

Current Bible behavior:

- Access Bibles from `View > Bibles` or the toolbar.
- Installed translations appear in a left-side translation list; translations must be installed/registered before use.
- Search supports book/chapter selection, passage/range entry with shortened book names, and keyword search.
- Multiple translations can be added to slides with a plus button next to the main translation.
- Bible themes can use one text box for verse plus reference or separate verse/reference text boxes.
- Bible theme objects are selected through Verse and Reference dropdowns in Bible View.
- Bible slides are created at the resolution of the top Audience Screen in Screen Configuration.
- Slide options include verse numbers, break on new verse, display translation, preserve font color, reference placement, template, and import library.
- Output actions include **Save as Presentation**, **Copy to Current Presentation**, and **Save to Selected Playlist**.
- Previous/Next Verse buttons can quickly add adjacent verses live, including crossing chapter boundaries.

Architecture implication: scripture generation depends on installed translation inventory, screen/theme context, generated slide options, and a target library/playlist/presentation destination.

## ProContent

The Media & Slides page describes ProContent as integrated media with free bundled assets and a much larger subscription library. For architecture, this is an online asset source that should feed the same media asset and cue pipeline as local media.

Recommended model:

- Remote asset identity and provider metadata.
- Local cached/resolved file identity.
- License/subscription state.
- Thumbnail and preview metadata.
- Cue defaults after import.

The playback engine should not care whether a video came from ProContent, a camera roll, a package, or a local file once the asset is resolved.

## Imports, Exports, and Packages

ProPresenter supports file imports, presentation exports, presentation bundles with media, playlist exports with optional media, migration, and sync. This requires explicit package boundaries:

- Presentation document without media.
- Presentation bundle with media.
- Playlist/package export with optional media.
- Media library assets and cue metadata.
- Shared support files such as screens, Looks, stage layouts, props, messages, timers, macros, groups, and labels.
- Machine-local bindings such as monitor ids, capture devices, credentials, caches, and local paths.

ChurchPresenter package operations should preview destructive outcomes, especially replacement/delete behavior during sync, migration, or import.

Current sync/export behavior:

- Individual presentations and playlists can be exported instead of syncing all data.
- Presentation export contains slide information without media.
- Presentation Bundle contains media.
- Playlist export can optionally include media.
- Sync uses a repository folder, often named `ProPresenter Sync`, on local/external/shared storage.
- Sync categories include Libraries, Media, Playlists, Themes, and Support Files.
- Media sync includes linked media in presentations, playlists, and the Media Bin.
- Playlist sync includes library playlists and Media Bin playlists; media must also be enabled for the files themselves.
- Support Files include screen configuration, Looks, props, messages, timers, macros, and related configuration.
- `Replace My Files` can delete or replace local presentations/media; unchecked behavior only replaces same-named items.
- New-machine migration recommends enabling **Manage Media Automatically** before syncing because older referenced media may not move.

Architecture implication: sync/import/export needs category manifests, media inclusion rules, same-name conflict behavior, and explicit destructive previews.

## Content Provenance

Generated or imported content should carry provenance because it changes user expectations:

- Manual presentation.
- Bible-generated presentation.
- SongSelect/CCLI import.
- MultiTracks import.
- Planning Center linked item.
- ProContent media.
- Local file import.
- Package import.

Provenance affects refresh behavior, reporting, licensing prompts, conflict resolution, and whether local edits are treated as source-of-truth changes.

## Cross-Feature Interactions

Libraries and playlists feed the show surface. Generated content feeds presentations. Integrations feed arrangements, slide actions, and stage data. Package/sync features carry content and support files across machines. The architecture should keep those concerns connected by references and provenance, not by hidden side effects in the UI.
