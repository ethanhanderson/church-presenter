/**
 * MainContent - Center content area (playlist view or presentation editor)
 */

import { FileText, Plus, Music, CloudDownload, ListMusic } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';
import { ScrollArea } from '@/components/ui/scroll-area';
import { useEditorStore, useCatalogStore } from '@/lib/stores';
import { PresentationEditor } from '@/components/editor/PresentationEditor';
import type { Playlist, Library } from '@/lib/models';

interface MainContentProps {
  view: 'home' | 'library' | 'playlist' | 'editor';
  selectedLibraryId: string | null;
  selectedPlaylistId: string | null;
  onNewPresentation: () => void;
  onOpenPresentation: (path: string) => void;
  onImportFromMusicManager?: () => void;
  onImportSetList?: () => void;
  onLinkPresentation?: () => void;
}

export function MainContent({
  view,
  selectedLibraryId,
  selectedPlaylistId,
  onNewPresentation,
  onOpenPresentation,
  onImportFromMusicManager,
  onImportSetList,
  onLinkPresentation,
}: MainContentProps) {
  const { presentation } = useEditorStore();
  const { catalog } = useCatalogStore();

  // Get selected library or playlist
  const selectedLibrary = selectedLibraryId
    ? catalog.libraries.find((l) => l.id === selectedLibraryId)
    : null;
  const selectedPlaylist = selectedPlaylistId
    ? catalog.playlists.find((p) => p.id === selectedPlaylistId)
    : null;

  // Show editor if there's an open presentation
  if (view === 'editor' && presentation) {
    return <PresentationEditor onRequestLink={onLinkPresentation} />;
  }

  // Show library view
  if (view === 'library' && selectedLibrary) {
    return (
      <LibraryView
        library={selectedLibrary}
        onNewPresentation={onNewPresentation}
        onOpenPresentation={onOpenPresentation}
      />
    );
  }

  // Show playlist view
  if (view === 'playlist' && selectedPlaylist) {
    return (
      <PlaylistView
        playlist={selectedPlaylist}
        onNewPresentation={onNewPresentation}
        onOpenPresentation={onOpenPresentation}
      />
    );
  }

  // Show home/welcome view
  return (
    <HomeView
      onNewPresentation={onNewPresentation}
      onImportFromMusicManager={onImportFromMusicManager}
      onImportSetList={onImportSetList}
    />
  );
}

interface HomeViewProps {
  onNewPresentation: () => void;
  onImportFromMusicManager?: () => void;
  onImportSetList?: () => void;
}

function HomeView({ onNewPresentation, onImportFromMusicManager, onImportSetList }: HomeViewProps) {
  return (
    <div className="flex h-full items-center justify-center p-8">
      <div className="text-center max-w-md space-y-6">
        <div className="space-y-2">
          <Music className="mx-auto h-12 w-12 text-muted-foreground" />
          <h2 className="text-2xl font-semibold">Welcome to Church Presenter</h2>
          <p className="text-muted-foreground">
            Create beautiful presentations for worship, sermons, and announcements.
          </p>
        </div>

        <div className="flex flex-col gap-3">
          <Button onClick={onNewPresentation} size="lg">
            <Plus className="mr-2 h-5 w-5" />
            New Presentation
          </Button>
          {onImportFromMusicManager && (
            <Button variant="outline" size="lg" onClick={onImportFromMusicManager}>
              <CloudDownload className="mr-2 h-5 w-5" />
              Import Song from Music Manager
            </Button>
          )}
          {onImportSetList && (
            <Button variant="outline" size="lg" onClick={onImportSetList}>
              <ListMusic className="mr-2 h-5 w-5" />
              Import Set List
            </Button>
          )}
          <Button variant="outline" size="lg">
            Open Presentation
          </Button>
        </div>

        <div className="pt-4">
          <p className="text-xs text-muted-foreground">
            Select a library or playlist from the sidebar to get started
          </p>
        </div>
      </div>
    </div>
  );
}

interface LibraryViewProps {
  library: Library;
  onNewPresentation: () => void;
  onOpenPresentation: (path: string) => void;
}

