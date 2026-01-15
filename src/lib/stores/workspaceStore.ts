/**
 * Workspace Store - manages persisted UI state across pages
 */

import { create } from 'zustand';
import { immer } from 'zustand/middleware/immer';
import { load } from '@tauri-apps/plugin-store';

export type AppPage = 'show' | 'edit' | 'reflow';

interface WorkspaceSnapshot {
  activePage: AppPage;
  selectedLibraryId: string | null;
  selectedPlaylistId: string | null;
  selectedPresentationPath: string | null;
}

interface WorkspaceState extends WorkspaceSnapshot {
  isLoading: boolean;
  error: string | null;

  loadWorkspace: () => Promise<void>;
  saveWorkspace: () => Promise<void>;
  setActivePage: (page: AppPage) => void;
  setSelectedLibraryId: (id: string | null) => void;
  setSelectedPlaylistId: (id: string | null) => void;
  setSelectedPresentationPath: (path: string | null) => void;
  remapSelectedPresentationPath: (oldBase: string, newBase: string) => void;
}

const WORKSPACE_FILE = 'workspace.json';
const defaultWorkspace: WorkspaceSnapshot = {
  activePage: 'show',
  selectedLibraryId: null,
  selectedPlaylistId: null,
  selectedPresentationPath: null,
};

let workspaceStorePromise: ReturnType<typeof load> | null = null;

const getWorkspaceStore = () => {
  if (!workspaceStorePromise) {
    workspaceStorePromise = load(WORKSPACE_FILE, {
      defaults: { workspace: defaultWorkspace },
      autoSave: 300,
    });
  }
  return workspaceStorePromise;
};

const mergeWorkspace = (stored?: Partial<WorkspaceSnapshot> | null): WorkspaceSnapshot => ({
  ...defaultWorkspace,
  ...stored,
});

export const useWorkspaceStore = create<WorkspaceState>()(
  immer((set, get) => ({
    ...defaultWorkspace,
    isLoading: false,
    error: null,

    loadWorkspace: async () => {
      set((state) => {
        state.isLoading = true;
        state.error = null;
      });

      try {
        const store = await getWorkspaceStore();
        const workspace = await store.get<WorkspaceSnapshot>('workspace');
        set((state) => {
          Object.assign(state, mergeWorkspace(workspace));
          state.isLoading = false;
        });
      } catch (error) {
        set((state) => {
          state.error = String(error);
          state.isLoading = false;
        });
      }
    },

    saveWorkspace: async () => {
      try {
        const store = await getWorkspaceStore();
        const snapshot: WorkspaceSnapshot = {
          activePage: get().activePage,
          selectedLibraryId: get().selectedLibraryId,
          selectedPlaylistId: get().selectedPlaylistId,
          selectedPresentationPath: get().selectedPresentationPath,
        };
        await store.set('workspace', snapshot);
        await store.save();
      } catch (error) {
        set((state) => {
          state.error = String(error);
        });
      }
    },

    setActivePage: (page) => {
      set((state) => {
        state.activePage = page;
      });
      void get().saveWorkspace();
    },

    setSelectedLibraryId: (id) => {
      set((state) => {
        state.selectedLibraryId = id;
      });
      void get().saveWorkspace();
    },

    setSelectedPlaylistId: (id) => {
      set((state) => {
        state.selectedPlaylistId = id;
      });
      void get().saveWorkspace();
    },

    setSelectedPresentationPath: (path) => {
      set((state) => {
        state.selectedPresentationPath = path;
      });
      void get().saveWorkspace();
    },

  remapSelectedPresentationPath: (oldBase, newBase) => {
    const normalizePath = (path: string) => path.replace(/\\/g, '/');
    const isAbsolutePath = (path: string) => /^[a-zA-Z]:[\\/]|^\//.test(path);
    const normalizedOld = normalizePath(oldBase);
    const normalizedNew = normalizePath(newBase);

    set((state) => {
      const current = state.selectedPresentationPath;
      if (!current || !isAbsolutePath(current)) return;
      const normalizedPath = normalizePath(current);
      if (!normalizedPath.startsWith(`${normalizedOld}/`)) return;
      state.selectedPresentationPath = `${normalizedNew}/${normalizedPath.slice(
        normalizedOld.length + 1
      )}`;
    });
    void get().saveWorkspace();
  },
  }))
);
