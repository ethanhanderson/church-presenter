/**
 * Catalog Store - manages libraries and playlists
 */

import { create } from 'zustand';
import { immer } from 'zustand/middleware/immer';
import type { Catalog, Library, Playlist, PresentationRef, ExternalSyncStatus } from '../models';
import { createLibrary, createPlaylist, createLinkedPlaylist, defaultCatalog } from '../models';
import type { LinkedSetData } from '../models/defaults';
import { readLibraries, readPlaylists, writeLibraries, writePlaylists } from '../services/appDataService';

interface CatalogState {
  catalog: Catalog;
  isLoading: boolean;
  error: string | null;

  // Library actions
  loadCatalog: () => Promise<void>;
  saveCatalog: () => Promise<void>;
  remapPresentationPaths: (oldBase: string, newBase: string) => void;
  
  addLibrary: (name: string) => Library;
  updateLibrary: (id: string, updates: Partial<Library>) => void;
  deleteLibrary: (id: string) => void;
  
  addPresentationToLibrary: (libraryId: string, presentation: PresentationRef) => void;
  removePresentationFromLibrary: (libraryId: string, path: string) => void;
  
  // Playlist actions
  addPlaylist: (name: string) => Playlist;
  addLinkedPlaylist: (setData: LinkedSetData, items?: PresentationRef[]) => Playlist;
  updatePlaylist: (id: string, updates: Partial<Playlist>) => void;
  deletePlaylist: (id: string) => void;
  
  addToPlaylist: (playlistId: string, presentation: PresentationRef) => void;
  removeFromPlaylist: (playlistId: string, path: string) => void;
  reorderPlaylist: (playlistId: string, fromIndex: number, toIndex: number) => void;

  // Linked playlist helpers
  findLinkedPlaylist: (setId: string) => Playlist | undefined;
  updatePlaylistSyncStatus: (playlistId: string, status: ExternalSyncStatus, conflictUrl?: string) => void;
}

