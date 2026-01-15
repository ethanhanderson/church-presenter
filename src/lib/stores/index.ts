/**
 * Stores index - re-export all stores
 */

export { useCatalogStore } from './catalogStore';
export { useSettingsStore } from './settingsStore';
export { useEditorStore } from './editorStore';
export { useLiveStore, useCurrentSlide, useNextSlide, usePreviousSlide } from './liveStore';
export { useMusicManagerStore } from './musicManagerStore';
export { useShowStore } from './showStore';
export { useWorkspaceStore } from './workspaceStore';
export type { LivePresentationEvent, LiveStateEvent } from './liveStore';
export type { AutoSaveStatus, AutoSaveState } from './editorStore';
export type { AppPage } from './workspaceStore';
