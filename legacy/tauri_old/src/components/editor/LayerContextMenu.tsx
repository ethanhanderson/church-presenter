import { useMemo, useState } from 'react';
import {
  ArrowDown,
  ArrowDownToLine,
  ArrowUp,
  ArrowUpToLine,
  Eye,
  EyeOff,
  Lock,
  Pencil,
  Trash2,
  Unlock,
} from 'lucide-react';
import {
  ContextMenu,
  ContextMenuContent,
  ContextMenuItem,
  ContextMenuLabel,
  ContextMenuSeparator,
  ContextMenuShortcut,
  ContextMenuTrigger,
} from '@/components/ui/context-menu';
import { Kbd, KbdGroup } from '@/components/ui/kbd';
import { NameDialog } from '@/components/dialogs';
import type { Layer } from '@/lib/models';
import { useEditorLayerContextMenuStore, type LayerContextMenuStore } from '@/lib/editor/slide-surface-store';

interface LayerContextMenuProps {
  slideId: string;
  layer: Layer;
  children: React.ReactNode;
  onSelect?: () => void;
  store?: LayerContextMenuStore;
}

function renderShortcut(keys: string[]) {
  return (
    <ContextMenuShortcut>
      <KbdGroup>
        {keys.map((key) => (
          <Kbd key={key}>{key}</Kbd>
        ))}
      </KbdGroup>
    </ContextMenuShortcut>
  );
}

export function LayerContextMenu({
  slideId,
  layer,
  children,
  onSelect,
  store: customStore,
}: LayerContextMenuProps) {
  const editorStore = useEditorLayerContextMenuStore();
  const {
    updateLayer,
    deleteLayer,
    bringLayerForward,
    sendLayerBackward,
    bringLayerToFront,
    sendLayerToBack,
  } = customStore ?? editorStore;
  const [renameOpen, setRenameOpen] = useState(false);
  const isMac = useMemo(
    () => typeof navigator !== 'undefined' && navigator.platform.toUpperCase().includes('MAC'),
    []
  );
  const cmdKey = isMac ? 'Cmd' : 'Ctrl';

  return (
    <>
      <ContextMenu
        onOpenChange={(open) => {
          if (open) onSelect?.();
        }}
      >
        <ContextMenuTrigger asChild>{children}</ContextMenuTrigger>
        <ContextMenuContent>
          <ContextMenuLabel>
            {layer.name} · {layer.type}
          </ContextMenuLabel>
          <ContextMenuSeparator />
          <ContextMenuItem onClick={() => setRenameOpen(true)}>
            <Pencil className="h-4 w-4" />
            Rename
          </ContextMenuItem>
          <ContextMenuItem
            onClick={() =>
              updateLayer(slideId, layer.id, { visible: !layer.visible })
            }
          >
            {layer.visible ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
            {layer.visible ? 'Hide Layer' : 'Show Layer'}
          </ContextMenuItem>
          <ContextMenuItem
            onClick={() =>
              updateLayer(slideId, layer.id, { locked: !layer.locked })
            }
          >
            {layer.locked ? <Unlock className="h-4 w-4" /> : <Lock className="h-4 w-4" />}
            {layer.locked ? 'Unlock Layer' : 'Lock Layer'}
          </ContextMenuItem>
          <ContextMenuSeparator />
          <ContextMenuItem onClick={() => bringLayerForward(slideId, layer.id)}>
            <ArrowUp className="h-4 w-4" />
            Move Forward
            {renderShortcut([']'])}
          </ContextMenuItem>
          <ContextMenuItem onClick={() => sendLayerBackward(slideId, layer.id)}>
            <ArrowDown className="h-4 w-4" />
            Move Backward
            {renderShortcut(['['])}
          </ContextMenuItem>
          <ContextMenuItem onClick={() => bringLayerToFront(slideId, layer.id)}>
            <ArrowUpToLine className="h-4 w-4" />
            Bring to Front
            {renderShortcut([cmdKey, ']'])}
          </ContextMenuItem>
          <ContextMenuItem onClick={() => sendLayerToBack(slideId, layer.id)}>
            <ArrowDownToLine className="h-4 w-4" />
            Send to Back
            {renderShortcut([cmdKey, '['])}
          </ContextMenuItem>
          <ContextMenuSeparator />
          <ContextMenuItem variant="destructive" onClick={() => deleteLayer(slideId, layer.id)}>
            <Trash2 className="h-4 w-4" />
            Delete Layer
            {renderShortcut(['Del'])}
          </ContextMenuItem>
        </ContextMenuContent>
      </ContextMenu>
      <NameDialog
        open={renameOpen}
        onOpenChange={setRenameOpen}
        title="Rename layer"
        label="Layer name"
        placeholder="Layer name..."
        confirmText="Rename"
        initialValue={layer.name}
        onConfirm={(name) => updateLayer(slideId, layer.id, { name })}
      />
    </>
  );
}