export const useCatalogStore = create<CatalogState>()(
  immer((set, get) => ({
    catalog: defaultCatalog,
    isLoading: false,
    error: null,

    loadCatalog: async () => {
      set((state) => {
        state.isLoading = true;
        state.error = null;
      });

      try {
        const [libraries, playlists] = await Promise.all([readLibraries(), readPlaylists()]);
        set((state) => {
          state.catalog = { libraries, playlists };
          state.isLoading = false;
        });
      } catch (error) {
        set((state) => {
          state.error = String(error);
          state.isLoading = false;
        });
      }
    },

    saveCatalog: async () => {
      try {
        const { libraries, playlists } = get().catalog;
        await Promise.all([writeLibraries(libraries), writePlaylists(playlists)]);
      } catch (error) {
        set((state) => {
          state.error = String(error);
        });
      }
    },

    remapPresentationPaths: (oldBase, newBase) => {
      const normalizePath = (path: string) => path.replace(/\\/g, '/');
      const isAbsolutePath = (path: string) => /^[a-zA-Z]:[\\/]|^\//.test(path);
      const normalizedOld = normalizePath(oldBase);
      const normalizedNew = normalizePath(newBase);
      const remapPath = (path: string) => {
        if (!isAbsolutePath(path)) return path;
        const normalizedPath = normalizePath(path);
        if (!normalizedPath.startsWith(`${normalizedOld}/`)) return path;
        return `${normalizedNew}/${normalizedPath.slice(normalizedOld.length + 1)}`;
      };

      set((state) => {
        state.catalog.libraries = state.catalog.libraries.map((library) => ({
          ...library,
          presentations: library.presentations.map((presentation) => ({
            ...presentation,
            path: remapPath(presentation.path),
          })),
        }));
        state.catalog.playlists = state.catalog.playlists.map((playlist) => ({
          ...playlist,
          items: playlist.items.map((item) => ({
            ...item,
            path: remapPath(item.path),
          })),
        }));
      });
      get().saveCatalog();
    },

    addLibrary: (name: string) => {
      const library = createLibrary(name);
      set((state) => {
        state.catalog.libraries.push(library);
      });
      get().saveCatalog();
      return library;
    },

    updateLibrary: (id: string, updates: Partial<Library>) => {
      set((state) => {
        const index = state.catalog.libraries.findIndex((l) => l.id === id);
        if (index !== -1) {
          Object.assign(state.catalog.libraries[index], updates, {
            updatedAt: new Date().toISOString(),
          });
        }
      });
      get().saveCatalog();
    },

    deleteLibrary: (id: string) => {
      set((state) => {
        state.catalog.libraries = state.catalog.libraries.filter((l) => l.id !== id);
      });
      get().saveCatalog();
    },

    addPresentationToLibrary: (libraryId: string, presentation: PresentationRef) => {
      set((state) => {
        const library = state.catalog.libraries.find((l) => l.id === libraryId);
        if (library) {
          // Don't add duplicates
          if (!library.presentations.some((p) => p.path === presentation.path)) {
            library.presentations.push(presentation);
            library.updatedAt = new Date().toISOString();
          }
        }
      });
      get().saveCatalog();
    },

    removePresentationFromLibrary: (libraryId: string, path: string) => {
      set((state) => {
        const library = state.catalog.libraries.find((l) => l.id === libraryId);
        if (library) {
          library.presentations = library.presentations.filter((p) => p.path !== path);
          library.updatedAt = new Date().toISOString();
        }
      });
      get().saveCatalog();
    },

    addPlaylist: (name: string) => {
      const playlist = createPlaylist(name);
      set((state) => {
        state.catalog.playlists.push(playlist);
      });
      get().saveCatalog();
      return playlist;
    },

    addLinkedPlaylist: (setData: LinkedSetData, items?: PresentationRef[]) => {
      // Check if a playlist already exists for this set
      const existing = get().catalog.playlists.find(
        (p) => p.externalSet?.setId === setData.setId
      );
      if (existing) {
        // Update existing playlist
        set((state) => {
          const playlist = state.catalog.playlists.find((p) => p.id === existing.id);
          if (playlist) {
            playlist.name = setData.title || playlist.name;
            if (items) {
              playlist.items = items;
            }
            playlist.updatedAt = new Date().toISOString();
            if (playlist.sync) {
              playlist.sync.status = 'synced';
              playlist.sync.lastSyncAttempt = new Date().toISOString();
            }
            if (playlist.externalSet) {
              playlist.externalSet.syncedAt = new Date().toISOString();
              playlist.externalSet.serviceDate = setData.serviceDate;
            }
          }
        });
        get().saveCatalog();
        return existing;
      }

      // Create new linked playlist
      const playlist = createLinkedPlaylist(setData);
      if (items) {
        playlist.items = items;
      }
      set((state) => {
        state.catalog.playlists.push(playlist);
      });
      get().saveCatalog();
      return playlist;
    },

    updatePlaylist: (id: string, updates: Partial<Playlist>) => {
      set((state) => {
        const index = state.catalog.playlists.findIndex((p) => p.id === id);
        if (index !== -1) {
          Object.assign(state.catalog.playlists[index], updates, {
            updatedAt: new Date().toISOString(),
          });
        }
      });
      get().saveCatalog();
    },

    deletePlaylist: (id: string) => {
      set((state) => {
        state.catalog.playlists = state.catalog.playlists.filter((p) => p.id !== id);
      });
      get().saveCatalog();
    },

    addToPlaylist: (playlistId: string, presentation: PresentationRef) => {
      set((state) => {
        const playlist = state.catalog.playlists.find((p) => p.id === playlistId);
        if (playlist) {
          playlist.items.push(presentation);
          playlist.updatedAt = new Date().toISOString();
        }
      });
      get().saveCatalog();
    },

    removeFromPlaylist: (playlistId: string, path: string) => {
      set((state) => {
        const playlist = state.catalog.playlists.find((p) => p.id === playlistId);
        if (playlist) {
          playlist.items = playlist.items.filter((p) => p.path !== path);
          playlist.updatedAt = new Date().toISOString();
        }
      });
      get().saveCatalog();
    },

    reorderPlaylist: (playlistId: string, fromIndex: number, toIndex: number) => {
      set((state) => {
        const playlist = state.catalog.playlists.find((p) => p.id === playlistId);
        if (playlist && fromIndex >= 0 && toIndex >= 0) {
          const [item] = playlist.items.splice(fromIndex, 1);
          playlist.items.splice(toIndex, 0, item);
          playlist.updatedAt = new Date().toISOString();

          // If this is a linked playlist, mark as pending sync
          if (playlist.externalSet && playlist.sync) {
            playlist.sync.status = 'pending';
            playlist.sync.lastSyncAttempt = new Date().toISOString();
          }
        }
      });
      get().saveCatalog();
    },

    findLinkedPlaylist: (setId: string) => {
      return get().catalog.playlists.find((p) => p.externalSet?.setId === setId);
    },

    updatePlaylistSyncStatus: (playlistId: string, status: ExternalSyncStatus, conflictUrl?: string) => {
      set((state) => {
        const playlist = state.catalog.playlists.find((p) => p.id === playlistId);
        if (playlist && playlist.sync) {
          playlist.sync.status = status;
          playlist.sync.lastSyncAttempt = new Date().toISOString();
          if (conflictUrl !== undefined) {
            playlist.sync.conflictUrl = conflictUrl;
          }
        }
      });
      get().saveCatalog();
    },
  }))
);
