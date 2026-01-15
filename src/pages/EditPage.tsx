/**
 * EditPage - presentation editing workspace
 */

import { useEffect, useState, useCallback } from 'react';
import { usePanelRef, type PanelSize } from 'react-resizable-panels';
import {
  ResizableHandle,
  ResizablePanel,
  ResizablePanelGroup,
} from '@/components/ui/resizable';
import { AppSidebar } from '@/components/layout/AppSidebar';
import { PresentationEditor } from '@/components/editor/PresentationEditor';
import { Button } from '@/components/ui/button';
import { useEditorStore } from '@/lib/stores';
import type { Library } from '@/lib/models';

interface EditPageProps {
  selectedLibraryId: string | null;
  selectedPresentationPath: string | null;
  onSelectLibrary: (id: string) => void;
  onSelectPresentation: (path: string, context: 'library' | 'playlist') => void;
  onNewLibrary: () => void;
  onNewPresentation: () => void;
  onEditLibrary: (library: Library) => void;
  onDeleteLibrary: (library: Library) => void;
  onOpenPresentation: (path: string) => void;
  onRemoveFromLibrary: (libraryId: string, path: string) => void;
  onOpenPresentationDialog: () => void;
  onLinkPresentation: () => void;
}

export function EditPage({
  selectedLibraryId,
  selectedPresentationPath,
  onSelectLibrary,
  onSelectPresentation,
  onNewLibrary,
  onNewPresentation,
  onEditLibrary,
  onDeleteLibrary,
  onOpenPresentation,
  onRemoveFromLibrary,
  onOpenPresentationDialog,
  onLinkPresentation,
}: EditPageProps) {
  const leftSidebarRef = usePanelRef();
  const [isLeftSidebarCollapsed, setIsLeftSidebarCollapsed] = useState(false);
  const { presentation, selectSlide } = useEditorStore();

  const LEFT_COLLAPSED_SIZE = 3;

  const handleToggleLeftSidebar = () => {
    const panel = leftSidebarRef.current;
    if (!panel) return;
    if (panel.isCollapsed()) {
      panel.expand();
      setIsLeftSidebarCollapsed(false);
    } else {
      panel.collapse();
      setIsLeftSidebarCollapsed(true);
    }
  };

  const handleLeftPanelResize = useCallback((size: PanelSize) => {
    // Detect collapse when panel is dragged to collapsed size
    setIsLeftSidebarCollapsed(size.asPercentage <= LEFT_COLLAPSED_SIZE + 0.5);
  }, []);

  useEffect(() => {
    if (!presentation) return;
    selectSlide(presentation.slides[0]?.id || null);
  }, [presentation?.manifest.presentationId, selectSlide]);

  return (
    <ResizablePanelGroup direction="horizontal" className="flex-1">
      <ResizablePanel
        defaultSize={18}
        minSize={15}
        maxSize={35}
        collapsible
        collapsedSize={LEFT_COLLAPSED_SIZE}
        panelRef={leftSidebarRef}
        onResize={handleLeftPanelResize}
      >
        <AppSidebar
          mode="edit"
          selectedLibraryId={selectedLibraryId}
          selectedPlaylistId={null}
          selectedPresentationPath={selectedPresentationPath}
          onSelectLibrary={onSelectLibrary}
          onSelectPlaylist={() => undefined}
          onSelectPresentation={onSelectPresentation}
          onNewLibrary={onNewLibrary}
          onNewPlaylist={() => undefined}
          onNewPresentation={onNewPresentation}
          onEditLibrary={onEditLibrary}
          onEditPlaylist={() => undefined}
          onDeleteLibrary={onDeleteLibrary}
          onDeletePlaylist={() => undefined}
          onOpenPresentation={onOpenPresentation}
          onRemoveFromLibrary={onRemoveFromLibrary}
          onRemoveFromPlaylist={() => undefined}
          collapsed={isLeftSidebarCollapsed}
          onToggleCollapse={handleToggleLeftSidebar}
        />
      </ResizablePanel>

      <ResizableHandle />

      <ResizablePanel defaultSize={82} minSize={50}>
        {presentation ? (
          <PresentationEditor onRequestLink={onLinkPresentation} />
        ) : (
          <EditEmptyState
            onNewPresentation={onNewPresentation}
            onOpenPresentation={onOpenPresentationDialog}
          />
        )}
      </ResizablePanel>
    </ResizablePanelGroup>
  );
}

interface EditEmptyStateProps {
  onNewPresentation: () => void;
  onOpenPresentation: () => void;
}

function EditEmptyState({ onNewPresentation, onOpenPresentation }: EditEmptyStateProps) {
  return (
    <div className="flex h-full items-center justify-center p-8">
      <div className="text-center max-w-md space-y-4">
        <div className="space-y-2">
          <h2 className="text-xl font-semibold">Edit Mode</h2>
          <p className="text-muted-foreground text-sm">
            Create a new presentation or open an existing one to begin editing.
          </p>
        </div>
        <div className="flex flex-col gap-2">
          <Button onClick={onNewPresentation}>New Presentation</Button>
          <Button variant="outline" onClick={onOpenPresentation}>
            Open Presentation
          </Button>
        </div>
      </div>
    </div>
  );
}
