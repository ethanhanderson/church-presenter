import { useCallback } from 'react';
import type { Layer } from '@/lib/models';
import type { SlideSurfaceSelection } from '@/lib/editor/slide-surface-store';

interface LayerSelectionOptions {
  layers: Layer[];
  selection: SlideSurfaceSelection;
  selectLayer: (layerId: string | null) => void;
  selectLayers: (layerIds: string[]) => void;
  lastSelectedLayerId: React.MutableRefObject<string | null>;
}

export function useLayerSelection({
  layers,
  selection,
  selectLayer,
  selectLayers,
  lastSelectedLayerId,
}: LayerSelectionOptions) {
  const getLayerRangeSelection = useCallback(
    (startId: string, endId: string) => {
      const orderedIds = layers.map((layer) => layer.id);
      const startIndex = orderedIds.indexOf(startId);
      const endIndex = orderedIds.indexOf(endId);
      if (startIndex === -1 || endIndex === -1) return [endId];
      const [from, to] = startIndex <= endIndex ? [startIndex, endIndex] : [endIndex, startIndex];
      return orderedIds.slice(from, to + 1);
    },
    [layers]
  );

  const handleLayerSelection = useCallback(
    (event: React.MouseEvent | React.PointerEvent, layerId: string) => {
      const isToggle = event.metaKey || event.ctrlKey;
      const isRange = event.shiftKey;
      const currentSelection = selection.layerIds;
      const anchor = lastSelectedLayerId.current ?? currentSelection[0] ?? null;
      let nextSelection = currentSelection;

      if (isRange && anchor) {
        const range = getLayerRangeSelection(anchor, layerId);
        nextSelection = Array.from(new Set([...currentSelection, ...range]));
        selectLayers(nextSelection);
      } else if (isToggle) {
        if (currentSelection.includes(layerId)) {
          nextSelection = currentSelection.filter((id) => id !== layerId);
        } else {
          nextSelection = [layerId, ...currentSelection];
        }
        selectLayers(nextSelection);
      } else if (!currentSelection.includes(layerId)) {
        selectLayer(layerId);
      }

      lastSelectedLayerId.current = layerId;
    },
    [getLayerRangeSelection, lastSelectedLayerId, selectLayer, selectLayers, selection.layerIds]
  );

  const clearSelection = useCallback(() => {
    selectLayer(null);
    lastSelectedLayerId.current = null;
  }, [selectLayer, lastSelectedLayerId]);

  return {
    handleLayerSelection,
    clearSelection,
    getLayerRangeSelection,
  };
}
