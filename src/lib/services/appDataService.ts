/**
 * App data service for documents folder structure and files.
 */

import type { Catalog, Library, Playlist } from '@/lib/models';
import { createPresentation, defaultCatalog } from '@/lib/models';
import {
  ensureDocumentsDataDir,
  ensureDocumentsDataSubDir,
  getDocumentsDataDir,
  openBundle,
  readAppDataFile,
  readDocumentsDataFile,
  saveBundle,
  writeDocumentsDataFile,
} from '@/lib/tauri-api';

const LIBRARIES_DIR = 'libraries';
const PLAYLISTS_DIR = 'playlists';
const PRESENTATIONS_DIR = 'presentations';
const SONG_PRESENTATIONS_DIR = `${PRESENTATIONS_DIR}/songs`;
const MEDIA_LIBRARY_DIR = 'media-library';

const LIBRARIES_FILE = `${LIBRARIES_DIR}/libraries.json`;
const PLAYLISTS_FILE = `${PLAYLISTS_DIR}/playlists.json`;
const LEGACY_CATALOG_FILE = 'catalog.json';
const WELCOME_PRESENTATION_FILENAME = 'Welcome.cpres';

const normalizePath = (path: string) => path.replace(/\\/g, '/');

export const getSongPresentationRelativePath = (songId: string) =>
  `${SONG_PRESENTATIONS_DIR}/${songId}.cpres`;

export async function getDocumentsDataDirPath(): Promise<string> {
  const dir = await getDocumentsDataDir();
  return normalizePath(dir);
}

export async function getMediaLibraryDirPath(): Promise<string> {
  await ensureDocumentsDataSubDir(MEDIA_LIBRARY_DIR);
  const baseDir = await getDocumentsDataDirPath();
  return normalizePath(`${baseDir}/${MEDIA_LIBRARY_DIR}`);
}

export async function toDocumentsRelativePath(path: string): Promise<string> {
  const baseDir = await getDocumentsDataDirPath();
  const normalized = normalizePath(path);
  if (normalized.startsWith(`${baseDir}/`)) {
    return normalized.slice(baseDir.length + 1);
  }
  return normalized;
}

export async function generatePresentationPath(
  title: string,
  presentationId: string
): Promise<string> {
  await ensureDocumentsDataSubDir(PRESENTATIONS_DIR);

  const baseDir = await getDocumentsDataDirPath();
  const sanitizedTitle = title
    .replace(/[<>:"/\\|?*]/g, '')
    .replace(/\s+/g, '_')
    .substring(0, 50);

  return normalizePath(
    `${baseDir}/${PRESENTATIONS_DIR}/${sanitizedTitle}_${presentationId.slice(0, 8)}.cpres`
  );
}

export async function generateSongPresentationPath(songId: string): Promise<string> {
  await ensureDocumentsDataSubDir(SONG_PRESENTATIONS_DIR);

  const baseDir = await getDocumentsDataDirPath();
  return normalizePath(`${baseDir}/${SONG_PRESENTATIONS_DIR}/${songId}.cpres`);
}

export async function readLibraries(): Promise<Library[]> {
  return (await readDocumentsDataFile<Library[]>(LIBRARIES_FILE)) ?? [];
}

export async function readPlaylists(): Promise<Playlist[]> {
  return (await readDocumentsDataFile<Playlist[]>(PLAYLISTS_FILE)) ?? [];
}

export async function writeLibraries(libraries: Library[]): Promise<void> {
  await writeDocumentsDataFile(LIBRARIES_FILE, libraries);
}

export async function writePlaylists(playlists: Playlist[]): Promise<void> {
  await writeDocumentsDataFile(PLAYLISTS_FILE, playlists);
}

async function ensureWelcomePresentation(): Promise<void> {
  const baseDir = await getDocumentsDataDirPath();
  const welcomePath = normalizePath(`${baseDir}/${PRESENTATIONS_DIR}/${WELCOME_PRESENTATION_FILENAME}`);

  try {
    await openBundle(welcomePath);
  } catch {
    const presentation = createPresentation('Welcome');
    await saveBundle(welcomePath, presentation);
  }
}

async function migrateLegacyCatalogIfNeeded(): Promise<void> {
  const [existingLibraries, existingPlaylists] = await Promise.all([
    readDocumentsDataFile<Library[]>(LIBRARIES_FILE),
    readDocumentsDataFile<Playlist[]>(PLAYLISTS_FILE),
  ]);

  if (existingLibraries || existingPlaylists) return;

  const legacyCatalog = await readAppDataFile<Catalog>(LEGACY_CATALOG_FILE);
  if (!legacyCatalog) return;

  await Promise.all([
    writeLibraries(legacyCatalog.libraries ?? defaultCatalog.libraries),
    writePlaylists(legacyCatalog.playlists ?? defaultCatalog.playlists),
  ]);
}

export async function initializeAppData(): Promise<void> {
  await ensureDocumentsDataDir();
  await Promise.all([
    ensureDocumentsDataSubDir(LIBRARIES_DIR),
    ensureDocumentsDataSubDir(PLAYLISTS_DIR),
    ensureDocumentsDataSubDir(PRESENTATIONS_DIR),
    ensureDocumentsDataSubDir(SONG_PRESENTATIONS_DIR),
    ensureDocumentsDataSubDir(MEDIA_LIBRARY_DIR),
  ]);

  await migrateLegacyCatalogIfNeeded();

  const [libraries, playlists] = await Promise.all([
    readDocumentsDataFile<Library[]>(LIBRARIES_FILE),
    readDocumentsDataFile<Playlist[]>(PLAYLISTS_FILE),
  ]);

  await Promise.all([
    libraries ? Promise.resolve() : writeLibraries(defaultCatalog.libraries),
    playlists ? Promise.resolve() : writePlaylists(defaultCatalog.playlists),
  ]);

  await ensureWelcomePresentation();
}
