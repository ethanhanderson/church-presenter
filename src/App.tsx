/**
 * Church Presenter - Main Application
 */

import { useEffect, useState, useCallback, useRef } from 'react';
import { open, save } from '@tauri-apps/plugin-dialog';
import { listen } from '@tauri-apps/api/event';
import { restoreStateCurrent, StateFlags } from '@tauri-apps/plugin-window-state';
import { check } from '@tauri-apps/plugin-updater';
import { TooltipProvider } from '@/components/ui/tooltip';
import { AppMenubar } from '@/components/layout/AppMenubar';
import { TopTabNav } from '@/components/layout/TopTabNav';
import { ShowPage } from '@/pages/ShowPage';
import { EditPage } from '@/pages/EditPage';
import { ReflowPage } from '@/pages/ReflowPage';
import {
  NewPresentationDialog,
  NameDialog,
  SettingsDialog,
  DeleteConfirmDialog,
  SongBrowserDialog,
  SetBrowserDialog,
  SaveConflictDialog,
  SaveReportDialog,
  UpdateDialog,
} from '@/components/dialogs';
import {
  useCatalogStore,
  useSettingsStore,
  useEditorStore,
  useLiveStore,
  useMusicManagerStore,
  useShowStore,
  useWorkspaceStore,
} from '@/lib/stores';
import { createSongPresentation } from '@/lib/models';
import type { Library, Playlist, SlideType, PresentationRef, Presentation } from '@/lib/models';
import type { Song, SetWithSongs } from '@/lib/supabase';
import { fetchSongArrangementSlides, fetchSong } from '@/lib/musicManager';
import { useAutoSave, resolveConflict } from '@/hooks';
import {
  allowMediaLibraryDir,
  closeOutputWindows,
  getMonitors,
  openOutputWindows,
  openBundle,
  saveBundle,
  setContentDir,
  type MonitorInfo,
} from '@/lib/tauri-api';
import {
  generateSongPresentationPath,
  getDocumentsDataDirPath,
  getMediaLibraryDirPath,
  getSongPresentationRelativePath,
  initializeAppData,
  toDocumentsRelativePath,
} from '@/lib/services/appDataService';
import type { SaveReportItem } from '@/components/dialogs/SaveReportDialog';

type UpdateInfo = Awaited<ReturnType<typeof check>>;

interface DeleteTarget {
  type: 'library' | 'playlist';
  id: string;
  name: string;
}

interface ConflictData {
  localVersion: Presentation;
  remoteVersion: Presentation;
  filePath: string;
}

