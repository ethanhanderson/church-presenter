/**
 * Music Manager Store - manages state for Music Manager integration
 */

import { create } from 'zustand';
import { immer } from 'zustand/middleware/immer';
import type {
  MusicGroup,
  Song,
  Set,
  SetWithSongs,
  SongWithAssets,
  PresenterLink,
  SyncRequest,
} from '../supabase';
import * as api from '../musicManager/api';

interface MusicManagerState {
  // Connection state
  isConnected: boolean;
  connectionError: string | null;

  // Data
  groups: MusicGroup[];
  songs: Song[];
  sets: Set[];
  currentGroup: MusicGroup | null;
  currentSong: SongWithAssets | null;
  currentSet: SetWithSongs | null;

  // Loading states
  isLoadingGroups: boolean;
  isLoadingSongs: boolean;
  isLoadingSets: boolean;
  isLoadingSong: boolean;
  isLoadingSet: boolean;

  // Search
  songSearchQuery: string;
  songSearchResults: Song[];
  isSearching: boolean;

  // Sync state
  presenterLinks: Map<string, PresenterLink>;
  pendingSyncRequests: SyncRequest[];

  // Actions
  testConnection: () => Promise<boolean>;
  
  // Groups
  loadGroups: () => Promise<void>;
  selectGroup: (groupId: string | null) => Promise<void>;

  // Songs
  loadSongs: (groupId?: string) => Promise<void>;
  loadSong: (songId: string) => Promise<SongWithAssets | null>;
  searchSongs: (query: string) => Promise<void>;
  clearSearch: () => void;

  // Sets
  loadSets: (groupId: string) => Promise<void>;
  loadSet: (setId: string) => Promise<SetWithSongs | null>;

  // Sync
  fetchLinkStatus: (localPresentationId: string) => Promise<PresenterLink | null>;
  updateLinkStatus: (localPresentationId: string, link: PresenterLink) => void;
  requestSetUpdate: (setId: string, newOrder: { songId: string; position: number }[]) => Promise<SyncRequest>;
}

