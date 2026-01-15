/**
 * Tauri API wrappers for Church Presenter
 */

import { invoke } from '@tauri-apps/api/core';
import type { Presentation, MediaEntry } from './models';

// ============================================================================
// Types from Rust
// ============================================================================

export interface ParsedBundle {
  manifest: string;
  slides: string;
  arrangement: string;
  themes: ThemeFile[];
}

export interface ThemeFile {
  filename: string;
  content: string;
}

export interface BundleState {
  manifest: string;
  slides: string;
  arrangement: string;
  themes: ThemeFile[];
  media: MediaFileRef[];
}

export interface MediaFileRef {
  id: string;
  source_path: string;
  bundle_path: string;
}

export interface MonitorInfo {
  index: number;
  name: string;
  width: number;
  height: number;
  x: number;
  y: number;
  is_primary: boolean;
  refresh_rate?: number | null;
}

// ============================================================================
// Bundle I/O
// ============================================================================

/**
 * Open a .cpres presentation bundle
 */
export async function openBundle(path: string): Promise<Presentation> {
  const bundle = await invoke<ParsedBundle>('cpres_open', { path });

  const manifest = JSON.parse(bundle.manifest);
  const slides = JSON.parse(bundle.slides);
  const arrangement = JSON.parse(bundle.arrangement);
  const themes = bundle.themes.map(t => JSON.parse(t.content));

  return {
    manifest,
    slides,
    arrangement,
    themes,
  };
}

/**
 * Save a presentation bundle atomically
 */
export async function saveBundle(
  path: string,
  presentation: Presentation,
  mediaRefs: MediaFileRef[] = []
): Promise<void> {
  const state: BundleState = {
    manifest: JSON.stringify(presentation.manifest, null, 2),
    slides: JSON.stringify(presentation.slides, null, 2),
    arrangement: JSON.stringify(presentation.arrangement, null, 2),
    themes: presentation.themes.map(theme => ({
      filename: `themes/${theme.id}.json`,
      content: JSON.stringify(theme, null, 2),
    })),
    media: mediaRefs,
  };

  await invoke('cpres_save', { path, state });
}

/**
 * Read media from a bundle
 */
export async function readBundleMedia(bundlePath: string, mediaPath: string): Promise<Uint8Array> {
  const data = await invoke<number[]>('cpres_read_media', { bundlePath, mediaPath });
  return new Uint8Array(data);
}

/**
 * Import media files and compute their metadata
 */
export async function importMediaFiles(paths: string[]): Promise<MediaEntry[]> {
  const entries = await invoke<Array<{
    id: string;
    filename: string;
    path: string;
    mime: string;
    sha256: string;
    byte_size: number;
    media_type: string;
  }>>('cpres_import_media', { paths });

  return entries.map(e => ({
    id: e.id,
    filename: e.filename,
    path: e.path,
    mime: e.mime,
    sha256: e.sha256,
    byteSize: e.byte_size,
    type: e.media_type as MediaEntry['type'],
  }));
}

// ============================================================================
// App Data
// ============================================================================

/**
 * Get the app data directory path
 */
export async function getAppDataDir(): Promise<string> {
  return invoke<string>('get_app_data_dir');
}

/**
 * Get the documents app data directory path
 */
export async function getDocumentsDataDir(): Promise<string> {
  return invoke<string>('get_documents_data_dir');
}

/**
 * Set the content directory used by the app
 */
export async function setContentDir(
  path: string,
  options?: { moveExisting?: boolean; mediaLibraryDir?: string | null }
): Promise<string> {
  return invoke<string>('set_content_dir', {
    path,
    moveExisting: options?.moveExisting ?? true,
    mediaLibraryDir: options?.mediaLibraryDir ?? null,
  });
}

/**
 * Ensure the app data directory exists
 */
export async function ensureAppDataDir(): Promise<string> {
  return invoke<string>('ensure_app_data_dir');
}

/**
 * Ensure the documents app data directory exists
 */
export async function ensureDocumentsDataDir(): Promise<string> {
  return invoke<string>('ensure_documents_data_dir');
}

/**
 * Ensure a subdirectory exists within app data
 */
export async function ensureAppDataSubDir(subDir: string): Promise<string> {
  return invoke<string>('ensure_app_data_subdir', { subDir });
}

/**
 * Ensure a subdirectory exists within documents app data
 */
export async function ensureDocumentsDataSubDir(subDir: string): Promise<string> {
  return invoke<string>('ensure_documents_data_subdir', { subDir });
}

/**
 * Read a JSON file from app data directory
 */
export async function readAppDataFile<T>(filename: string): Promise<T | null> {
  try {
    const content = await invoke<string>('read_app_data_file', { filename });
    return JSON.parse(content);
  } catch {
    return null;
  }
}

/**
 * Read a JSON file from documents app data directory
 */
export async function readDocumentsDataFile<T>(filename: string): Promise<T | null> {
  try {
    const content = await invoke<string>('read_documents_data_file', { filename });
    return JSON.parse(content);
  } catch {
    return null;
  }
}

/**
 * Write a JSON file to app data directory
 */
export async function writeAppDataFile<T>(filename: string, data: T): Promise<void> {
  const content = JSON.stringify(data, null, 2);
  await invoke('write_app_data_file', { filename, content });
}

/**
 * Write a JSON file to documents app data directory
 */
export async function writeDocumentsDataFile<T>(filename: string, data: T): Promise<void> {
  const content = JSON.stringify(data, null, 2);
  await invoke('write_documents_data_file', { filename, content });
}

/**
 * Allow a media library directory in the fs scope (persisted)
 */
export async function allowMediaLibraryDir(path: string): Promise<void> {
  await invoke('allow_media_library_dir', { path });
}

// ============================================================================
// Window Management
// ============================================================================

/**
 * Open output windows on the specified monitors
 */
export async function openOutputWindows(monitorIndices: number[]): Promise<void> {
  await invoke('open_output_windows', { monitorIndices });
}

/**
 * Close all output windows
 */
export async function closeOutputWindows(): Promise<void> {
  await invoke('close_output_windows');
}

/**
 * Get list of available monitors
 */
export async function getMonitors(): Promise<MonitorInfo[]> {
  return invoke<MonitorInfo[]>('get_monitors');
}
