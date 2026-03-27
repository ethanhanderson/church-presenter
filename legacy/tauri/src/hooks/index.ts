/**
 * Hooks index
 */

export { useIsMobile } from './use-mobile';
export {
  useAutoSave,
  resolveConflict,
  useThemeAutoSave,
  resolveThemeConflict,
} from './use-auto-save';
export { useSystemFonts } from './use-system-fonts';
export { generatePresentationPath, generateSongPresentationPath } from '@/lib/services/appDataService';
export type { AutoSaveStatus, ThemeConflictData } from './use-auto-save';