export const useMusicManagerStore = create<MusicManagerState>()(
  immer((set, get) => ({
    // Initial state
    isConnected: false,
    connectionError: null,

    groups: [],
    songs: [],
    sets: [],
    currentGroup: null,
    currentSong: null,
    currentSet: null,

    isLoadingGroups: false,
    isLoadingSongs: false,
    isLoadingSets: false,
    isLoadingSong: false,
    isLoadingSet: false,

    songSearchQuery: '',
    songSearchResults: [],
    isSearching: false,

    presenterLinks: new Map(),
    pendingSyncRequests: [],

    // Test connection by fetching groups
    testConnection: async () => {
      try {
        const groups = await api.fetchMusicGroups();
        set((state) => {
          state.isConnected = true;
          state.connectionError = null;
          state.groups = groups;
        });
        return true;
      } catch (error) {
        set((state) => {
          state.isConnected = false;
          state.connectionError = error instanceof Error ? error.message : 'Connection failed';
        });
        return false;
      }
    },

    // Load all groups
    loadGroups: async () => {
      set((state) => {
        state.isLoadingGroups = true;
      });

      try {
        const groups = await api.fetchMusicGroups();
        set((state) => {
          state.groups = groups;
          state.isConnected = true;
          state.connectionError = null;
          state.isLoadingGroups = false;
        });
      } catch (error) {
        set((state) => {
          state.connectionError = error instanceof Error ? error.message : 'Failed to load groups';
          state.isLoadingGroups = false;
        });
      }
    },

    // Select a group and load its songs/sets
    selectGroup: async (groupId: string | null) => {
      if (!groupId) {
        set((state) => {
          state.currentGroup = null;
          state.sets = [];
          state.isLoadingSongs = true;
        });
        try {
          const groups = get().groups;
          const results = await Promise.all(
            groups.map((group) => api.fetchSongs(group.id))
          );
          const merged = new Map<string, Song>();
          for (const groupSongs of results) {
            for (const song of groupSongs) {
              merged.set(song.id, song);
            }
          }
          set((state) => {
            state.songs = Array.from(merged.values()).sort((a, b) =>
              a.title.localeCompare(b.title)
            );
            state.isLoadingSongs = false;
          });
        } catch (error) {
          set((state) => {
            state.connectionError = error instanceof Error ? error.message : 'Failed to load songs';
            state.isLoadingSongs = false;
          });
        }
        return;
      }

      const group = get().groups.find((g) => g.id === groupId);
      if (group) {
        set((state) => {
          state.currentGroup = group;
        });

        // Load songs and sets for this group
        await Promise.all([
          get().loadSongs(groupId),
          get().loadSets(groupId),
        ]);
      }
    },

    // Load songs
    loadSongs: async (groupId?: string) => {
      set((state) => {
        state.isLoadingSongs = true;
      });

      try {
        const songs = await api.fetchSongs(groupId);
        set((state) => {
          state.songs = songs;
          state.isLoadingSongs = false;
        });
      } catch (error) {
        set((state) => {
          state.connectionError = error instanceof Error ? error.message : 'Failed to load songs';
          state.isLoadingSongs = false;
        });
      }
    },

    // Load a single song with assets
    loadSong: async (songId: string) => {
      set((state) => {
        state.isLoadingSong = true;
      });

      try {
        const song = await api.fetchSongWithAssets(songId);
        set((state) => {
          state.currentSong = song;
          state.isLoadingSong = false;
        });
        return song;
      } catch (error) {
        set((state) => {
          state.connectionError = error instanceof Error ? error.message : 'Failed to load song';
          state.isLoadingSong = false;
        });
        return null;
      }
    },

    // Search songs
    searchSongs: async (query: string) => {
      set((state) => {
        state.songSearchQuery = query;
        state.isSearching = true;
      });

      if (!query.trim()) {
        set((state) => {
          state.songSearchResults = [];
          state.isSearching = false;
        });
        return;
      }

      try {
        const currentGroupId = get().currentGroup?.id;
        const results = await api.searchSongs(query, currentGroupId);
        set((state) => {
          state.songSearchResults = results;
          state.isSearching = false;
        });
      } catch (error) {
        set((state) => {
          state.connectionError = error instanceof Error ? error.message : 'Search failed';
          state.isSearching = false;
        });
      }
    },

    clearSearch: () => {
      set((state) => {
        state.songSearchQuery = '';
        state.songSearchResults = [];
      });
    },

    // Load sets for a group
    loadSets: async (groupId: string) => {
      set((state) => {
        state.isLoadingSets = true;
      });

      try {
        const sets = await api.fetchSets(groupId);
        set((state) => {
          state.sets = sets;
          state.isLoadingSets = false;
        });
      } catch (error) {
        set((state) => {
          state.connectionError = error instanceof Error ? error.message : 'Failed to load sets';
          state.isLoadingSets = false;
        });
      }
    },

    // Load a single set with songs
    loadSet: async (setId: string) => {
      set((state) => {
        state.isLoadingSet = true;
      });

      try {
        const setData = await api.fetchSetWithSongs(setId);
        set((state) => {
          state.currentSet = setData;
          state.isLoadingSet = false;
        });
        return setData;
      } catch (error) {
        set((state) => {
          state.connectionError = error instanceof Error ? error.message : 'Failed to load set';
          state.isLoadingSet = false;
        });
        return null;
      }
    },

    // Fetch link status for a presentation
    fetchLinkStatus: async (localPresentationId: string) => {
      try {
        const link = await api.fetchPresenterLink(localPresentationId);
        if (link) {
          set((state) => {
            state.presenterLinks.set(localPresentationId, link);
          });
        }
        return link;
      } catch (error) {
        console.error('Failed to fetch link status:', error);
        return null;
      }
    },

    // Update link status in local state
    updateLinkStatus: (localPresentationId: string, link: PresenterLink) => {
      set((state) => {
        state.presenterLinks.set(localPresentationId, link);
      });
    },

    // Request a set update (for playlist reorder sync)
    requestSetUpdate: async (setId: string, newOrder: { songId: string; position: number }[]) => {
      const currentGroup = get().currentGroup;
      if (!currentGroup) {
        throw new Error('No group selected');
      }

      const request = await api.requestSetUpdate(currentGroup.id, setId, newOrder);
      set((state) => {
        state.pendingSyncRequests.push(request);
      });
      return request;
    },
  }))
);
