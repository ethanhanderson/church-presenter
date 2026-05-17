# Library and Show Management

This document captures ProPresenter's content-management behavior that affects ChurchPresenter's architecture beyond the output engine.

Primary sources: [Building your Playlist](https://support.renewedvision.com/hc/en-us/articles/360041344234-Building-your-Playlist-in-ProPresenter), [Creating and Using Playlist Templates](https://support.renewedvision.com/hc/en-us/articles/40377194830995-Creating-and-Using-Playlist-Templates-in-ProPresenter), [Understanding the ProPresenter User Interface](https://support.renewedvision.com/hc/en-us/articles/360041345954-Understanding-The-ProPresenter-User-Interface), [Themes in ProPresenter](https://support.renewedvision.com/hc/en-us/articles/11910559859603-Themes-in-ProPresenter), [Using Planning Center](https://support.renewedvision.com/hc/en-us/articles/4408670102419-Using-Planning-Center-with-ProPresenter), [Converting a Planning Center Song Sequence Into a ProPresenter Arrangement](https://support.renewedvision.com/hc/en-us/articles/360062444614-Converting-a-Planning-Center-Song-Sequence-Into-a-ProPresenter-Arrangement), [Using Bibles](https://support.renewedvision.com/hc/en-us/articles/360041347594-Using-Bibles-in-ProPresenter), [SongSelect and ProPresenter](https://support.renewedvision.com/hc/en-us/articles/360041815033-SongSelect-and-ProPresenter), [Quick Search](https://support.renewedvision.com/hc/en-us/articles/9808905773715-How-to-Use-Quick-Search-within-ProPresenter), [Reflow Editor](https://support.renewedvision.com/hc/en-us/articles/34513457866899-How-to-Use-Reflow-Editor-in-ProPresenter), [Syncing Between Computers](https://support.renewedvision.com/hc/en-us/articles/360041588774-Syncing-Between-Computers-with-ProPresenter), and [Migrating Data](https://support.renewedvision.com/hc/en-us/articles/360041339674-Migrating-ProPresenter-Data-to-a-New-Machine).

## Libraries and Service Playlists

ProPresenter supports multiple libraries. A single service playlist can contain presentations from any library. The Building your Playlist article describes adding presentations by right-clicking a library item, dragging to a playlist, dragging to a specific position in a playlist, or using search.

The UI article describes the left side of the app as an outline/detail split:

- Outline view contains libraries and playlists.
- Detail view shows the selected library or playlist contents.
- The selected presentation or playlist then drives the central show view.

ChurchPresenter should not assume "one library equals one show." A show playlist should contain references to library-owned presentations, generated items, standalone media, headers, placeholders, and future external-plan items.

For ChurchPresenter, a library is the durable owner of presentation documents. A presentation belongs to one owning library at a time, and playlist entries point to that library-owned presentation rather than owning or copying the `.cpres` file themselves. Copying a presentation into another library should create a distinct owned presentation bundle for that library; moving it to another library should transfer ownership. Adding a presentation to a playlist should only add a playlist reference.

Playlist references are per-occurrence items, not a uniqueness set keyed only by presentation path. The same library-owned presentation can appear multiple times in one playlist, including adjacent entries, and each occurrence can later carry its own arrangement, destination, notes, external-plan link, or cue context. Selecting a library, selecting a playlist, and selecting matching presentation rows in either source should remain independent UI actions; adding a presentation from a library to a playlist must not link the library selection to the playlist selection.

This ownership model makes destructive behavior clear:

- Deleting a playlist item removes only that playlist reference. If a playlist contains multiple occurrences of the same presentation, the default remove action should remove only the selected occurrence; the UI may offer an explicit unchecked "remove all instances" choice.
- Deleting a presentation from its library deletes the presentation from the app: remove the `.cpres` bundle through the content-management API, remove every playlist/reference occurrence, clear recents/workspace selections that point at it, invalidate session caches, prepared cues, thumbnails, render/scene snapshots, and publish a presentation-deleted content change.
- Deleting a library deletes the library and every presentation it owns. The operation must traverse the reference graph first, remove playlist and external-plan references to those owned presentations, delete the owned bundles through the same content-management path, clear dependent caches/state/settings, then persist catalog/library/playlist updates atomically where possible.
- Package, sync, cleanup, and audit flows should preview the affected owned presentations and downstream playlist/support references before applying destructive deletes.

## Playlist Templates

Playlist templates let teams predefine a recurring service flow. Renewed Vision's template article identifies these playlist-structure items:

- Headers for service sections such as Worship, Message, or Announcements.
- Placeholders for content that will be added later.
- Presentations for recurring elements.
- Drag/reorder/rename/delete operations before saving as a reusable template.

ChurchPresenter implication: a playlist item is not always executable content. It may be a header, placeholder, linked plan item, media cue, or presentation reference. Templates should preserve structure without requiring all assets to exist yet.

## Presentations, Slides, Groups, and Arrangements

The UI article says Show mode can add slides, transitions, arrangements, operator notes, and timelines. The Planning Center sequence article clarifies ProPresenter terminology:

- Planning Center uses `Arrangements` and `Sequence`.
- ProPresenter uses `Arrangements` and `Groups`.
- A Planning Center sequence is an ordered list of ProPresenter groups.
- Group names must match exactly, including capitalization, for automatic arrangement creation.
- If the sequence contains a group missing from the ProPresenter presentation, the arrangement is not created until the mismatch is resolved.
- A song can appear more than once in a service with different sequences; each can become a separate arrangement.

ChurchPresenter should model:

- `Presentation` as a durable document owned by a library.
- `Slide` as one item in a presentation.
- `Group` as a named section, especially for songs.
- `Arrangement` as an ordered sequence of groups/slides used for a specific service context.
- `PlaylistItem` as a placed reference to a library-owned presentation that may select a specific arrangement. Multiple `PlaylistItem` records may point to the same presentation and must still be treated as separate service placements.

## Reflow and Text Preparation

The Reflow Editor article describes text-focused slide preparation: splitting text across slides, combining slides, adjusting line breaks, previewing, and changing transition/theme options while editing text. This is a different workflow from canvas editing and matters for lyric-heavy church services.

Implementation implication: ChurchPresenter should keep text/reflow operations close to the presentation model. A lyric text editor should be able to regenerate slide boundaries while preserving group/arrangement semantics and avoiding accidental loss of output actions.

## Themes and Output Formatting

Themes can be applied to individual slides, ranges of slides, whole presentations, entire libraries, Bible searches, and Looks. The theme editor has theme slides, object z-order, canvas content, theme-slide background color, builds, shapes, and media actions in themes.

The most important output implication is that Looks can apply themes to screens to reformat the same triggered slide content on the fly. This supports patterns like full-screen lyrics in the room and lower-thirds on a stream from the same presentation content.

ChurchPresenter should not treat themes as only slide-editor styling. Themes can be:

- Content defaults for presentations/libraries.
- Bible-generation templates.
- Per-screen formatting variants selected by Looks.
- Containers for media actions in more advanced cases.

## Planning Center Integration Model

The current Planning Center article describes preferences and workflows that matter even if ChurchPresenter implements an equivalent integration later rather than Planning Center itself:

- The integration can be hidden behind a "House of Worship Integrations" setting.
- The app logs into Planning Center and can import a service as a playlist.
- It can automatically check for plan updates.
- It can match imported items to presentations already in the library.
- It can show historical plans.
- It can convert Planning Center song sequences into ProPresenter arrangements.
- It can upload linked presentations/media back to Planning Center.
- It can download attached presentations/media based on item type.
- Each imported item can link by attachment, new presentation, local import, or search.
- Linked items can be unlinked.
- Plan items can be hidden and later unhidden without deleting the source plan item.

ChurchPresenter should separate external plan identity from local content identity. A linked playlist item should know:

- External service/plan/item IDs.
- Local presentation/media reference, if linked.
- Arrangement/sequence mapping, if selected.
- Hidden/skipped state.
- Last known external update state.
- Whether local changes should upload or remain local.

## Bibles, SongSelect, and Quick Search

These features are not required for an initial output engine, but they shape the content model:

- Bibles can search by passage, range, or keyword; generate slides; apply Bible themes; include multiple translations; save as a presentation; copy into the current presentation; or save into a selected playlist.
- Bible slide options include verse numbers, break-on-new-verse, translation display, font-color preservation, reference placement, and import library.
- SongSelect search runs through Quick Search or File > Import, imports songs into the library, and participates in CCLI reporting.
- Quick Search searches all libraries, SongSelect, and MultiTracks from one window; library search can preview text or slide cues and add selected content to the selected playlist.

ChurchPresenter should keep generated content provenance. A presentation may come from a manual editor, Bible generation, SongSelect import, Planning Center attachment, text import, or package import. That provenance affects refresh, reporting, and user expectations.

## Sync, Migration, Export, and Packages

Renewed Vision's sync article distinguishes:

- Libraries: presentations in libraries.
- Media: linked media in presentations, playlists, and the media bin.
- Playlists: library playlists and media bin playlists.
- Themes.
- Support files: screen configuration, Looks, props, messages, timers, macros, and other configuration.

The migration article for moving current ProPresenter data to a new machine uses the same repository flow as Sync:

- create a repository folder such as `ProPresenter Sync`,
- choose it in the Sync tab of Settings,
- select the categories to transfer,
- run `Sync Up to Repository` on the source machine,
- copy or share that repository folder,
- run `Sync Down From Repository` on the destination machine.

Renewed Vision also calls out a media prerequisite: `Manage Media Automatically` should be enabled before migration if media is expected to move reliably. Media added before that setting was enabled may not move across.

Current sync and migration material identifies Support Files as a portable category that includes screen configuration, Looks, props, messages, timers, macros, and related configuration. This means portable setup is not only content; it includes the production configuration that makes a new machine behave like the original once local devices and credentials are reconciled.

It also describes export options:

- Presentation export contains slide information, not media.
- Presentation Bundle includes media.
- Playlist export can optionally include media.

The sync workflow also has destructive options such as `Replace Files in Repository` and `Replace My Files`. `Replace My Files` can delete or replace local presentations and media, so ChurchPresenter should surface replacement scope before running an import or sync.

ChurchPresenter should define package boundaries early:

- Project/show file.
- Presentation document.
- Presentation bundle with media.
- Playlist/package export with optional media.
- Shared configuration/support files: screens, Looks, stage layouts, groups and labels, props, messages, timers, macros, media defaults, and shared operator/interface setup.
- Sync/backup format with clear conflict and replace semantics.

ChurchPresenter implication: portable settings need a first-class schema and UI, not just incidental JSON files. A future Sync or Migration screen should let operators choose documents, media, themes, playlists, and shared support files separately, while keeping machine-only bindings such as monitor ids, local device selections, credentials, and caches out of portable support files.

## Implementation Invariants

- A playlist is an ordered operational structure, not just a list of presentation files.
- Playlist items can be placeholders, headers, external links, presentations, media, or other future item types.
- Presentations are owned by libraries; playlists reference them and may choose per-occurrence arrangements, destination, and cue context.
- Playlist presentation occurrences are independent references. Add operations may create duplicate references to the same presentation; remove, reorder, and per-reference preference operations must target the selected occurrence unless the operator explicitly chooses all matching occurrences.
- Copying a presentation into another library creates a new owned presentation document; moving a presentation transfers ownership.
- Deleting a library-owned presentation is destructive for that presentation bundle and must remove all app references and invalidate dependent state through application content-management services.
- Deleting or clearing a playlist item is non-destructive for the presentation bundle.
- Themes can act at content, generation, and output-routing levels.
- External integrations need stable link records rather than one-time imports only.
- Portability must distinguish documents, media assets, shared support/interface settings, machine-local bindings, and destructive replacement choices.

## Open Coverage Questions

- The official "building a Library" article requires sign-in when accessed directly. Before implementing library creation UI, locate an accessible current source or use the official user guide.
- Presentation timelines, operator notes, and masks need more detailed user-guide coverage if they become near-term.
- Decide whether ChurchPresenter needs CCLI/SongSelect reporting, Bible licensing, or a generic importer first.