function LibraryView({
  library,
  onNewPresentation,
  onOpenPresentation,
}: LibraryViewProps) {
  return (
    <div className="flex flex-col h-full">
      {/* Header */}
      <div className="flex items-center justify-between border-b p-4">
        <div>
          <h2 className="text-lg font-semibold">{library.name}</h2>
          <p className="text-sm text-muted-foreground">
            {library.presentations.length} presentation
            {library.presentations.length !== 1 ? 's' : ''}
          </p>
        </div>
        <Button onClick={onNewPresentation}>
          <Plus className="mr-2 h-4 w-4" />
          New Presentation
        </Button>
      </div>

      {/* Content */}
      <ScrollArea className="flex-1 p-4">
        {library.presentations.length === 0 ? (
          <EmptyState
            title="No presentations yet"
            description="Create your first presentation to get started"
            onAction={onNewPresentation}
            actionLabel="New Presentation"
          />
        ) : (
          <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-4 gap-4">
            {library.presentations.map((pres) => (
              <PresentationCard
                key={pres.path}
                title={pres.title}
                updatedAt={pres.updatedAt}
                onClick={() => onOpenPresentation(pres.path)}
              />
            ))}
          </div>
        )}
      </ScrollArea>
    </div>
  );
}

interface PlaylistViewProps {
  playlist: Playlist;
  onNewPresentation: () => void;
  onOpenPresentation: (path: string) => void;
}

function PlaylistView({
  playlist,
  onNewPresentation,
  onOpenPresentation,
}: PlaylistViewProps) {
  return (
    <div className="flex flex-col h-full">
      {/* Header */}
      <div className="flex items-center justify-between border-b p-4">
        <div>
          <h2 className="text-lg font-semibold">{playlist.name}</h2>
          <p className="text-sm text-muted-foreground">
            {playlist.items.length} item{playlist.items.length !== 1 ? 's' : ''}
          </p>
        </div>
        <Button onClick={onNewPresentation}>
          <Plus className="mr-2 h-4 w-4" />
          Add Presentation
        </Button>
      </div>

      {/* Content */}
      <ScrollArea className="flex-1 p-4">
        {playlist.items.length === 0 ? (
          <EmptyState
            title="Playlist is empty"
            description="Add presentations to build your service order"
            onAction={onNewPresentation}
            actionLabel="Add Presentation"
          />
        ) : (
          <div className="space-y-2">
            {playlist.items.map((item, index) => (
              <PlaylistItemCard
                key={`${item.path}-${index}`}
                index={index + 1}
                title={item.title}
                onClick={() => onOpenPresentation(item.path)}
              />
            ))}
          </div>
        )}
      </ScrollArea>
    </div>
  );
}

interface EmptyStateProps {
  title: string;
  description: string;
  onAction: () => void;
  actionLabel: string;
}

function EmptyState({ title, description, onAction, actionLabel }: EmptyStateProps) {
  return (
    <div className="flex h-full items-center justify-center">
      <div className="text-center space-y-4">
        <FileText className="mx-auto h-12 w-12 text-muted-foreground" />
        <div className="space-y-1">
          <h3 className="font-medium">{title}</h3>
          <p className="text-sm text-muted-foreground">{description}</p>
        </div>
        <Button onClick={onAction}>
          <Plus className="mr-2 h-4 w-4" />
          {actionLabel}
        </Button>
      </div>
    </div>
  );
}

interface PresentationCardProps {
  title: string;
  updatedAt: string;
  onClick: () => void;
}

function PresentationCard({ title, updatedAt, onClick }: PresentationCardProps) {
  const formattedDate = new Date(updatedAt).toLocaleDateString();

  return (
    <Card
      className="cursor-pointer transition-colors hover:bg-accent overflow-hidden"
      onClick={onClick}
    >
      <CardContent className="p-4">
        <div className="aspect-video bg-muted rounded-md mb-3 flex items-center justify-center overflow-hidden">
          <FileText className="h-8 w-8 text-muted-foreground" />
        </div>
        <div className="space-y-1 min-w-0">
          <h4 className="font-medium truncate">{title}</h4>
          <p className="text-xs text-muted-foreground truncate">{formattedDate}</p>
        </div>
      </CardContent>
    </Card>
  );
}

interface PlaylistItemCardProps {
  index: number;
  title: string;
  onClick: () => void;
}

function PlaylistItemCard({ index, title, onClick }: PlaylistItemCardProps) {
  return (
    <Card
      className="cursor-pointer transition-colors hover:bg-accent overflow-hidden"
      onClick={onClick}
    >
      <CardContent className="flex items-center gap-4 p-3">
        <div className="flex h-8 w-8 items-center justify-center rounded-full bg-muted text-sm font-medium shrink-0">
          {index}
        </div>
        <div className="flex-1 min-w-0">
          <h4 className="font-medium truncate">{title}</h4>
        </div>
        <FileText className="h-5 w-5 text-muted-foreground shrink-0" />
      </CardContent>
    </Card>
  );
}