export default function App() {
  // Stores
  const {
    catalog,
    loadCatalog,
    addLibrary,
    updateLibrary,
    deleteLibrary,
    addPresentationToLibrary,
    addPlaylist,
    addLinkedPlaylist,
    updatePlaylist,
    deletePlaylist,
    removePresentationFromLibrary,
    removeFromPlaylist,
  } = useCatalogStore();
  const {
    loadSettings,
    saveSettings,
    updateSettings,
    removeRecentFile,
    error: settingsError,
    settings,
  } = useSettingsStore();
  const {
    presentation,
    filePath,
    closePresentation,
    newPresentation,
    openPresentation,
    savePresentation,
    undo,
    redo,
    linkExternalSong,
  } = useEditorStore();
  const {
    setupListeners,
    isLive,
    presentation: livePresentation,
    goLive,
    endLive,
    goToSlide,
  } = useLiveStore();
  const { selectedSlideId } = useShowStore();
  const { groups } = useMusicManagerStore();

  // Conflict state for auto-save
  const [conflictData, setConflictData] = useState<ConflictData | null>(null);
  const [conflictDialogOpen, setConflictDialogOpen] = useState(false);

  // Auto-save hook - saves quickly (300ms) after edits stop
  const { saveNow } = useAutoSave({
    debounceMs: 300,
    enabled: true,
    onConflict: (conflict) => {
      setConflictData(conflict);
      setConflictDialogOpen(true);
    },
    onSaved: () => {
      // Update library/playlist references if there's a pending association
      const { filePath } = useEditorStore.getState();
      if (pendingLibraryId && filePath) {
        void finalizeLibraryAssociation(pendingLibraryId, filePath);
      }
    },
    onError: (error) => {
      console.error('Auto-save error:', error);
    },
  });

  // Handle conflict resolution
  const handleResolveConflict = useCallback(async (choice: 'local' | 'remote') => {
    if (!conflictData) return;
    
    try {
      await resolveConflict(choice, conflictData);
      setConflictData(null);
    } catch (error) {
      console.error('Failed to resolve conflict:', error);
    }
  }, [conflictData]);

  const {
    activePage,
    selectedLibraryId,
    selectedPlaylistId,
    selectedPresentationPath,
    loadWorkspace,
    setActivePage,
    setSelectedLibraryId,
    setSelectedPlaylistId,
    setSelectedPresentationPath,
  } = useWorkspaceStore();

  // Dialog state
  const [newPresentationOpen, setNewPresentationOpen] = useState(false);
  const [newLibraryOpen, setNewLibraryOpen] = useState(false);
  const [newPlaylistOpen, setNewPlaylistOpen] = useState(false);
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [songBrowserOpen, setSongBrowserOpen] = useState(false);
  const [songBrowserMode, setSongBrowserMode] = useState<'import' | 'link'>('import');
  const [setBrowserOpen, setSetBrowserOpen] = useState(false);
  const [editLibrary, setEditLibrary] = useState<Library | null>(null);
  const [editPlaylist, setEditPlaylist] = useState<Playlist | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<DeleteTarget | null>(null);
  const [pendingNewPresentationOpen, setPendingNewPresentationOpen] = useState(false);
  const [pendingLibraryId, setPendingLibraryId] = useState<string | null>(null);
  const [saveReportOpen, setSaveReportOpen] = useState(false);
  const [saveReportItems, setSaveReportItems] = useState<SaveReportItem[]>([]);
  const [updateDialogOpen, setUpdateDialogOpen] = useState(false);
  const [pendingUpdate, setPendingUpdate] = useState<UpdateInfo>(null);
  const [isInitialized, setIsInitialized] = useState(false);
  const pendingOpenPathsRef = useRef<string[]>([]);
  const hasAppliedStartupSelectionRef = useRef(false);
  const hasCheckedUpdatesRef = useRef(false);
  const [monitors, setMonitors] = useState<MonitorInfo[]>([]);

  const ensureSongPresentation = useCallback(async (
    song: Song,
    lyrics?: string | null,
    arrangementSlides?: Awaited<ReturnType<typeof fetchSongArrangementSlides>> | null
  ) => {
    const savePath = await generateSongPresentationPath(song.id);
    const relativePath = getSongPresentationRelativePath(song.id);

    try {
      const existing = await openBundle(savePath);
      const hasOnlyTitleSlide = existing.slides.length <= 1;
      const canRebuild = arrangementSlides && arrangementSlides.length > 0;

      if (hasOnlyTitleSlide && canRebuild) {
        const rebuilt = createSongPresentation({
          id: song.id,
          title: song.title,
          groupId: song.group_id,
          lyrics,
          arrangementSlides,
        });
        rebuilt.manifest.presentationId = existing.manifest.presentationId;
        rebuilt.manifest.createdAt = existing.manifest.createdAt;
        rebuilt.manifest.externalSong = existing.manifest.externalSong ?? rebuilt.manifest.externalSong;
        rebuilt.manifest.sync = existing.manifest.sync ?? rebuilt.manifest.sync;
        rebuilt.manifest.updatedAt = new Date().toISOString();
        await saveBundle(savePath, rebuilt);

        return {
          presentation: rebuilt,
          savePath,
          relativePath,
          updatedAt: rebuilt.manifest.updatedAt,
        };
      }

      return {
        presentation: existing,
        savePath,
        relativePath,
        updatedAt: existing.manifest.updatedAt || new Date().toISOString(),
      };
    } catch {
      const presentation = createSongPresentation({
        id: song.id,
        title: song.title,
        groupId: song.group_id,
        lyrics,
        arrangementSlides,
      });
      presentation.manifest.updatedAt = new Date().toISOString();
      await saveBundle(savePath, presentation);
      return {
        presentation,
        savePath,
        relativePath,
        updatedAt: presentation.manifest.updatedAt,
      };
    }
  }, []);

  // Initialize on mount
  useEffect(() => {
    const initialize = async () => {
      try {
        await loadSettings();
        const isTauriApp = '__TAURI_INTERNALS__' in window || '__TAURI__' in window;
        const storedSettings = useSettingsStore.getState().settings;
        const currentContentDir = await getDocumentsDataDirPath();
        if (storedSettings.contentDir && storedSettings.contentDir !== currentContentDir) {
          await setContentDir(storedSettings.contentDir, {
            moveExisting: false,
            mediaLibraryDir: storedSettings.mediaLibraryDir ?? null,
          });
        }

        const syncedContentDir = await getDocumentsDataDirPath();
        const mediaLibraryDir = await getMediaLibraryDirPath();
        if (
          syncedContentDir !== storedSettings.contentDir ||
          mediaLibraryDir !== storedSettings.mediaLibraryDir
        ) {
          updateSettings({
            contentDir: syncedContentDir,
            mediaLibraryDir,
          });
        }
        if (isTauriApp) {
          await allowMediaLibraryDir(mediaLibraryDir);
        }

        await initializeAppData();
        await loadCatalog();
        await loadWorkspace();
      } catch (error) {
        console.error('Failed to initialize app data:', error);
        await loadSettings();
        await loadCatalog();
        await loadWorkspace();
      } finally {
        setIsInitialized(true);
      }
    };

    void initialize();
    
    // Setup live event listeners
    const isTauriApp = '__TAURI_INTERNALS__' in window || '__TAURI__' in window;
    const unlisten = isTauriApp ? setupListeners() : null;
    return () => {
      if (unlisten) {
        unlisten.then((fn) => fn());
      }
    };
  }, [loadCatalog, loadSettings, loadWorkspace, setupListeners, updateSettings]);

  useEffect(() => {
    const isTauriApp = '__TAURI_INTERNALS__' in window || '__TAURI__' in window;
    if (!isTauriApp) return;
    restoreStateCurrent(StateFlags.ALL).catch((error) => {
      console.warn('Failed to restore window state:', error);
    });
  }, []);

  useEffect(() => {
    const isTauriApp = '__TAURI_INTERNALS__' in window || '__TAURI__' in window;
    if (!isTauriApp) return;

    let active = true;
    const fetchMonitors = () => {
      getMonitors()
        .then((next) => {
          if (active) setMonitors(next);
        })
        .catch((error) => {
          console.warn('Failed to load monitors:', error);
        });
    };

    fetchMonitors();
    const interval = window.setInterval(fetchMonitors, 3000);
    window.addEventListener('focus', fetchMonitors);
    return () => {
      active = false;
      window.clearInterval(interval);
      window.removeEventListener('focus', fetchMonitors);
    };
  }, []);

  useEffect(() => {
    const isTauriApp = '__TAURI_INTERNALS__' in window || '__TAURI__' in window;
    if (!isTauriApp) return;

    if (!settings.output.audienceEnabled) {
      void closeOutputWindows();
      return;
    }

    if (monitors.length === 0) {
      void closeOutputWindows();
      return;
    }

    const configured = (settings.output.monitorIds || [])
      .map((id) => Number(id))
      .filter((id) => monitors.some((monitor) => monitor.index === id));

    if (configured.length === 0) {
      void closeOutputWindows();
      return;
    }

    void openOutputWindows(configured);
  }, [monitors, settings.output.audienceEnabled, settings.output.monitorIds]);

  useEffect(() => {
    const shouldBeLive = settings.output.audienceEnabled && !!presentation;
    if (!shouldBeLive) {
      if (isLive) {
        void endLive();
      }
      return;
    }

    const samePresentation =
      livePresentation?.manifest.presentationId === presentation?.manifest.presentationId;
    if (!isLive || !samePresentation) {
      void goLive(presentation!, filePath);
    }
  }, [
    settings.output.audienceEnabled,
    presentation,
    filePath,
    isLive,
    livePresentation,
    goLive,
    endLive,
  ]);

  useEffect(() => {
    if (!isLive || !livePresentation || !selectedSlideId) return;
    const hasSlide = livePresentation.slides.some((slide) => slide.id === selectedSlideId);
    if (!hasSlide) return;
    goToSlide(selectedSlideId);
  }, [isLive, livePresentation, selectedSlideId, goToSlide]);

  useEffect(() => {
    if (!isInitialized) return;
    if (!settings.updates.autoCheck) return;
    if (hasCheckedUpdatesRef.current) return;

    const isTauriApp = '__TAURI_INTERNALS__' in window || '__TAURI__' in window;
    if (!isTauriApp) return;

    hasCheckedUpdatesRef.current = true;
    const checkedAt = new Date().toISOString();
    updateSettings({
      updates: { ...settings.updates, lastCheckedAt: checkedAt },
    });
    check()
      .then((update) => {
        if (update) {
          setPendingUpdate(update);
          setUpdateDialogOpen(true);
        }
      })
      .catch((error) => {
        console.warn('Update check failed:', error);
      });
  }, [isInitialized, settings.updates, updateSettings]);

  const handleRequestNewPresentation = useCallback(() => {
    if (catalog.libraries.length === 0) {
      setPendingNewPresentationOpen(true);
      setNewLibraryOpen(true);
      return;
    }
    setNewPresentationOpen(true);
  }, [catalog.libraries.length]);

  // Keyboard shortcuts
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.ctrlKey || e.metaKey) {
        switch (e.key.toLowerCase()) {
          case 'n':
            e.preventDefault();
            handleRequestNewPresentation();
            break;
          case 'o':
            e.preventDefault();
            handleOpenPresentation();
            break;
          case 's':
            e.preventDefault();
            if (e.shiftKey) {
              handleSaveAs();
            } else {
              handleSave();
            }
            break;
          case 'z':
            e.preventDefault();
            undo();
            break;
          case 'y':
            e.preventDefault();
            redo();
            break;
          case ',':
            e.preventDefault();
            setSettingsOpen(true);
            break;
        }
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [handleRequestNewPresentation, undo, redo]);

  // Handlers

  const getLibraryForGroup = useCallback((groupId: string) => {
    const groupName = groups.find((group) => group.id === groupId)?.name?.trim();
    const resolvedName = groupName || 'Music Manager';
    const existingLibrary = catalog.libraries.find(
      (library) => library.name.toLowerCase() === resolvedName.toLowerCase()
    );
    return existingLibrary || addLibrary(resolvedName);
  }, [addLibrary, catalog.libraries, groups]);

  const finalizeLibraryAssociation = useCallback(async (libraryId: string, path: string) => {
    const { presentation: currentPresentation } = useEditorStore.getState();
    if (!currentPresentation) return;

    const resolvedPath = await toDocumentsRelativePath(path);
    addPresentationToLibrary(libraryId, {
      path: resolvedPath,
      title: currentPresentation.manifest.title,
      updatedAt: currentPresentation.manifest.updatedAt,
    });
    setPendingLibraryId(null);
  }, [addPresentationToLibrary, toDocumentsRelativePath]);

  const handleNewPresentation = useCallback((title: string, type: SlideType, libraryId: string | null) => {
    newPresentation(title);
    setActivePage('edit');
    setSelectedPresentationPath(null);
    if (type === 'song') {
      setPendingLibraryId(libraryId);
      if (libraryId) {
        setSelectedLibraryId(libraryId);
        setSelectedPlaylistId(null);
      }
    } else {
      setPendingLibraryId(null);
    }
  }, [newPresentation, setActivePage, setSelectedPresentationPath, setSelectedLibraryId, setSelectedPlaylistId]);

  const handleImportSongs = useCallback(async (songs: Song[]) => {
    if (songs.length === 0) return;

    const libraryByGroup = new Map<string, Library>();
    const ensureLibraryForSong = (song: Song) => {
      if (!libraryByGroup.has(song.group_id)) {
        libraryByGroup.set(song.group_id, getLibraryForGroup(song.group_id));
      }
      return libraryByGroup.get(song.group_id)!;
    };

    const addSongToLibrary = (song: Song, updatedAt: string) => {
      const library = ensureLibraryForSong(song);
      const relativePath = getSongPresentationRelativePath(song.id);
      addPresentationToLibrary(library.id, {
        path: relativePath,
        title: song.title,
        updatedAt,
      });
      return library;
    };

    // For a single song, keep the existing UX: open editor immediately.
    if (songs.length === 1) {
      const song = songs[0]!;
      let lyrics: string | null = null;
      let arrangementSlides: Awaited<ReturnType<typeof fetchSongArrangementSlides>> = null;
      try {
        arrangementSlides = await fetchSongArrangementSlides(song.id);
      } catch (error) {
        console.warn('Could not fetch arrangements for song:', error);
      }
      const { presentation, savePath, updatedAt } = await ensureSongPresentation(
        song,
        lyrics,
        arrangementSlides
      );
      const library = addSongToLibrary(song, updatedAt);

      useEditorStore.setState({
        presentation,
        filePath: savePath,
        isDirty: false,
        selection: { slideIds: [], layerIds: [] },
        activeSlideId: presentation.slides[0]?.id || null,
        undoStack: [],
        redoStack: [],
        pendingMedia: new Map(),
        autoSave: { status: 'saved', lastSaved: updatedAt, lastError: null },
      });

      setSelectedLibraryId(library.id);
      setSelectedPlaylistId(null);
      setActivePage('edit');
      setSelectedPresentationPath(savePath);
      
      // Presentation is persisted on import
      return;
    }

    // For multiple songs, create a playlist and persist each song presentation.
    const playlist = addPlaylist(`Imported Songs (${songs.length})`);
    const items: PresentationRef[] = [];
    for (const song of songs) {
      let lyrics: string | null = null;
      let arrangementSlides: Awaited<ReturnType<typeof fetchSongArrangementSlides>> = null;
      try {
        arrangementSlides = await fetchSongArrangementSlides(song.id);
      } catch (error) {
        console.warn('Could not fetch arrangements for song:', error);
      }

      const { relativePath, updatedAt } = await ensureSongPresentation(song, lyrics, arrangementSlides);
      items.push({ path: relativePath, title: song.title, updatedAt });
      addSongToLibrary(song, updatedAt);
    }
    updatePlaylist(playlist.id, { items });

    setSelectedPlaylistId(playlist.id);
    setSelectedLibraryId(null);
    setActivePage('show');
  }, [
    addPlaylist,
    addPresentationToLibrary,
    ensureSongPresentation,
    getLibraryForGroup,
    setActivePage,
    setSelectedLibraryId,
    setSelectedPlaylistId,
    setSelectedPresentationPath,
    updatePlaylist,
  ]);

  const handleLinkSong = useCallback((song: Song) => {
    linkExternalSong(song.id, song.group_id);
  }, [linkExternalSong]);

  const handleImportSets = useCallback(async (setsToImport: SetWithSongs[]) => {
    if (setsToImport.length === 0) return;

    const createdPlaylists = [];

    for (const set of setsToImport) {
      // Create presentation refs for each song in the set
      const items: PresentationRef[] = [];
      
      if (set.set_songs) {
        for (const setSong of set.set_songs) {
          if (setSong.songs) {
            let lyrics: string | null = null;
            let arrangementSlides: Awaited<ReturnType<typeof fetchSongArrangementSlides>> = null;
            try {
              arrangementSlides = await fetchSongArrangementSlides(setSong.songs.id);
            } catch (error) {
              console.warn('Could not fetch arrangements for song:', error);
            }

            const { relativePath, updatedAt } = await ensureSongPresentation(
              setSong.songs,
              lyrics,
              arrangementSlides
            );
            items.push({
              path: relativePath,
              title: setSong.songs.title,
              updatedAt,
            });
          }
        }
      }

      // Create or update the linked playlist
      const playlist = addLinkedPlaylist(
        {
          setId: set.id,
          groupId: set.group_id,
          title: set.notes?.trim() || '',
          serviceDate: set.service_date,
        },
        items
      );
      createdPlaylists.push(playlist);
    }

    // Select the first imported playlist
    const first = createdPlaylists[0];
    if (first) setSelectedPlaylistId(first.id);
    setSelectedLibraryId(null);
    setActivePage('show');
  }, [addLinkedPlaylist, ensureSongPresentation, setActivePage, setSelectedLibraryId, setSelectedPlaylistId]);

  const handleOpenPresentation = useCallback(async () => {
    const file = await open({
      filters: [{ name: 'Church Presenter', extensions: ['cpres'] }],
    });
    if (file) {
      await openPresentation(file);
      setActivePage('edit');
      setSelectedPresentationPath(file);
    }
  }, [ensureSongPresentation, openPresentation, setActivePage, setSelectedPresentationPath]);

  const openPresentationFromPath = useCallback(async (path: string) => {
    try {
      await openPresentation(path);
      setSelectedPresentationPath(path);
      return true;
    } catch (error) {
      const normalizedPath = path.replace(/\\/g, '/');
      const match = normalizedPath.match(/(?:presentations\/songs|songs)\/([^/]+)\.cpres$/);
      const songId = match?.[1];
      if (!songId) {
        console.error('Failed to open presentation:', error);
        return false;
      }

      try {
        const song = await fetchSong(songId);
        if (!song) {
          console.error('Song not found for presentation:', songId);
          return false;
        }

        let lyrics: string | null = null;
        let arrangementSlides: Awaited<ReturnType<typeof fetchSongArrangementSlides>> = null;
        try {
          arrangementSlides = await fetchSongArrangementSlides(song.id);
        } catch (lyricsError) {
          console.warn('Could not fetch arrangements for song:', lyricsError);
        }

        const { presentation, savePath, updatedAt } = await ensureSongPresentation(
          song,
          lyrics,
          arrangementSlides
        );

        useEditorStore.setState({
          presentation,
          filePath: savePath,
          isDirty: false,
          selection: { slideIds: [], layerIds: [] },
          activeSlideId: presentation.slides[0]?.id || null,
          undoStack: [],
          redoStack: [],
          pendingMedia: new Map(),
          autoSave: { status: 'saved', lastSaved: updatedAt, lastError: null },
        });

        setSelectedPresentationPath(path);
        return true;
      } catch (fallbackError) {
        console.error('Failed to rebuild song presentation:', fallbackError);
        return false;
      }
    }
  }, [ensureSongPresentation, openPresentation, setSelectedPresentationPath]);

  const handleOpenPresentationPath = useCallback(async (path: string) => {
    await openPresentationFromPath(path);
  }, [openPresentationFromPath]);

  useEffect(() => {
    const isTauriApp = '__TAURI_INTERNALS__' in window || '__TAURI__' in window;
    if (!isTauriApp) return;

    const unlisten = listen<{ path?: string }>('app:open-path', (event) => {
      const path = event.payload?.path;
      if (!path) return;

      if (!isInitialized) {
        pendingOpenPathsRef.current.push(path);
        return;
      }

      void openPresentationFromPath(path);
    });

    return () => {
      unlisten.then((fn) => fn()).catch(() => undefined);
    };
  }, [isInitialized, openPresentationFromPath]);

  useEffect(() => {
    if (!isInitialized) return;
    if (pendingOpenPathsRef.current.length === 0) return;

    const queued = [...pendingOpenPathsRef.current];
    pendingOpenPathsRef.current = [];
    queued.forEach((path) => {
      void openPresentationFromPath(path);
    });
  }, [isInitialized, openPresentationFromPath]);

  useEffect(() => {
    if (!isInitialized) return;
    if (hasAppliedStartupSelectionRef.current) return;
    if (pendingOpenPathsRef.current.length > 0) return;

    const normalizePath = (path: string) => path.replace(/\\/g, '/');
    const parseTimestamp = (timestamp?: string | null) => {
      if (!timestamp) return 0;
      const parsed = Date.parse(timestamp);
      return Number.isNaN(parsed) ? 0 : parsed;
    };

    const findLibraryPresentation = (path: string) => {
      const normalized = normalizePath(path);
      for (const library of catalog.libraries) {
        const match = library.presentations.find((presentation) => {
          const presentationPath = normalizePath(presentation.path);
          return presentationPath === normalized || normalized.endsWith(presentationPath);
        });
        if (match) {
          return { library, presentation: match };
        }
      }
      return null;
    };

    const selectPlaylist = (playlist: Playlist) => {
      setActivePage('show');
      setSelectedPlaylistId(playlist.id);
      setSelectedLibraryId(null);
      setSelectedPresentationPath(playlist.items[0]?.path ?? null);
    };

    const selectLibraryPresentation = (library: Library, presentation: PresentationRef) => {
      setActivePage('show');
      setSelectedLibraryId(library.id);
      setSelectedPlaylistId(null);
      setSelectedPresentationPath(presentation.path);
    };

    const startOfToday = new Date();
    startOfToday.setHours(0, 0, 0, 0);

    const upcomingPlaylist = catalog.playlists
      .map((playlist) => {
        const serviceDate = playlist.externalSet?.serviceDate;
        return serviceDate ? { playlist, serviceDate: Date.parse(serviceDate) } : null;
      })
      .filter((entry): entry is { playlist: Playlist; serviceDate: number } => {
        return !!entry && !Number.isNaN(entry.serviceDate);
      })
      .filter((entry) => entry.serviceDate >= startOfToday.getTime())
      .sort((a, b) => a.serviceDate - b.serviceDate)[0]?.playlist;

    if (upcomingPlaylist) {
      selectPlaylist(upcomingPlaylist);
      hasAppliedStartupSelectionRef.current = true;
      return;
    }

    const recentPlaylist = [...catalog.playlists].sort((a, b) => {
      const aTime = parseTimestamp(a.updatedAt || a.createdAt);
      const bTime = parseTimestamp(b.updatedAt || b.createdAt);
      return bTime - aTime;
    })[0];

    if (recentPlaylist) {
      selectPlaylist(recentPlaylist);
      hasAppliedStartupSelectionRef.current = true;
      return;
    }

    const recentFromSettings = settings.recentFiles
      .map((recent) => findLibraryPresentation(recent.path))
      .find((match) => match !== null);

    if (recentFromSettings) {
      selectLibraryPresentation(recentFromSettings.library, recentFromSettings.presentation);
      hasAppliedStartupSelectionRef.current = true;
      return;
    }

    let recentFromLibraries: { library: Library; presentation: PresentationRef } | null = null;
    for (const library of catalog.libraries) {
      for (const presentation of library.presentations) {
        if (!recentFromLibraries) {
          recentFromLibraries = { library, presentation };
          continue;
        }
        const currentTime = parseTimestamp(presentation.updatedAt);
        const bestTime = parseTimestamp(recentFromLibraries.presentation.updatedAt);
        if (currentTime > bestTime) {
          recentFromLibraries = { library, presentation };
        }
      }
    }

    if (recentFromLibraries) {
      selectLibraryPresentation(recentFromLibraries.library, recentFromLibraries.presentation);
      hasAppliedStartupSelectionRef.current = true;
      return;
    }

    hasAppliedStartupSelectionRef.current = true;
  }, [
    catalog.libraries,
    catalog.playlists,
    isInitialized,
    setActivePage,
    setSelectedLibraryId,
    setSelectedPlaylistId,
    setSelectedPresentationPath,
    settings.recentFiles,
  ]);

  useEffect(() => {
    if (!isInitialized) return;
    if (!selectedPresentationPath) return;

    const normalizedSelected = selectedPresentationPath.replace(/\\/g, '/');
    const normalizedFilePath = filePath?.replace(/\\/g, '/');
    if (normalizedFilePath && normalizedFilePath.endsWith(normalizedSelected)) {
      return;
    }

    void openPresentationFromPath(selectedPresentationPath);
  }, [filePath, isInitialized, openPresentationFromPath, selectedPresentationPath]);

  const handleSaveAs = useCallback(async () => {
    if (!presentation) return;
    
    const file = await save({
      filters: [{ name: 'Church Presenter', extensions: ['cpres'] }],
      defaultPath: `${presentation.manifest.title}.cpres`,
    });
    if (file) {
      await savePresentation(file);
      setSelectedPresentationPath(file);
      if (pendingLibraryId) {
        await finalizeLibraryAssociation(pendingLibraryId, file);
      }
    }
  }, [finalizeLibraryAssociation, pendingLibraryId, presentation, savePresentation, setSelectedPresentationPath]);

  const handleSave = useCallback(async () => {
    if (!presentation) return;
    
    const { filePath } = useEditorStore.getState();
    if (filePath) {
      await savePresentation();
      if (pendingLibraryId) {
        await finalizeLibraryAssociation(pendingLibraryId, filePath);
      }
    } else {
      await handleSaveAs();
    }
  }, [finalizeLibraryAssociation, handleSaveAs, pendingLibraryId, presentation, savePresentation]);

  const handleSaveAllAndReport = useCallback(async () => {
    const report: SaveReportItem[] = [];
    const editorStateBefore = useEditorStore.getState();

    if (editorStateBefore.presentation && editorStateBefore.isDirty) {
      try {
        await saveNow();
        report.push({
          status: 'success',
          title: 'Presentation saved',
          detail: editorStateBefore.presentation.manifest.title,
        });
      } catch (error) {
        report.push({
          status: 'error',
          title: 'Presentation save failed',
          detail: error instanceof Error ? error.message : String(error),
        });
      }
    }

    await saveSettings();
    await useCatalogStore.getState().saveCatalog();

    const editorStateAfter = useEditorStore.getState();
    if (editorStateAfter.presentation) {
      if (editorStateAfter.pendingMedia.size > 0) {
        report.push({
          status: 'warning',
          title: 'Media changes are pending',
          detail: 'Some media assets have not been bundled yet.',
        });
      }

      if (!editorStateAfter.filePath) {
        report.push({
          status: 'warning',
          title: 'Presentation has no file path',
          detail: 'Auto-save could not persist to disk.',
        });
      }

      if (editorStateAfter.isDirty) {
        report.push({
          status: 'warning',
          title: 'Presentation still has unsaved changes',
          detail: 'Auto-save may be blocked or paused.',
        });
      }

      if (editorStateAfter.autoSave.status === 'error') {
        report.push({
          status: 'error',
          title: 'Auto-save error',
          detail: editorStateAfter.autoSave.lastError || 'Auto-save failed.',
        });
      }

      if (editorStateAfter.autoSave.status === 'conflict') {
        report.push({
          status: 'warning',
          title: 'Auto-save conflict detected',
          detail: 'Resolve the conflict to continue saving.',
        });
      }
    }

    if (settingsError) {
      report.push({
        status: 'error',
        title: 'Settings save error',
        detail: settingsError,
      });
    }

    const catalogError = useCatalogStore.getState().error;
    if (catalogError) {
      report.push({
        status: 'error',
        title: 'Catalog save error',
        detail: catalogError,
      });
    }

    if (report.length === 0) {
      report.push({
        status: 'success',
        title: 'No missed changes detected',
        detail: 'Auto-save is up to date.',
      });
    }

    setSaveReportItems(report);
    setSaveReportOpen(true);
  }, [saveNow, saveSettings, settingsError]);

  const handleCheckForUpdates = useCallback(() => {
    setPendingUpdate(null);
    setUpdateDialogOpen(true);
  }, []);

  const handleSelectLibrary = useCallback((id: string) => {
    setSelectedLibraryId(id);
    setSelectedPlaylistId(null);
  }, [setSelectedLibraryId, setSelectedPlaylistId]);

  const handleSelectPlaylist = useCallback((id: string) => {
    setSelectedPlaylistId(id);
    setSelectedLibraryId(null);
  }, [setSelectedPlaylistId, setSelectedLibraryId]);

  const handleSelectPresentation = useCallback(async (path: string, _context: 'library' | 'playlist') => {
    setSelectedPresentationPath(path);
    await openPresentationFromPath(path);
  }, [openPresentationFromPath, setSelectedPresentationPath]);

  const handleNewLibrary = useCallback((name: string) => {
    const library = addLibrary(name);
    setSelectedLibraryId(library.id);
    if (pendingNewPresentationOpen) {
      setPendingNewPresentationOpen(false);
      setNewPresentationOpen(true);
    }
  }, [addLibrary, pendingNewPresentationOpen, setSelectedLibraryId]);

  const handleNewPlaylist = useCallback((name: string) => {
    const playlist = addPlaylist(name);
    setSelectedPlaylistId(playlist.id);
  }, [addPlaylist, setSelectedPlaylistId]);

  const handleEditLibrary = useCallback((library: Library) => {
    setEditLibrary(library);
  }, []);

  const handleEditPlaylist = useCallback((playlist: Playlist) => {
    setEditPlaylist(playlist);
  }, []);

  const handleUpdateLibrary = useCallback((name: string) => {
    if (editLibrary) {
      updateLibrary(editLibrary.id, { name });
      setEditLibrary(null);
    }
  }, [editLibrary, updateLibrary]);

  const handleUpdatePlaylist = useCallback((name: string) => {
    if (editPlaylist) {
      updatePlaylist(editPlaylist.id, { name });
      setEditPlaylist(null);
    }
  }, [editPlaylist, updatePlaylist]);

  const handleDeleteLibrary = useCallback((library: Library) => {
    setDeleteTarget({ type: 'library', id: library.id, name: library.name });
  }, []);

  const handleDeletePlaylist = useCallback((playlist: Playlist) => {
    setDeleteTarget({ type: 'playlist', id: playlist.id, name: playlist.name });
  }, []);

  const handleConfirmDelete = useCallback(() => {
    if (!deleteTarget) return;
    
    if (deleteTarget.type === 'library') {
      deleteLibrary(deleteTarget.id);
      if (selectedLibraryId === deleteTarget.id) {
        setSelectedLibraryId(null);
      }
    } else {
      deletePlaylist(deleteTarget.id);
      if (selectedPlaylistId === deleteTarget.id) {
        setSelectedPlaylistId(null);
      }
    }
    setDeleteTarget(null);
  }, [deleteTarget, deleteLibrary, deletePlaylist, selectedLibraryId, selectedPlaylistId]);

  const handleRemoveFromLibrary = useCallback((libraryId: string, path: string) => {
    const normalizePath = (value: string) => value.replace(/\\/g, '/');
    const isMatch = (candidate: string | null, target: string) => {
      if (!candidate) return false;
      const normalizedCandidate = normalizePath(candidate);
      const normalizedTarget = normalizePath(target);
      return (
        normalizedCandidate === normalizedTarget ||
        normalizedCandidate.endsWith(normalizedTarget)
      );
    };

    removePresentationFromLibrary(libraryId, path);
    removeRecentFile(path);

    if (isMatch(selectedPresentationPath, path)) {
      setSelectedPresentationPath(null);
    }
    if (isMatch(filePath, path)) {
      closePresentation();
    }
  }, [
    closePresentation,
    filePath,
    removePresentationFromLibrary,
    removeRecentFile,
    selectedPresentationPath,
    setSelectedPresentationPath,
  ]);

  const handleRemoveFromPlaylist = useCallback((playlistId: string, path: string) => {
    const normalizePath = (value: string) => value.replace(/\\/g, '/');
    const isMatch = (candidate: string | null, target: string) => {
      if (!candidate) return false;
      const normalizedCandidate = normalizePath(candidate);
      const normalizedTarget = normalizePath(target);
      return (
        normalizedCandidate === normalizedTarget ||
        normalizedCandidate.endsWith(normalizedTarget)
      );
    };

    removeFromPlaylist(playlistId, path);
    removeRecentFile(path);

    if (isMatch(selectedPresentationPath, path)) {
      setSelectedPresentationPath(null);
    }
    if (isMatch(filePath, path)) {
      closePresentation();
    }
  }, [
    closePresentation,
    filePath,
    removeFromPlaylist,
    removeRecentFile,
    selectedPresentationPath,
    setSelectedPresentationPath,
  ]);

  return (
    <TooltipProvider>
      <div className="flex h-full flex-col bg-background text-foreground">
        {/* Menu Bar */}
        <AppMenubar
          onNewPresentation={handleRequestNewPresentation}
          onOpenPresentation={handleOpenPresentation}
          onSavePresentation={handleSave}
          onSaveAs={handleSaveAs}
          onOpenSettings={() => setSettingsOpen(true)}
          onNewLibrary={() => setNewLibraryOpen(true)}
          onNewPlaylist={() => setNewPlaylistOpen(true)}
          onUndo={undo}
          onRedo={redo}
          onSaveAllAndReport={handleSaveAllAndReport}
          onCheckForUpdates={handleCheckForUpdates}
        />
        <TopTabNav value={activePage} onChange={setActivePage} />

        {activePage === 'show' && (
          <ShowPage
            selectedLibraryId={selectedLibraryId}
            selectedPlaylistId={selectedPlaylistId}
            selectedPresentationPath={selectedPresentationPath}
            onSelectLibrary={handleSelectLibrary}
            onSelectPlaylist={handleSelectPlaylist}
            onSelectPresentation={handleSelectPresentation}
            onNewLibrary={() => setNewLibraryOpen(true)}
            onNewPlaylist={() => setNewPlaylistOpen(true)}
            onNewPresentation={handleRequestNewPresentation}
            onEditLibrary={handleEditLibrary}
            onEditPlaylist={handleEditPlaylist}
            onDeleteLibrary={handleDeleteLibrary}
            onDeletePlaylist={handleDeletePlaylist}
            onOpenPresentation={handleOpenPresentationPath}
            onRemoveFromLibrary={handleRemoveFromLibrary}
            onRemoveFromPlaylist={handleRemoveFromPlaylist}
            onImportSongs={() => {
              setSongBrowserMode('import');
              setSongBrowserOpen(true);
            }}
            onImportSets={() => setSetBrowserOpen(true)}
            onOpenOutputSettings={() => setSettingsOpen(true)}
          />
        )}

        {activePage === 'edit' && (
          <EditPage
            selectedLibraryId={selectedLibraryId}
            selectedPresentationPath={selectedPresentationPath}
            onSelectLibrary={handleSelectLibrary}
            onSelectPresentation={handleSelectPresentation}
            onNewLibrary={() => setNewLibraryOpen(true)}
            onNewPresentation={handleRequestNewPresentation}
            onEditLibrary={handleEditLibrary}
            onDeleteLibrary={handleDeleteLibrary}
            onOpenPresentation={handleOpenPresentationPath}
            onRemoveFromLibrary={handleRemoveFromLibrary}
            onOpenPresentationDialog={handleOpenPresentation}
            onLinkPresentation={() => {
              setSongBrowserMode('link');
              setSongBrowserOpen(true);
            }}
          />
        )}

        {activePage === 'reflow' && (
          <ReflowPage
            selectedLibraryId={selectedLibraryId}
            selectedPresentationPath={selectedPresentationPath}
            onSelectLibrary={handleSelectLibrary}
            onSelectPresentation={handleSelectPresentation}
            onNewLibrary={() => setNewLibraryOpen(true)}
            onNewPresentation={handleRequestNewPresentation}
            onEditLibrary={handleEditLibrary}
            onDeleteLibrary={handleDeleteLibrary}
            onOpenPresentation={handleOpenPresentationPath}
            onRemoveFromLibrary={handleRemoveFromLibrary}
          />
        )}

        {/* Dialogs */}
        <NewPresentationDialog
          open={newPresentationOpen}
          onOpenChange={setNewPresentationOpen}
          onConfirm={handleNewPresentation}
          libraries={catalog.libraries}
          defaultLibraryId={selectedLibraryId}
        />

        <NameDialog
          open={newLibraryOpen}
          onOpenChange={(open) => {
            setNewLibraryOpen(open);
            if (!open) setPendingNewPresentationOpen(false);
          }}
          onConfirm={handleNewLibrary}
          title="New Library"
          description="Create a new library to organize your presentations"
          label="Library Name"
          placeholder="Enter library name..."
          confirmText="Create"
        />

        <NameDialog
          open={newPlaylistOpen}
          onOpenChange={setNewPlaylistOpen}
          onConfirm={handleNewPlaylist}
          title="New Playlist"
          description="Create a new playlist for your service"
          label="Playlist Name"
          placeholder="Enter playlist name..."
          confirmText="Create"
        />

        <NameDialog
          open={!!editLibrary}
          onOpenChange={(open) => !open && setEditLibrary(null)}
          onConfirm={handleUpdateLibrary}
          title="Rename Library"
          label="Library Name"
          placeholder="Enter library name..."
          initialValue={editLibrary?.name || ''}
          confirmText="Save"
        />

        <NameDialog
          open={!!editPlaylist}
          onOpenChange={(open) => !open && setEditPlaylist(null)}
          onConfirm={handleUpdatePlaylist}
          title="Rename Playlist"
          label="Playlist Name"
          placeholder="Enter playlist name..."
          initialValue={editPlaylist?.name || ''}
          confirmText="Save"
        />

        <DeleteConfirmDialog
          open={!!deleteTarget}
          onOpenChange={(open) => !open && setDeleteTarget(null)}
          onConfirm={handleConfirmDelete}
          title={`Delete ${deleteTarget?.type === 'library' ? 'Library' : 'Playlist'}`}
          description={`Are you sure you want to delete "${deleteTarget?.name}"? This action cannot be undone.`}
        />

        <SettingsDialog
          open={settingsOpen}
          onOpenChange={setSettingsOpen}
        />

        <SongBrowserDialog
          open={songBrowserOpen}
          onOpenChange={setSongBrowserOpen}
          onImportSongs={handleImportSongs}
          onLinkSong={handleLinkSong}
          mode={songBrowserMode}
        />

        <SetBrowserDialog
          open={setBrowserOpen}
          onOpenChange={setSetBrowserOpen}
          onImportSets={handleImportSets}
        />

        <SaveConflictDialog
          open={conflictDialogOpen}
          onOpenChange={setConflictDialogOpen}
          conflict={conflictData}
          onResolve={handleResolveConflict}
        />
        <SaveReportDialog
          open={saveReportOpen}
          onOpenChange={setSaveReportOpen}
          items={saveReportItems}
        />
        <UpdateDialog
          open={updateDialogOpen}
          onOpenChange={(open) => {
            setUpdateDialogOpen(open);
            if (!open) {
              setPendingUpdate(null);
            }
          }}
          initialUpdate={pendingUpdate}
        />
      </div>
    </TooltipProvider>
  );
}
