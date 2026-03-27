/**
 * SlideCanvas - WYSIWYG canvas for editing slide content with layer support
 * Uses pointer events for drag/resize/rotate operations
 */

import { useState, useRef, useEffect, useLayoutEffect, useCallback, memo, useMemo } from 'react';
import { Plus } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Textarea } from '@/components/ui/textarea';
import { LayerContent } from '@/components/preview/slide-layer-content';
import { BackgroundMedia } from '@/components/preview/BackgroundMedia';
import { useSettingsStore } from '@/lib/stores';
import {
  useEditorSlideCanvasStore,
  type AssetToolbarStore,
  type SlideCanvasStore,
} from '@/lib/editor/slide-surface-store';
import { AssetToolbar } from './AssetToolbar';
import { LayerContextMenu } from './LayerContextMenu';
import type {
  Slide,
  Layer,
  TextLayer,
  ShapeLayer,
  MediaLayer,
  WebLayer,
  Background,
  LayerTransform,
  BlendMode,
  Presentation,
  LayerFill,
  TextStyle,
} from '@/lib/models';
import {
  defaultPrimaryTextStyle,
  getBackgroundStyle,
  resolveSlideBackground,
} from '@/lib/models';
import { coerceRotation } from '@/lib/models/transform-utils';
import { cn } from '@/lib/utils';
import { parseColor, toRgba } from '@/lib/color-utils';
import { useResolvedBackgroundMediaSrc } from '@/lib/media/resolveMediaUrl';
import {
  getLayerContentStyle,
  getPrimaryStroke,
  getTextContainerStyle,
  getTextStyle,
  getTextStyleSignature,
  getTransformOrigin,
  mergeTextStyle,
  resolveLayerFills,
} from '@/components/preview/layer-render-utils';
import { useLayerSelection } from './use-layer-selection';
import { useCursor } from '@/components/cursor';
import type { CursorVariant, ResizeDirection } from '@/components/cursor';

const blendStyle: React.CSSProperties | undefined = undefined;

// Selection color variants based on background luminance
type SelectionColorScheme = 'light' | 'dark';

interface SelectionColors {
  border: string;
  fill: string;
  handleBorder: string;
  text: string;
}

const SELECTION_COLORS: Record<SelectionColorScheme, SelectionColors> = {
  light: {
    border: '#22d3ee',
    fill: '#06b6d4',
    handleBorder: '#ffffff',
    text: '#ffffff',
  },
  dark: {
    border: '#2563eb',
    fill: '#1d4ed8',
    handleBorder: '#ffffff',
    text: '#ffffff',
  },
};

const TRANSPARENT_CANVAS_LIGHT = '#e5e7eb';
const TRANSPARENT_CANVAS_DARK = '#4b5563';
const TRANSPARENT_CANVAS_OPACITY = 0.6;
const ROTATION_INNER_RADIUS = 16;
const ROTATION_OUTER_RADIUS = 32;
const ROTATION_CURSOR = 'grab';

const getPrimaryFill = (fills?: LayerFill[]) => {
  const enabled = (fills ?? []).filter((fill) => fill.enabled !== false);
  return enabled[enabled.length - 1] ?? enabled[0] ?? null;
};

const getPrimaryLayerColor = (layer?: Layer | null) => {
  if (!layer) return null;
  const primaryFill = getPrimaryFill(layer.fills);
  if (primaryFill?.color) {
    return { color: primaryFill.color, opacity: primaryFill.opacity ?? 1 };
  }
  if (layer.type === 'text') {
    return { color: layer.style?.color ?? defaultPrimaryTextStyle.color, opacity: 1 };
  }
  if (layer.type === 'shape') {
    return { color: layer.style?.fill ?? '#3b82f6', opacity: layer.style?.fillOpacity ?? 1 };
  }
  return null;
};

const getColorLuminance = (color: string) => {
  const parsed = parseColor(color);
  if (!parsed) return null;
  const [rs, gs, bs] = [parsed.r, parsed.g, parsed.b].map((channel) => {
    const normalized = channel / 255;
    return normalized <= 0.03928
      ? normalized / 12.92
      : Math.pow((normalized + 0.055) / 1.055, 2.4);
  });
  return 0.2126 * rs + 0.7152 * gs + 0.0722 * bs;
};

interface SlideCanvasProps {
  slide: Slide | null;
  aspectRatio?: '16:9' | '4:3' | '16:10';
  presentation?: Presentation | null;
  presentationPath?: string | null;
  pendingMedia?: Map<string, string>;
  store?: SlideCanvasStore;
  toolbarMode?: 'editor' | 'theme';
  onAddTextLayer?: () => void;
  onAddHeadingLayer?: () => void;
  toolbarStore?: AssetToolbarStore;
  allowMediaImport?: boolean;
}


type ResizeHandle = 'nw' | 'n' | 'ne' | 'e' | 'se' | 's' | 'sw' | 'w';

type GuideLine = {
  orientation: 'vertical' | 'horizontal';
  position: number;
};

export function SlideCanvas({
  slide,
  aspectRatio,
  presentation,
  presentationPath,
  pendingMedia,
  store: customStore,
  toolbarMode = 'editor',
  onAddTextLayer,
  onAddHeadingLayer,
  toolbarStore,
  allowMediaImport,
}: SlideCanvasProps) {
  const editorStore = useEditorSlideCanvasStore();
  const store = customStore ?? editorStore;
  const {
    addTextLayer,
    updateLayer,
    updateLayers,
    deleteLayer,
    selectLayer,
    selectLayers,
    beginLayerTransform,
    updateLayerTransform,
    updateLayerTransforms,
    commitLayerTransform,
    bringLayerForward,
    sendLayerBackward,
    bringLayerToFront,
    sendLayerToBack,
  } = store;
  const selection = store.selection ?? { slideIds: [], layerIds: [] };
  const { settings } = useSettingsStore();
  const containerRef = useRef<HTMLDivElement>(null);
  const slideRef = useRef<HTMLDivElement>(null);
  const [editingLayerId, setEditingLayerId] = useState<string | null>(null);
  const [textScale, setTextScale] = useState(1);
  const [guides, setGuides] = useState<GuideLine[]>([]);
  const lastSelectedLayerId = useRef<string | null>(null);
  const layersRef = useRef<Layer[]>([]);
  const selectionIdsRef = useRef<string[]>([]);
  const pointerMetricsRef = useRef<{ lastLogAt: number; count: number; maxDeltaMs: number; lastEventAt: number | null }>({
    lastLogAt: 0,
    count: 0,
    maxDeltaMs: 0,
    lastEventAt: null,
  });
  const baseSize = useMemo(
    () => getBaseSlideSize(aspectRatio, presentation?.manifest.slideSize),
    [aspectRatio, presentation?.manifest.slideSize]
  );
  const aspectClass = getAspectClass(aspectRatio);
  const selectedLayerIds = useMemo(() => new Set(selection.layerIds), [selection.layerIds]);
  const getLayersSnapshot = useCallback(() => layersRef.current, []);
  const getSelectionIds = useCallback(() => selectionIdsRef.current, []);
  const { handleLayerSelection, clearSelection } = useLayerSelection({
    layers: slide?.layers ?? [],
    selection,
    selectLayer,
    selectLayers,
    lastSelectedLayerId,
  });

  useEffect(() => {
    layersRef.current = slide?.layers ?? [];
  }, [slide?.layers]);

  useEffect(() => {
    selectionIdsRef.current = selection.layerIds;
  }, [selection.layerIds]);

  useEffect(() => {
    const target = slideRef.current;
    if (!target) {
      // #region agent log
      fetch('http://127.0.0.1:7247/ingest/b87235aa-ab93-4b87-9657-fe5221616d91',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({sessionId:'debug-session',runId:'pre-fix',hypothesisId:'F',location:'SlideCanvas.tsx:slideRef',message:'slide_ref_missing',data:{hasSlide:!!slide},timestamp:Date.now()})}).catch(()=>{});
      // #endregion
      return;
    }
    const metrics = pointerMetricsRef.current;
    const onPointerMove = () => {
      const now = performance.now();
      if (metrics.lastEventAt !== null) {
        const delta = now - metrics.lastEventAt;
        if (delta > metrics.maxDeltaMs) metrics.maxDeltaMs = delta;
      }
      metrics.lastEventAt = now;
      metrics.count += 1;
      const wallNow = Date.now();
      if (wallNow - metrics.lastLogAt > 1000) {
        const eventsPerSecond = metrics.count;
        // #region agent log
        fetch('http://127.0.0.1:7247/ingest/b87235aa-ab93-4b87-9657-fe5221616d91',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({sessionId:'debug-session',runId:'pre-fix',hypothesisId:'F',location:'SlideCanvas.tsx:pointerMove',message:'pointer_move_rate',data:{eventsPerSecond,maxDeltaMs:Math.round(metrics.maxDeltaMs),layerCount:slide?.layers.length ?? 0},timestamp:Date.now()})}).catch(()=>{});
        // #endregion
        metrics.lastLogAt = wallNow;
        metrics.count = 0;
        metrics.maxDeltaMs = 0;
      }
    };
    target.addEventListener('pointermove', onPointerMove);
    return () => {
      target.removeEventListener('pointermove', onPointerMove);
    };
  }, [slide]);

  useEffect(() => {
    let rafId = 0;
    let lastFrame = performance.now();
    let frameCount = 0;
    let maxDelta = 0;
    let lastLogAt = Date.now();
    const loop = () => {
      const now = performance.now();
      const delta = now - lastFrame;
      if (delta > maxDelta) maxDelta = delta;
      lastFrame = now;
      frameCount += 1;
      rafId = window.requestAnimationFrame(loop);
    };
    rafId = window.requestAnimationFrame(loop);

    const intervalId = window.setInterval(() => {
      const now = Date.now();
      const elapsed = Math.max(1, now - lastLogAt);
      const fps = Math.round((frameCount * 1000) / elapsed);
      // #region agent log
      fetch('http://127.0.0.1:7247/ingest/b87235aa-ab93-4b87-9657-fe5221616d91',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({sessionId:'debug-session',runId:'pre-fix',hypothesisId:'G',location:'SlideCanvas.tsx:raf',message:'raf_stats',data:{fps,maxDeltaMs:Math.round(maxDelta)},timestamp:Date.now()})}).catch(()=>{});
      // #endregion
      lastLogAt = now;
      frameCount = 0;
      maxDelta = 0;
    }, 1000);

    return () => {
      window.cancelAnimationFrame(rafId);
      window.clearInterval(intervalId);
    };
  }, []);

  useEffect(() => {
    if (typeof PerformanceObserver === 'undefined') return;
    if (!PerformanceObserver.supportedEntryTypes?.includes('longtask')) return;
    const observer = new PerformanceObserver((list) => {
      for (const entry of list.getEntries()) {
        if (entry.duration < 50) continue;
        // #region agent log
        fetch('http://127.0.0.1:7247/ingest/b87235aa-ab93-4b87-9657-fe5221616d91',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({sessionId:'debug-session',runId:'pre-fix',hypothesisId:'G',location:'SlideCanvas.tsx:longtask',message:'long_task',data:{durationMs:Math.round(entry.duration),startTimeMs:Math.round(entry.startTime)},timestamp:Date.now()})}).catch(()=>{});
        // #endregion
      }
    });
    observer.observe({ entryTypes: ['longtask'] });
    return () => observer.disconnect();
  }, []);

  const alignSelection = useCallback(
    (direction: 'left' | 'center' | 'right' | 'top' | 'middle' | 'bottom') => {
      if (!slide) return;
      const selectedLayers = slide.layers.filter((layer) => selectedLayerIds.has(layer.id));
      if (selectedLayers.length === 0) return;
      const selectionBounds = getLayerBounds(selectedLayers);
      const alignBounds =
        selectedLayers.length > 1
          ? selectionBounds
          : { left: 0, top: 0, width: baseSize.width, height: baseSize.height };

      const updates = selectedLayers.map((layer) => {
        const layerTransform = layer.transform;
        if (direction === 'left') {
          return {
            layerId: layer.id,
            updates: { transform: { ...layerTransform, x: alignBounds.left } },
          };
        }
        if (direction === 'center') {
          return {
            layerId: layer.id,
            updates: {
              transform: {
                ...layerTransform,
                x: alignBounds.left + (alignBounds.width - layerTransform.width) / 2,
              },
            },
          };
        }
        if (direction === 'right') {
          return {
            layerId: layer.id,
            updates: {
              transform: {
                ...layerTransform,
                x: alignBounds.left + alignBounds.width - layerTransform.width,
              },
            },
          };
        }
        if (direction === 'top') {
          return {
            layerId: layer.id,
            updates: { transform: { ...layerTransform, y: alignBounds.top } },
          };
        }
        if (direction === 'middle') {
          return {
            layerId: layer.id,
            updates: {
              transform: {
                ...layerTransform,
                y: alignBounds.top + (alignBounds.height - layerTransform.height) / 2,
              },
            },
          };
        }
        return {
          layerId: layer.id,
          updates: {
            transform: {
              ...layerTransform,
              y: alignBounds.top + alignBounds.height - layerTransform.height,
            },
          },
        };
      });
      updateLayers(slide.id, updates);
    },
    [baseSize.height, baseSize.width, selectedLayerIds, slide, updateLayers]
  );

  const distributeSelection = useCallback(
    (axis: 'horizontal' | 'vertical') => {
      if (!slide) return;
      const selectedLayers = slide.layers.filter((layer) => selectedLayerIds.has(layer.id));
      if (selectedLayers.length < 2) return;
      const selectionBounds = getLayerBounds(selectedLayers);

      if (axis === 'horizontal') {
        const sorted = [...selectedLayers].sort(
          (a, b) => a.transform.x - b.transform.x
        );
        const totalWidth = sorted.reduce(
          (sum, layer) => sum + layer.transform.width,
          0
        );
        const gap = (selectionBounds.width - totalWidth) / (sorted.length - 1 || 1);
        let cursor = selectionBounds.left;
        const updates = sorted.map((layer) => {
          const nextUpdate = {
            layerId: layer.id,
            updates: { transform: { ...layer.transform, x: cursor } },
          };
          cursor += layer.transform.width + gap;
          return nextUpdate;
        });
        updateLayers(slide.id, updates);
      } else {
        const sorted = [...selectedLayers].sort(
          (a, b) => a.transform.y - b.transform.y
        );
        const totalHeight = sorted.reduce(
          (sum, layer) => sum + layer.transform.height,
          0
        );
        const gap = (selectionBounds.height - totalHeight) / (sorted.length - 1 || 1);
        let cursor = selectionBounds.top;
        const updates = sorted.map((layer) => {
          const nextUpdate = {
            layerId: layer.id,
            updates: { transform: { ...layer.transform, y: cursor } },
          };
          cursor += layer.transform.height + gap;
          return nextUpdate;
        });
        updateLayers(slide.id, updates);
      }
    },
    [selectedLayerIds, slide, updateLayers]
  );

  // Get effective background
  const background = slide ? resolveSlideBackground(slide) : null;
  const backgroundMediaSrc = useResolvedBackgroundMediaSrc({
    background,
    presentation,
    presentationPath,
    pendingMedia,
  });
  // Theme-aware solid color for transparent backgrounds in canvas
  // Track theme changes to update color reactively
  const [isDarkMode, setIsDarkMode] = useState(() => 
    document.documentElement.classList.contains('dark')
  );
  
  useEffect(() => {
    const observer = new MutationObserver(() => {
      setIsDarkMode(document.documentElement.classList.contains('dark'));
    });
    observer.observe(document.documentElement, {
      attributes: true,
      attributeFilter: ['class'],
    });
    return () => observer.disconnect();
  }, []);
  
  const transparentCanvasColor = useMemo(() => {
    if (!slide || background?.type !== 'transparent') return null;
    // Use theme-aware color: light gray for light mode, dark gray for dark mode
    const baseGray = isDarkMode ? TRANSPARENT_CANVAS_DARK : TRANSPARENT_CANVAS_LIGHT;
    return toRgba(baseGray, TRANSPARENT_CANVAS_OPACITY);
  }, [background?.type, slide, isDarkMode]);



  // Calculate selection colors based on background luminance
  const selectionColors = useMemo(() => {
    const scheme = getBackgroundColorScheme(background);
    return SELECTION_COLORS[scheme];
  }, [background]);

  useEffect(() => {
    if (!slideRef.current) return;

    const updateScale = (width: number, height: number) => {
      if (!width || !height) return;
      const nextScale = Math.min(width / baseSize.width, height / baseSize.height) || 1;
      setTextScale((prev) => (Math.abs(prev - nextScale) < 0.001 ? prev : nextScale));
    };

    const observer = new ResizeObserver((entries) => {
      for (const entry of entries) {
        updateScale(entry.contentRect.width, entry.contentRect.height);
      }
    });

    observer.observe(slideRef.current);
    updateScale(slideRef.current.clientWidth, slideRef.current.clientHeight);

    return () => observer.disconnect();
  }, [baseSize.width, baseSize.height]);


  // Keyboard shortcuts for layer manipulation
  useEffect(() => {
    if (!slide) return;

    const handleKeyDown = (e: KeyboardEvent) => {
      // Don't handle shortcuts when editing text
      if (editingLayerId) return;
      
      // Don't handle if focus is in an input/textarea
      const target = e.target as HTMLElement;
      if (target.tagName === 'INPUT' || target.tagName === 'TEXTAREA' || target.isContentEditable) {
        return;
      }

      const selectedLayerIds = selection.layerIds;
      const hasSelection = selectedLayerIds.length > 0;
      const isMac = navigator.platform.toUpperCase().indexOf('MAC') >= 0;
      const cmdOrCtrl = isMac ? e.metaKey : e.ctrlKey;

      // Get the first selected layer for single-layer operations
      const selectedLayerId = selectedLayerIds[0];
      const selectedLayer = selectedLayerId 
        ? slide.layers.find(l => l.id === selectedLayerId) 
        : null;

      switch (e.key) {
        // Delete selected layer(s)
        case 'Delete':
        case 'Backspace': {
          if (hasSelection) {
            e.preventDefault();
            // Delete all selected layers
            selectedLayerIds.forEach(layerId => {
              deleteLayer(slide.id, layerId);
            });
            selectLayer(null);
          }
          break;
        }

        // Deselect all
        case 'Escape': {
          if (hasSelection) {
            e.preventDefault();
            selectLayer(null);
          }
          break;
        }

        // Move layer with arrow keys
        case 'ArrowUp':
        case 'ArrowDown':
        case 'ArrowLeft':
        case 'ArrowRight': {
          if (cmdOrCtrl && e.shiftKey && hasSelection) {
            e.preventDefault();
            if (e.key === 'ArrowUp') {
              alignSelection('top');
            } else if (e.key === 'ArrowDown') {
              alignSelection('bottom');
            } else if (e.key === 'ArrowLeft') {
              alignSelection('left');
            } else if (e.key === 'ArrowRight') {
              alignSelection('right');
            }
            break;
          }
          if (hasSelection && selectedLayer && !selectedLayer.locked) {
            e.preventDefault();
            
            // Movement amount: 1px normally, 10px with shift (slide pixels)
            const moveAmount = e.shiftKey ? 10 : 1;
            
            let deltaX = 0;
            let deltaY = 0;
            
            switch (e.key) {
              case 'ArrowUp': deltaY = -moveAmount; break;
              case 'ArrowDown': deltaY = moveAmount; break;
              case 'ArrowLeft': deltaX = -moveAmount; break;
              case 'ArrowRight': deltaX = moveAmount; break;
            }

            // Apply movement to all selected layers
            const updates: { layerId: string; transform: Partial<LayerTransform> }[] = [];
            selectedLayerIds.forEach((layerId) => {
              const layer = slide.layers.find((l) => l.id === layerId);
              if (layer && !layer.locked) {
                const newX = Math.max(
                  0,
                  Math.min(baseSize.width - layer.transform.width, layer.transform.x + deltaX)
                );
                const newY = Math.max(
                  0,
                  Math.min(baseSize.height - layer.transform.height, layer.transform.y + deltaY)
                );
                updates.push({ layerId, transform: { x: newX, y: newY } });
              }
            });

            if (updates.length > 0) {
              beginLayerTransform();
              updateLayerTransforms(slide.id, updates);
              commitLayerTransform();
            }
          }
          break;
        }

        // Cycle through layers with Tab
        case 'Tab': {
          if (slide.layers.length > 0) {
            e.preventDefault();
            
            const visibleLayers = slide.layers.filter(l => l.visible && !l.locked);
            if (visibleLayers.length === 0) break;
            
            const currentIndex = selectedLayerId 
              ? visibleLayers.findIndex(l => l.id === selectedLayerId)
              : -1;
            
            let nextIndex: number;
            if (e.shiftKey) {
              // Go backwards
              nextIndex = currentIndex <= 0 ? visibleLayers.length - 1 : currentIndex - 1;
            } else {
              // Go forwards
              nextIndex = currentIndex >= visibleLayers.length - 1 ? 0 : currentIndex + 1;
            }
            
            selectLayer(visibleLayers[nextIndex].id);
          }
          break;
        }

        // Select all with Cmd/Ctrl+A
        case 'a':
        case 'A': {
          if (cmdOrCtrl && slide.layers.length > 0) {
            e.preventDefault();
            const selectableLayerIds = slide.layers
              .filter(l => l.visible && !l.locked)
              .map(l => l.id);
            selectLayers(selectableLayerIds);
          }
          break;
        }

        case 'h':
        case 'H': {
          if (cmdOrCtrl && e.shiftKey && hasSelection) {
            e.preventDefault();
            alignSelection('center');
          }
          break;
        }

        case 'm':
        case 'M': {
          if (cmdOrCtrl && e.shiftKey && hasSelection) {
            e.preventDefault();
            alignSelection('middle');
          }
          break;
        }

        case 'x':
        case 'X': {
          if (cmdOrCtrl && e.shiftKey && hasSelection) {
            e.preventDefault();
            distributeSelection('horizontal');
          }
          break;
        }

        case 'y':
        case 'Y': {
          if (cmdOrCtrl && e.shiftKey && hasSelection) {
            e.preventDefault();
            distributeSelection('vertical');
          }
          break;
        }

        // Duplicate with Cmd/Ctrl+D
        case 'd':
        case 'D': {
          if (cmdOrCtrl && hasSelection) {
            e.preventDefault();
            
            // Duplicate each selected layer
            selectedLayerIds.forEach(layerId => {
              const layer = slide.layers.find(l => l.id === layerId);
              if (layer) {
                // Create a duplicate with offset position
                const newTransform = {
                  ...layer.transform,
                  x: Math.min(baseSize.width - layer.transform.width, layer.transform.x + 2),
                  y: Math.min(baseSize.height - layer.transform.height, layer.transform.y + 2),
                };

                // Use the appropriate add function based on layer type
                switch (layer.type) {
                  case 'text': {
                    const textLayer = addTextLayer(slide.id, layer.content);
                    if (textLayer) {
                      updateLayer(slide.id, textLayer.id, {
                        transform: newTransform,
                        style: layer.style,
                      } as Partial<TextLayer>);
                      selectLayer(textLayer.id);
                    }
                    break;
                  }
                  // For other layer types, just create with default and update transform
                  default:
                    break;
                }
              }
            });
          }
          break;
        }

        // Layer ordering with [ and ]
        case '[': {
          if (hasSelection && selectedLayerId) {
            e.preventDefault();
            if (cmdOrCtrl) {
              sendLayerToBack(slide.id, selectedLayerId);
            } else {
              sendLayerBackward(slide.id, selectedLayerId);
            }
          }
          break;
        }

        case ']': {
          if (hasSelection && selectedLayerId) {
            e.preventDefault();
            if (cmdOrCtrl) {
              bringLayerToFront(slide.id, selectedLayerId);
            } else {
              bringLayerForward(slide.id, selectedLayerId);
            }
          }
          break;
        }

        default:
          break;
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [
    slide,
    editingLayerId,
    selection.layerIds,
    selectLayer,
    selectLayers,
    deleteLayer,
    beginLayerTransform,
    updateLayerTransform,
    updateLayerTransforms,
    commitLayerTransform,
    bringLayerForward,
    sendLayerBackward,
    bringLayerToFront,
    sendLayerToBack,
    alignSelection,
    distributeSelection,
    addTextLayer,
    updateLayer,
    baseSize.height,
    baseSize.width,
  ]);

  // Memoized handlers
  const handleAddTextLayer = useCallback(() => {
    if (!slide) return;
    addTextLayer(slide.id, 'Enter text here...');
  }, [slide, addTextLayer]);

  const handleLayerClick = useCallback((e: React.MouseEvent, layerId: string) => {
    e.stopPropagation();
    handleLayerSelection(e, layerId);
  }, [handleLayerSelection]);

  const handleLayerDoubleClick = useCallback((layer: Layer) => {
    if (layer.type === 'text') {
      setEditingLayerId(layer.id);
    }
  }, []);

  const handleCanvasClick = useCallback(() => {
    if (selection.layerIds.length > 0) {
      clearSelection();
    }
    setEditingLayerId(null);
  }, [selection.layerIds.length, clearSelection]);

  const handleLayerTextChange = useCallback((layerId: string, content: string) => {
    if (!slide) return;
    updateLayer(slide.id, layerId, { content } as Partial<TextLayer>);
  }, [slide, updateLayer]);

  const handleLayerBlur = useCallback(() => {
    setEditingLayerId(null);
  }, []);

  // Handle transform commit (called when drag/resize/rotate ends)
  const handleTransformCommit = useCallback((layerId: string, transform: Partial<LayerTransform>) => {
    if (!slide) return;
    const isDragCommit =
      (transform.x !== undefined || transform.y !== undefined) &&
      transform.width === undefined &&
      transform.height === undefined &&
      transform.rotation === undefined;
    const anchorLayer = slide.layers.find((layer) => layer.id === layerId) ?? null;
    const deltaX = anchorLayer && transform.x !== undefined ? transform.x - anchorLayer.transform.x : 0;
    const deltaY = anchorLayer && transform.y !== undefined ? transform.y - anchorLayer.transform.y : 0;
    const shouldMultiDrag = isDragCommit && selection.layerIds.length > 1 && anchorLayer;
    // #region agent log
    fetch('http://127.0.0.1:7247/ingest/b87235aa-ab93-4b87-9657-fe5221616d91',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({sessionId:'debug-session',runId:'pre-fix',hypothesisId:'D',location:'SlideCanvas.tsx:handleTransformCommit',message:'transform_commit',data:{isDragCommit,shouldMultiDrag,selectionCount:selection.layerIds.length,hasAnchor:!!anchorLayer},timestamp:Date.now()})}).catch(()=>{});
    // #endregion
    beginLayerTransform();
    if (shouldMultiDrag && anchorLayer) {
      const updates: { layerId: string; transform: Partial<LayerTransform> }[] = [];
      selection.layerIds.forEach((selectedId) => {
        const selectedLayer = slide.layers.find((layer) => layer.id === selectedId);
        if (!selectedLayer || selectedLayer.locked) return;
        const nextX = Math.max(
          0,
          Math.min(
            baseSize.width - selectedLayer.transform.width,
            selectedLayer.transform.x + deltaX
          )
        );
        const nextY = Math.max(
          0,
          Math.min(
            baseSize.height - selectedLayer.transform.height,
            selectedLayer.transform.y + deltaY
          )
        );
        updates.push({ layerId: selectedId, transform: { x: nextX, y: nextY } });
      });
      updateLayerTransforms(slide.id, updates);
    } else {
      updateLayerTransform(slide.id, layerId, transform);
    }
    commitLayerTransform();
  }, [
    slide,
    baseSize.height,
    baseSize.width,
    beginLayerTransform,
    updateLayerTransform,
    updateLayerTransforms,
    commitLayerTransform,
    selection.layerIds,
  ]);

  const handleGuidesChange = useCallback((nextGuides: GuideLine[]) => {
    setGuides(nextGuides);
  }, []);

  if (!slide) {
    return (
      <div className="flex h-full items-center justify-center bg-muted/50">
        <div className="text-center text-muted-foreground">
          <p>Select a slide to edit</p>
        </div>
      </div>
    );
  }

  return (
    <div className="flex h-full flex-col">
      <div
        ref={containerRef}
        className="flex-1 overflow-auto p-8 bg-muted/50"
        onClick={handleCanvasClick}
      >
        <div className="mx-auto flex w-full flex-col items-center gap-14">
          <div
            ref={slideRef}
          className={cn(
            'relative rounded-lg overflow-hidden border border-border/70 w-full',
            aspectClass
          )}
          style={
            background
              ? {
                  ...getBackgroundStyle(background),
                  ...(transparentCanvasColor
                    ? { backgroundColor: transparentCanvasColor }
                    : null),
                }
              : undefined
          }
          >
          <BackgroundMedia
            background={background}
            src={backgroundMediaSrc}
            className="absolute inset-0"
          />
            {/* Grid overlay */}
            {settings.editor.showGrid && (
              <div
                className="absolute inset-0 pointer-events-none"
                style={{
                  backgroundImage: `
                    linear-gradient(to right, rgba(255,255,255,0.1) 1px, transparent 1px),
                    linear-gradient(to bottom, rgba(255,255,255,0.1) 1px, transparent 1px)
                  `,
                  backgroundSize: `${settings.editor.gridSize * textScale}px ${settings.editor.gridSize * textScale}px`,
                }}
              />
            )}

            {/* Layers */}
            {slide.layers.map((layer) => (
              <LayerContextMenu
                key={layer.id}
                slideId={slide.id}
                layer={layer}
                onSelect={() => selectLayer(layer.id)}
                store={{
                  updateLayer,
                  deleteLayer,
                  bringLayerForward,
                  sendLayerBackward,
                  bringLayerToFront,
                  sendLayerToBack,
                }}
              >
                <MotionLayerRenderer
                  layer={layer}
                  isSelected={selectedLayerIds.has(layer.id)}
                  isEditing={editingLayerId === layer.id}
                  selectionColors={selectionColors}
                  slideRef={slideRef}
                  baseSize={baseSize}
                  editorSettings={settings.editor}
                  textScale={textScale}
                  getLayersSnapshot={getLayersSnapshot}
                  getSelectionIds={getSelectionIds}
                  onClick={(e) => handleLayerClick(e, layer.id)}
                  onDoubleClick={() => handleLayerDoubleClick(layer)}
                  onSelect={() => selectLayer(layer.id)}
                  onSelectWithEvent={(event) => handleLayerSelection(event, layer.id)}
                  onChange={(content) => handleLayerTextChange(layer.id, content)}
                  onBlur={handleLayerBlur}
                  onTransformCommit={(transform) => handleTransformCommit(layer.id, transform)}
                  onGuidesChange={handleGuidesChange}
                />
              </LayerContextMenu>
            ))}

            {guides.length > 0 && (
              <div className="absolute inset-0 pointer-events-none z-20">
                {guides.map((guide, index) => {
                  const isVertical = guide.orientation === 'vertical';
                  const positionPercent = isVertical
                    ? (guide.position / baseSize.width) * 100
                    : (guide.position / baseSize.height) * 100;

                  return (
                    <div
                      key={`${guide.orientation}-${guide.position}-${index}`}
                      className="absolute"
                      style={
                        isVertical
                          ? {
                              left: `${positionPercent}%`,
                              top: 0,
                              bottom: 0,
                              width: '1px',
                              backgroundColor: selectionColors.border,
                              boxShadow: '0 0 0 1px rgba(0,0,0,0.1)',
                            }
                          : {
                              top: `${positionPercent}%`,
                              left: 0,
                              right: 0,
                              height: '1px',
                              backgroundColor: selectionColors.border,
                              boxShadow: '0 0 0 1px rgba(0,0,0,0.1)',
                            }
                      }
                    />
                  );
                })}
              </div>
            )}

            {/* Empty state */}
            {slide.layers.length === 0 && toolbarMode !== 'theme' && (
              <div className="absolute inset-0 flex items-center justify-center">
                <Button
                  variant="outline"
                  className="bg-white/10 border-white/20 text-white hover:bg-white/20"
                  onClick={handleAddTextLayer}
                >
                  <Plus className="mr-2 h-4 w-4" />
                  Add Text
                </Button>
              </div>
            )}
          </div>

          <AssetToolbar
            slideId={slide.id}
            mode={toolbarMode}
            onAddTextLayer={onAddTextLayer}
            onAddHeadingLayer={onAddHeadingLayer}
            store={toolbarStore}
            allowMediaImport={allowMediaImport}
          />
        </div>
      </div>
    </div>
  );
}

interface MotionLayerRendererProps extends React.HTMLAttributes<HTMLDivElement> {
  layer: Layer;
  isSelected: boolean;
  isEditing: boolean;
  selectionColors: SelectionColors;
  slideRef: React.RefObject<HTMLDivElement | null>;
  baseSize: { width: number; height: number };
  editorSettings: { snapToGrid: boolean; gridSize: number };
  textScale: number;
  getLayersSnapshot: () => Layer[];
  getSelectionIds: () => string[];
  onClick: (e: React.MouseEvent) => void;
  onDoubleClick: () => void;
  onSelect: () => void;
  onSelectWithEvent: (event: React.MouseEvent | React.PointerEvent) => void;
  onChange: (content: string) => void;
  onBlur: () => void;
  onTransformCommit: (transform: Partial<LayerTransform>) => void;
  onGuidesChange: (guides: GuideLine[]) => void;
}

// Interaction mode for the layer
type InteractionMode = 'none' | 'drag' | 'resize' | 'rotate';

// High-performance layer renderer with custom pointer-based interactions
const MotionLayerRenderer = memo(function MotionLayerRenderer({
  layer,
  isSelected,
  isEditing,
  selectionColors,
  slideRef,
  baseSize,
  editorSettings,
  textScale,
  getLayersSnapshot,
  getSelectionIds,
  onClick,
  onDoubleClick,
  onSelect,
  onSelectWithEvent,
  onChange,
  onBlur,
  onTransformCommit,
  onGuidesChange,
  onContextMenu,
  onPointerDown,
  className,
  style: forwardedStyle,
  ...rest
}: MotionLayerRendererProps) {
  const layerRef = useRef<HTMLDivElement>(null);
  const contentRef = useRef<HTMLDivElement>(null);
  const setLayerElement = useCallback((node: HTMLDivElement | null) => {
    layerRef.current = node;
  }, [layer.id, layer.type]);
  const setContentElement = useCallback((node: HTMLDivElement | null) => {
    contentRef.current = node;
  }, [layer.id, layer.type]);
  
  // Interaction state - using refs to avoid re-renders during interaction
  const interactionMode = useRef<InteractionMode>('none');
  const pendingDrag = useRef(false);
  const activeLayerElement = useRef<HTMLDivElement | null>(null);
  const startPointer = useRef<{ x: number; y: number } | null>(null);
  const startTransform = useRef<LayerTransform | null>(null);
  const resizeHandle = useRef<ResizeHandle | null>(null);
  const rotateCenter = useRef<{ x: number; y: number } | null>(null);
  const startAngle = useRef<number>(0);
  const rotateMoveLoggedRef = useRef(false);
  const liveRotationRef = useRef<number | null>(null);
  const interactionStartRef = useRef<number | null>(null);
  const lastHoverLogRef = useRef<number>(0);
  const hoverMetricsRef = useRef<{ lastLogAt: number; count: number; totalMs: number; maxMs: number }>({
    lastLogAt: 0,
    count: 0,
    totalMs: 0,
    maxMs: 0,
  });
  const dragThreshold = 3;
  const multiDragStartTransforms = useRef<Record<string, LayerTransform>>({});
  const { setHoverVariant, clearHoverVariant, setOverrideVariant, clearOverrideVariant } =
    useCursor();
  const toResizeVariant = useCallback(
    (handle: ResizeHandle) => `${handle}-resize` as CursorVariant,
    []
  );
  const toRotateVariant = useCallback(
    (direction: ResizeDirection) => `rotate-${direction}` as CursorVariant,
    []
  );
  const multiDragElements = useRef<Record<string, HTMLDivElement>>({});
  const dragSelectionIds = useRef<string[]>([]);
  const snapTargetsRef = useRef<{ vertical: number[]; horizontal: number[] } | null>(null);
  const moveFrameRef = useRef<number | null>(null);
  const lastPointerEventRef = useRef<PointerEvent | null>(null);

  const logInteractionStart = useCallback((mode: InteractionMode) => {
    interactionStartRef.current = Date.now();
    // #region agent log
    fetch('http://127.0.0.1:7247/ingest/b87235aa-ab93-4b87-9657-fe5221616d91',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({sessionId:'debug-session',runId:'pre-fix',hypothesisId:'D',location:'SlideCanvas.tsx:interactionStart',message:'interaction_start',data:{mode,layerId:layer.id,selectionCount:getSelectionIds().length},timestamp:Date.now()})}).catch(()=>{});
    // #endregion
  }, [getSelectionIds, layer.id]);

  // Get container dimensions for percentage calculations
  const getContainerRect = () => slideRef.current?.getBoundingClientRect();

  // Calculate snapped value
  const snapToGrid = (value: number) => {
    if (!editorSettings.snapToGrid || editorSettings.gridSize <= 0) return value;
    return Math.round(value / editorSettings.gridSize) * editorSettings.gridSize;
  };

  // Unified pointer move handler
  const applyPointerMove = useCallback((e: PointerEvent) => {
    let targetElement = activeLayerElement.current;
    if (!targetElement && interactionMode.current === 'resize') {
      targetElement = document.querySelector(
        `[data-layer-root][data-layer-id="${layer.id}"]`
      ) as HTMLDivElement | null;
      if (targetElement) {
        activeLayerElement.current = targetElement;
      }
      if (!targetElement) {
        const hit = document.elementFromPoint(e.clientX, e.clientY) as HTMLElement | null;
        const closestRoot = hit?.closest('[data-layer-root]') as HTMLDivElement | null;
        if (closestRoot) {
          targetElement = closestRoot;
          activeLayerElement.current = closestRoot;
        }
      }
    }
    if (!targetElement || !startPointer.current || !startTransform.current) {
      return;
    }
    
    const containerRect = getContainerRect();
    if (!containerRect) return;

    const deltaX = e.clientX - startPointer.current.x;
    const deltaY = e.clientY - startPointer.current.y;
    if (interactionMode.current === 'none' && pendingDrag.current) {
      const distance = Math.hypot(deltaX, deltaY);
      if (distance < dragThreshold) {
        return;
      }
      pendingDrag.current = false;
      interactionMode.current = 'drag';
    }
    const scaleX = containerRect.width / baseSize.width;
    const scaleY = containerRect.height / baseSize.height;
    const deltaXSlide = deltaX / scaleX;
    const deltaYSlide = deltaY / scaleY;

    if (interactionMode.current === 'drag') {
      let newX = startTransform.current.x + deltaXSlide;
      let newY = startTransform.current.y + deltaYSlide;

      const snapTargets =
        snapTargetsRef.current ??
        buildSnapTargets(getLayersSnapshot(), layer.id, baseSize.width, baseSize.height);
      const snapX = getSnapForAxis(
        newX,
        startTransform.current.width,
        snapTargets.vertical,
        'vertical'
      );
      const snapY = getSnapForAxis(
        newY,
        startTransform.current.height,
        snapTargets.horizontal,
        'horizontal'
      );

      newX = snapX.value;
      newY = snapY.value;

      // Snap to grid
      newX = snapToGrid(newX);
      newY = snapToGrid(newY);

      // Clamp to bounds
      newX = Math.max(0, Math.min(baseSize.width - startTransform.current.width, newX));
      newY = Math.max(0, Math.min(baseSize.height - startTransform.current.height, newY));

      if (snapX.guide || snapY.guide) {
        onGuidesChange([snapX.guide, snapY.guide].filter(Boolean) as GuideLine[]);
      } else {
        onGuidesChange([]);
      }

      targetElement.style.left = `${(newX / baseSize.width) * 100}%`;
      targetElement.style.top = `${(newY / baseSize.height) * 100}%`;
      if (dragSelectionIds.current.length > 1) {
        const appliedDeltaX = newX - startTransform.current.x;
        const appliedDeltaY = newY - startTransform.current.y;
        dragSelectionIds.current.forEach((selectedId) => {
          if (selectedId === layer.id) return;
          const start = multiDragStartTransforms.current[selectedId];
          if (!start) return;
          const element = multiDragElements.current[selectedId];
          if (!element) return;
          let nextX = start.x + appliedDeltaX;
          let nextY = start.y + appliedDeltaY;
          nextX = Math.max(0, Math.min(baseSize.width - start.width, nextX));
          nextY = Math.max(0, Math.min(baseSize.height - start.height, nextY));
          element.style.left = `${(nextX / baseSize.width) * 100}%`;
          element.style.top = `${(nextY / baseSize.height) * 100}%`;
        });
      }
    } else if (interactionMode.current === 'resize' && resizeHandle.current) {
      onGuidesChange([]);
      const baseLockAspect = layer.transform.lockAspectRatio ?? false;
      const lockAspectRatio = e.shiftKey ? !baseLockAspect : baseLockAspect;

      const newTransform = calculateResize(
        resizeHandle.current,
        startTransform.current,
        deltaXSlide,
        deltaYSlide,
        editorSettings.snapToGrid,
        editorSettings.gridSize,
        baseSize.width,
        baseSize.height,
        lockAspectRatio,
        e.altKey
      );

      targetElement.style.left = `${(newTransform.x / baseSize.width) * 100}%`;
      targetElement.style.top = `${(newTransform.y / baseSize.height) * 100}%`;
      targetElement.style.width = `${(newTransform.width / baseSize.width) * 100}%`;
      targetElement.style.height = `${(newTransform.height / baseSize.height) * 100}%`;
    } else if (interactionMode.current === 'rotate' && rotateCenter.current) {
      const angle = Math.atan2(
        e.clientY - rotateCenter.current.y,
        e.clientX - rotateCenter.current.x
      );
      const angleDelta = angle - startAngle.current;
      const startRotation = coerceRotation(startTransform.current.rotation);
      let rotation = startRotation + (angleDelta * 180) / Math.PI;
      rotation = coerceRotation(rotation);
      liveRotationRef.current = rotation;
      
      // Snap to 15 degree increments when shift is held
      if (e.shiftKey) {
        rotation = Math.round(rotation / 15) * 15;
      }
      liveRotationRef.current = rotation;

      targetElement.style.transform = `rotate(${rotation - startRotation}deg)`;
    }
  }, [
    baseSize.height,
    baseSize.width,
    editorSettings.snapToGrid,
    editorSettings.gridSize,
    slideRef,
    layer.id,
    onGuidesChange,
    getLayersSnapshot,
  ]);

  const handlePointerMove = useCallback(
    (e: PointerEvent) => {
      lastPointerEventRef.current = e;
      if (interactionMode.current === 'rotate' && !rotateMoveLoggedRef.current) {
        rotateMoveLoggedRef.current = true;
      }
      if (moveFrameRef.current !== null) return;
      moveFrameRef.current = window.requestAnimationFrame(() => {
        moveFrameRef.current = null;
        const latest = lastPointerEventRef.current;
        if (latest) {
          applyPointerMove(latest);
        }
      });
    },
    [applyPointerMove]
  );

  // Unified pointer up handler
  const handlePointerUp = useCallback(() => {
    clearOverrideVariant();
    if (interactionMode.current === 'none' && pendingDrag.current) {
      pendingDrag.current = false;
      activeLayerElement.current = null;
      startPointer.current = null;
      startTransform.current = null;
      resizeHandle.current = null;
      rotateCenter.current = null;
      snapTargetsRef.current = null;
      dragSelectionIds.current = [];
      if (moveFrameRef.current !== null) {
        window.cancelAnimationFrame(moveFrameRef.current);
        moveFrameRef.current = null;
      }
      window.removeEventListener('pointermove', handlePointerMove);
      window.removeEventListener('pointerup', handlePointerUp);
      onGuidesChange([]);
      return;
    }
    const finalElement = activeLayerElement.current ?? layerRef.current;
    if (!finalElement || interactionMode.current === 'none') return;

    const mode = interactionMode.current;
    const interactionDuration =
      interactionStartRef.current !== null ? Date.now() - interactionStartRef.current : null;
    // #region agent log
    fetch('http://127.0.0.1:7247/ingest/b87235aa-ab93-4b87-9657-fe5221616d91',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({sessionId:'debug-session',runId:'pre-fix',hypothesisId:'D',location:'SlideCanvas.tsx:handlePointerUp',message:'interaction_end',data:{mode,interactionDurationMs:interactionDuration,layerId:layer.id},timestamp:Date.now()})}).catch(()=>{});
    // #endregion

    
    // Get final values from DOM
    const finalXPercent = parseFloat(finalElement.style.left);
    const finalYPercent = parseFloat(finalElement.style.top);
    const finalWidthPercent = parseFloat(finalElement.style.width);
    const finalHeightPercent = parseFloat(finalElement.style.height);
    const finalX = Number.isFinite(finalXPercent) ? (finalXPercent / 100) * baseSize.width : layer.transform.x;
    const finalY = Number.isFinite(finalYPercent) ? (finalYPercent / 100) * baseSize.height : layer.transform.y;
    const finalWidth = Number.isFinite(finalWidthPercent)
      ? (finalWidthPercent / 100) * baseSize.width
      : layer.transform.width;
    const finalHeight = Number.isFinite(finalHeightPercent)
      ? (finalHeightPercent / 100) * baseSize.height
      : layer.transform.height;
    
    let finalRotation = layer.transform.rotation;
    if (mode === 'rotate') {
      if (liveRotationRef.current !== null) {
        finalRotation = liveRotationRef.current;
      }
    }

    // Reset interaction state
    interactionMode.current = 'none';
    activeLayerElement.current = null;
    startPointer.current = null;
    startTransform.current = null;
    resizeHandle.current = null;
    rotateCenter.current = null;
    snapTargetsRef.current = null;
    dragSelectionIds.current = [];
    if (moveFrameRef.current !== null) {
      window.cancelAnimationFrame(moveFrameRef.current);
      moveFrameRef.current = null;
    }

    // Remove listeners
    window.removeEventListener('pointermove', handlePointerMove);
    window.removeEventListener('pointerup', handlePointerUp);

    // Commit the transform
    if (mode === 'drag') {
      onTransformCommit({ x: finalX, y: finalY });
    } else if (mode === 'resize') {
      onTransformCommit({ x: finalX, y: finalY, width: finalWidth, height: finalHeight });
    } else if (mode === 'rotate') {
      finalElement.style.transform = '';
      onTransformCommit({ rotation: coerceRotation(finalRotation) });
    }

    onGuidesChange([]);
  }, [
    baseSize.height,
    baseSize.width,
    clearOverrideVariant,
    layer.transform,
    onTransformCommit,
    handlePointerMove,
    onGuidesChange,
  ]);

  // Start drag interaction
  const handleDragStart = useCallback((e: React.PointerEvent) => {
    if (e.button !== 0) return;
    if (layer.locked || isEditing) return;
    
    e.stopPropagation();
    e.preventDefault();
    onGuidesChange([]);
    setOverrideVariant('grabbing');
    logInteractionStart('drag');

    
    // Capture pointer for smooth tracking
    (e.target as HTMLElement).setPointerCapture(e.pointerId);
    interactionMode.current = 'none';
    pendingDrag.current = true;
    activeLayerElement.current = e.currentTarget as HTMLDivElement;
    startPointer.current = { x: e.clientX, y: e.clientY };
    startTransform.current = { ...layer.transform };
    dragSelectionIds.current = getSelectionIds();
    snapTargetsRef.current = buildSnapTargets(
      getLayersSnapshot(),
      layer.id,
      baseSize.width,
      baseSize.height
    );
    if (dragSelectionIds.current.length > 1) {
      const nextTransforms: Record<string, LayerTransform> = {};
      const nextElements: Record<string, HTMLDivElement> = {};
      const layersSnapshot = getLayersSnapshot();
      dragSelectionIds.current.forEach((selectedId) => {
        const selectedLayer = layersSnapshot.find((item) => item.id === selectedId);
        if (!selectedLayer) return;
        nextTransforms[selectedId] = { ...selectedLayer.transform };
        const element = document.querySelector(
          `[data-layer-root][data-layer-id="${selectedId}"]`
        ) as HTMLDivElement | null;
        if (element) {
          nextElements[selectedId] = element;
        }
      });
      multiDragStartTransforms.current = nextTransforms;
      multiDragElements.current = nextElements;
    } else {
      multiDragStartTransforms.current = {};
      multiDragElements.current = {};
    }

    onSelectWithEvent(e);

    window.addEventListener('pointermove', handlePointerMove);
    window.addEventListener('pointerup', handlePointerUp);
  }, [
    layer.locked,
    layer.transform,
    isEditing,
    isSelected,
    onSelectWithEvent,
    handlePointerMove,
    handlePointerUp,
    onGuidesChange,
    setOverrideVariant,
    logInteractionStart,
    getLayersSnapshot,
    getSelectionIds,
    baseSize.height,
    baseSize.width,
    layer.id,
  ]);

  // Start resize interaction
  const handleResizeStart = useCallback((e: React.PointerEvent, handle: ResizeHandle) => {
    if (layer.locked) return;
    
    e.stopPropagation();
    e.preventDefault();

    
    // Capture pointer
    (e.target as HTMLElement).setPointerCapture(e.pointerId);

    setOverrideVariant(toResizeVariant(handle));
    logInteractionStart('resize');
    
    interactionMode.current = 'resize';
    startPointer.current = { x: e.clientX, y: e.clientY };
    startTransform.current = { ...layer.transform };
    resizeHandle.current = handle;
    const closestRoot = (e.currentTarget as HTMLElement).closest('[data-layer-root]') as HTMLDivElement | null;
    activeLayerElement.current = closestRoot ?? layerRef.current;
    if (!isSelected) onSelect();

    window.addEventListener('pointermove', handlePointerMove);
    window.addEventListener('pointerup', handlePointerUp);
  }, [
    layer.locked,
    layer.transform,
    isSelected,
    onSelect,
    handlePointerMove,
    handlePointerUp,
    setOverrideVariant,
    toResizeVariant,
    logInteractionStart,
  ]);

  // Start rotate interaction
  const handleRotateStart = useCallback((e: React.PointerEvent) => {
    if (layer.locked) return;

    e.stopPropagation();
    e.preventDefault();
    logInteractionStart('rotate');

    // Capture pointer
    (e.target as HTMLElement).setPointerCapture(e.pointerId);
    activeLayerElement.current =
      (e.currentTarget as HTMLElement | null)?.closest('[data-layer-root]') as
        | HTMLDivElement
        | null;
    if (!activeLayerElement.current && layerRef.current) {
      activeLayerElement.current = layerRef.current;
    }

    const containerRect = getContainerRect();
    if (!containerRect) return;

    // Calculate element center in screen coordinates
    const scaleX = containerRect.width / baseSize.width;
    const scaleY = containerRect.height / baseSize.height;
    const centerX = containerRect.left + (layer.transform.x + layer.transform.width / 2) * scaleX;
    const centerY = containerRect.top + (layer.transform.y + layer.transform.height / 2) * scaleY;

    interactionMode.current = 'rotate';
    rotateMoveLoggedRef.current = false;
    liveRotationRef.current = null;
    startPointer.current = { x: e.clientX, y: e.clientY };
    startTransform.current = { ...layer.transform };
    rotateCenter.current = { x: centerX, y: centerY };
    startAngle.current = Math.atan2(e.clientY - centerY, e.clientX - centerX);

    if (!isSelected) onSelect();

    window.addEventListener('pointermove', handlePointerMove);
    window.addEventListener('pointerup', handlePointerUp);
  }, [
    baseSize.height,
    baseSize.width,
    layer.locked,
    layer.transform,
    isSelected,
    onSelect,
    handlePointerMove,
    handlePointerUp,
    logInteractionStart,
    slideRef,
  ]);

  // Handle click (only if not from an interaction)
  const handleClick = useCallback((e: React.MouseEvent) => {
    e.stopPropagation();
    // Only process click if we weren't in an interaction
    if (interactionMode.current === 'none') {
      onClick(e);
    }
  }, [onClick]);

  // Early return AFTER all hooks to maintain consistent hook order
  if (!layer.visible) return null;

  // Base styles
  const flipX = layer.transform.flipX ? -1 : 1;
  const flipY = layer.transform.flipY ? -1 : 1;
  const baseCornerRadius = layer.transform.cornerRadius ?? 0;
  const cornerRadii = {
    topLeft: layer.transform.cornerRadiusTopLeft ?? baseCornerRadius,
    topRight: layer.transform.cornerRadiusTopRight ?? baseCornerRadius,
    bottomRight: layer.transform.cornerRadiusBottomRight ?? baseCornerRadius,
    bottomLeft: layer.transform.cornerRadiusBottomLeft ?? baseCornerRadius,
  };
  const borderRadius = `${cornerRadii.topLeft}px ${cornerRadii.topRight}px ${cornerRadii.bottomRight}px ${cornerRadii.bottomLeft}px`;
  const containerStyle: React.CSSProperties = {
    left: `${(layer.transform.x / baseSize.width) * 100}%`,
    top: `${(layer.transform.y / baseSize.height) * 100}%`,
    width: `${(layer.transform.width / baseSize.width) * 100}%`,
    height: `${(layer.transform.height / baseSize.height) * 100}%`,
    touchAction: 'none', // Prevent browser handling of touch
  };
  const layerTransformStyle: React.CSSProperties = {
    transform: `rotate(${layer.transform.rotation}deg) scale(${flipX}, ${flipY})`,
    opacity: layer.transform.opacity,
  };
  const layerContentStyle = getLayerContentStyle(layer);
  const contentStyle: React.CSSProperties = {
    ...layerTransformStyle,
    ...layerContentStyle,
    ...(blendStyle ?? {}),
  };
  const overlayStyle: React.CSSProperties = {
    ...layerTransformStyle,
    zIndex: 1,
  };
  const selectionOverlayVisible = isSelected && !isEditing;
  const rootClassName = cn(
    'absolute',
    !layer.locked &&
      !isEditing &&
      (isSelected ? 'cursor-move' : 'hover:cursor-grab active:cursor-grabbing'),
    className
  );
  const shouldTrackRotation = !layer.locked && !isEditing;
  const getHandleDistance = (
    currentTarget: HTMLDivElement,
    clientX: number,
    clientY: number
  ) => {
    const handleElements = Array.from(
      currentTarget.querySelectorAll<HTMLElement>('[data-handle]')
    );
    let minDistance = Number.POSITIVE_INFINITY;
    let nearest: ResizeHandle | null = null;
    for (const handleElement of handleElements) {
      const rect = handleElement.getBoundingClientRect();
      const centerX = rect.left + rect.width / 2;
      const centerY = rect.top + rect.height / 2;
      const distance = Math.hypot(clientX - centerX, clientY - centerY);
      if (distance < minDistance) {
        minDistance = distance;
        nearest = handleElement.getAttribute('data-handle') as ResizeHandle | null;
      }
    }
    return { minDistance, nearest, handleCount: handleElements.length };
  };

  return (
    <div
      ref={setLayerElement}
      data-layer-id={layer.id}
      data-layer-root
      className={rootClassName}
      style={{ ...containerStyle, ...forwardedStyle }}
      onPointerMoveCapture={(event) => {
        const handlerStart = performance.now();
        const target = event.target as HTMLElement | null;
        const currentTarget = event.currentTarget as HTMLDivElement | null;
        if (!shouldTrackRotation || !currentTarget) {
          return;
        }
        const isHandleTarget = !!target?.closest('[data-handle]');
        const isResizeHitTarget = !!target?.closest('[data-resize-hit]');
        if (isHandleTarget || isResizeHitTarget) {
          return;
        }
        const hoverStart = performance.now();
        const { minDistance, nearest, handleCount } = getHandleDistance(
          currentTarget,
          event.clientX,
          event.clientY
        );
        const hoverDuration = performance.now() - hoverStart;
        const canRotate =
          handleCount > 0 &&
          minDistance > ROTATION_INNER_RADIUS &&
          minDistance <= ROTATION_OUTER_RADIUS;
        if (canRotate) {
          setHoverVariant(toRotateVariant((nearest ?? 'n') as ResizeDirection));
        } else if (isSelected) {
          setHoverVariant('move');
        } else {
          clearHoverVariant();
        }
        const now = Date.now();
        const metrics = hoverMetricsRef.current;
        metrics.count += 1;
        metrics.totalMs += hoverDuration;
        if (hoverDuration > metrics.maxMs) metrics.maxMs = hoverDuration;
        if (now - metrics.lastLogAt > 1000) {
          const avg = metrics.count > 0 ? metrics.totalMs / metrics.count : 0;
          metrics.lastLogAt = now;
          metrics.count = 0;
          metrics.totalMs = 0;
          metrics.maxMs = 0;
          // #region agent log
          fetch('http://127.0.0.1:7247/ingest/b87235aa-ab93-4b87-9657-fe5221616d91',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({sessionId:'debug-session',runId:'pre-fix',hypothesisId:'E',location:'SlideCanvas.tsx:hoverMove',message:'hover_move_stats',data:{layerId:layer.id,handleCount,minDistance,canRotate,isSelected,hoverAvgMs:Math.round(avg),hoverMaxMs:Math.round(metrics.maxMs),slideLayerCount:slide.layers.length},timestamp:Date.now()})}).catch(()=>{});
          // #endregion
        }
        const handlerDuration = performance.now() - handlerStart;
        if (handlerDuration > 16) {
          // #region agent log
          fetch('http://127.0.0.1:7247/ingest/b87235aa-ab93-4b87-9657-fe5221616d91',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({sessionId:'debug-session',runId:'pre-fix',hypothesisId:'H',location:'SlideCanvas.tsx:pointerMoveCapture',message:'pointer_move_slow',data:{layerId:layer.id,handlerDurationMs:Math.round(handlerDuration),handleCount,minDistance,canRotate,isSelected},timestamp:Date.now()})}).catch(()=>{});
          // #endregion
        }
        currentTarget.style.cursor = canRotate ? ROTATION_CURSOR : isSelected ? 'move' : '';
      }}
      onPointerDownCapture={(event) => {
        const target = event.target as HTMLElement | null;
        const currentTarget = event.currentTarget as HTMLDivElement | null;
        if (!isSelected && event.button === 0) {
          onSelectWithEvent(event);
        }
        if (!shouldTrackRotation || !currentTarget) {
          return;
        }
        const isHandleTarget = !!target?.closest('[data-handle]');
        const isResizeHitTarget = !!target?.closest('[data-resize-hit]');
        const { minDistance, nearest, handleCount } = getHandleDistance(
          currentTarget,
          event.clientX,
          event.clientY
        );
        const canRotate =
          handleCount > 0 &&
          minDistance > ROTATION_INNER_RADIUS &&
          minDistance <= ROTATION_OUTER_RADIUS;
        if (isHandleTarget) {
          return;
        }
        if (!canRotate) {
          return;
        }
        setOverrideVariant(toRotateVariant((nearest ?? 'n') as ResizeDirection));
        event.stopPropagation();
        event.preventDefault();
        handleRotateStart(event);
      }}
      onPointerDown={(event) => {
        onPointerDown?.(event);
        if (!isEditing) {
          if (interactionMode.current === 'rotate') {
            return;
          }
          handleDragStart(event);
        }
      }}
      onContextMenu={onContextMenu}
      onClick={handleClick}
      onDoubleClick={onDoubleClick}
      {...rest}
    >
      {/* Layer content */}
      <div
        ref={setContentElement}
        data-layer-content
        className={cn(
          'absolute inset-0',
          !layer.locked && !isEditing && isSelected && 'cursor-move'
        )}
        style={contentStyle}
      >
        {layer.type === 'text' ? (
          <TextLayerContent
            layer={layer}
            isEditing={isEditing}
            textScale={textScale}
            onChange={onChange}
            onBlur={onBlur}
          />
        ) : (
          <LayerContent layer={layer} textScale={textScale} renderMode="canvas" />
        )}
      </div>

      {/* Selection overlay */}
      {selectionOverlayVisible && (
        <SelectionOverlay
          data-selection-overlay
          className="absolute inset-0"
          style={overlayStyle}
          onResizeStart={handleResizeStart}
          onRotateStart={handleRotateStart}
          rotation={layer.transform.rotation}
          colors={selectionColors}
        />
      )}
    </div>
  );
}, (prevProps, nextProps) => {
  return (
    prevProps.layer === nextProps.layer &&
    prevProps.isSelected === nextProps.isSelected &&
    prevProps.isEditing === nextProps.isEditing &&
    prevProps.selectionColors === nextProps.selectionColors &&
    prevProps.editorSettings === nextProps.editorSettings &&
    prevProps.getLayersSnapshot === nextProps.getLayersSnapshot &&
    prevProps.getSelectionIds === nextProps.getSelectionIds &&
    prevProps.baseSize.width === nextProps.baseSize.width &&
    prevProps.baseSize.height === nextProps.baseSize.height &&
    prevProps.textScale === nextProps.textScale
  );
});

interface SelectionOverlayProps {
  onResizeStart: (e: React.PointerEvent, handle: ResizeHandle) => void;
  onRotateStart: (e: React.PointerEvent) => void;
  rotation: number;
  colors: SelectionColors;
}

function SelectionOverlay({
  onResizeStart,
  onRotateStart,
  rotation,
  colors,
  ...rest
}: SelectionOverlayProps & React.HTMLAttributes<HTMLDivElement>) {
  const { setHoverVariant, clearHoverVariant, setOverrideVariant } = useCursor();
  const hitSize = 14;
  const edgeThickness = 8;
  const handleSize = 10;
  const rotationInnerRadius = ROTATION_INNER_RADIUS;
  const rotationOuterRadius = ROTATION_OUTER_RADIUS;
  const toResizeVariant = (handle: ResizeHandle) => `${handle}-resize` as CursorVariant;
  const toRotateVariant = (direction: ResizeDirection) =>
    `rotate-${direction}` as CursorVariant;
  const rotationHandleMetaRef = useRef<{ count: number; nearest: ResizeHandle | null }>({
    count: 0,
    nearest: null,
  });
  const loggedMissingHandlesRef = useRef(false);
  const handles: { handle: ResizeHandle; className: string }[] = [
    { handle: 'nw', className: 'top-0 left-0 -translate-x-1/2 -translate-y-1/2 cursor-nw-resize' },
    { handle: 'ne', className: 'top-0 right-0 translate-x-1/2 -translate-y-1/2 cursor-ne-resize' },
    { handle: 'se', className: 'bottom-0 right-0 translate-x-1/2 translate-y-1/2 cursor-se-resize' },
    { handle: 'sw', className: 'bottom-0 left-0 -translate-x-1/2 translate-y-1/2 cursor-sw-resize' },
  ];
  const hitAreas: { handle: ResizeHandle; className: string; style: React.CSSProperties }[] = [
    {
      handle: 'n',
      className: 'cursor-n-resize',
      style: { top: -edgeThickness / 2, left: 0, right: 0, height: edgeThickness },
    },
    {
      handle: 's',
      className: 'cursor-s-resize',
      style: { bottom: -edgeThickness / 2, left: 0, right: 0, height: edgeThickness },
    },
    {
      handle: 'e',
      className: 'cursor-e-resize',
      style: { top: 0, bottom: 0, right: -edgeThickness / 2, width: edgeThickness },
    },
    {
      handle: 'w',
      className: 'cursor-w-resize',
      style: { top: 0, bottom: 0, left: -edgeThickness / 2, width: edgeThickness },
    },
    {
      handle: 'nw',
      className: 'cursor-nw-resize',
      style: { top: -hitSize / 2, left: -hitSize / 2, width: hitSize, height: hitSize },
    },
    {
      handle: 'ne',
      className: 'cursor-ne-resize',
      style: { top: -hitSize / 2, right: -hitSize / 2, width: hitSize, height: hitSize },
    },
    {
      handle: 'se',
      className: 'cursor-se-resize',
      style: { bottom: -hitSize / 2, right: -hitSize / 2, width: hitSize, height: hitSize },
    },
    {
      handle: 'sw',
      className: 'cursor-sw-resize',
      style: { bottom: -hitSize / 2, left: -hitSize / 2, width: hitSize, height: hitSize },
    },
  ];

  const getLocalGeometry = (e: React.PointerEvent<HTMLDivElement>) => {
    const target = e.currentTarget;
    const rect = target.getBoundingClientRect();
    const centerX = rect.left + rect.width / 2;
    const centerY = rect.top + rect.height / 2;
    const dx = e.clientX - centerX;
    const dy = e.clientY - centerY;
    const radians = (rotation * Math.PI) / 180;
    const cos = Math.cos(radians);
    const sin = Math.sin(radians);
    const localX = dx * cos + dy * sin;
    const localY = -dx * sin + dy * cos;
    const halfWidth = rect.width / 2;
    const halfHeight = rect.height / 2;
    return { localX, localY, halfWidth, halfHeight };
  };

  return (
    <div
      {...rest}
      onPointerDown={(e) => {
        e.stopPropagation();
        const target = e.target as HTMLElement | null;
        const { localX, localY, halfWidth, halfHeight } = getLocalGeometry(e);
        const handleHitRadius = handleSize / 2;
        const edgeHit = edgeThickness;
        const corners: { handle: ResizeHandle; x: number; y: number }[] = [
          { handle: 'nw', x: -halfWidth, y: -halfHeight },
          { handle: 'ne', x: halfWidth, y: -halfHeight },
          { handle: 'se', x: halfWidth, y: halfHeight },
          { handle: 'sw', x: -halfWidth, y: halfHeight },
        ];
        let nearestCorner: ResizeHandle | null = null;
        let cornerDistance = Number.POSITIVE_INFINITY;
        for (const corner of corners) {
          const distance = Math.hypot(localX - corner.x, localY - corner.y);
          if (distance < cornerDistance) {
            cornerDistance = distance;
            nearestCorner = corner.handle;
          }
        }
        const edgeDistanceX = halfWidth - Math.abs(localX);
        const edgeDistanceY = halfHeight - Math.abs(localY);
        const isOutsideLayer = Math.abs(localX) > halfWidth || Math.abs(localY) > halfHeight;
        const isOnHandle = cornerDistance <= handleHitRadius;
        const isInRotationRing =
          isOutsideLayer &&
          cornerDistance > rotationInnerRadius &&
          cornerDistance <= rotationOuterRadius;
        const isNearVerticalEdge =
          edgeDistanceX >= -edgeHit &&
          edgeDistanceX <= edgeHit &&
          Math.abs(localY) <= halfHeight + edgeHit;
        const isNearHorizontalEdge =
          edgeDistanceY >= -edgeHit &&
          edgeDistanceY <= edgeHit &&
          Math.abs(localX) <= halfWidth + edgeHit;
        let action: 'resize' | 'rotate' | 'none' = 'none';
        let resizeHandle: ResizeHandle | null = null;
        if (isOnHandle && nearestCorner) {
          action = 'resize';
          resizeHandle = nearestCorner;
        } else if (isInRotationRing) {
          action = 'rotate';
        } else if (isNearVerticalEdge || isNearHorizontalEdge) {
          action = 'resize';
          if (isNearVerticalEdge && Math.abs(localY) <= halfHeight + edgeHit) {
            resizeHandle = localX >= 0 ? 'e' : 'w';
          } else if (isNearHorizontalEdge) {
            resizeHandle = localY >= 0 ? 's' : 'n';
          }
        }
        if (action === 'rotate') {
          e.stopPropagation();
          e.preventDefault();
          setOverrideVariant(
            toRotateVariant((rotationHandleMetaRef.current.nearest ?? 'n') as ResizeDirection)
          );
          onRotateStart(e);
          return;
        }
        if (action === 'resize' && resizeHandle) {
          e.stopPropagation();
          e.preventDefault();
          setOverrideVariant(toResizeVariant(resizeHandle));
          onResizeStart(e, resizeHandle);
          return;
        }
      }}
      onPointerMove={(e) => {
        const { localX, localY, halfWidth, halfHeight } = getLocalGeometry(e);
        const handleHitRadius = handleSize / 2;
        const edgeHit = edgeThickness;
        const corners = [
          { handle: 'nw', x: -halfWidth, y: -halfHeight },
          { handle: 'ne', x: halfWidth, y: -halfHeight },
          { handle: 'se', x: halfWidth, y: halfHeight },
          { handle: 'sw', x: -halfWidth, y: halfHeight },
        ] as const;
        let nearestCorner: ResizeHandle | null = null;
        let cornerDistance = Number.POSITIVE_INFINITY;
        for (const corner of corners) {
          const distance = Math.hypot(localX - corner.x, localY - corner.y);
          if (distance < cornerDistance) {
            cornerDistance = distance;
            nearestCorner = corner.handle;
          }
        }
        const edgeDistanceX = halfWidth - Math.abs(localX);
        const edgeDistanceY = halfHeight - Math.abs(localY);
        const isOutsideLayer = Math.abs(localX) > halfWidth || Math.abs(localY) > halfHeight;
        const isOnHandle = cornerDistance <= handleHitRadius;
        const isInRotationRing =
          isOutsideLayer &&
          cornerDistance > rotationInnerRadius &&
          cornerDistance <= rotationOuterRadius;
        const isNearVerticalEdge =
          edgeDistanceX >= -edgeHit &&
          edgeDistanceX <= edgeHit &&
          Math.abs(localY) <= halfHeight + edgeHit;
        const isNearHorizontalEdge =
          edgeDistanceY >= -edgeHit &&
          edgeDistanceY <= edgeHit &&
          Math.abs(localX) <= halfWidth + edgeHit;
        let cursor = 'default';
        if (isOnHandle && nearestCorner) {
          cursor = `cursor-${nearestCorner}-resize`;
        } else if (isInRotationRing) {
          cursor = ROTATION_CURSOR;
        } else if (isNearVerticalEdge) {
          cursor = 'cursor-e-resize';
          if (localX < 0) cursor = 'cursor-w-resize';
        } else if (isNearHorizontalEdge) {
          cursor = 'cursor-s-resize';
          if (localY < 0) cursor = 'cursor-n-resize';
        }
        if (isOnHandle && nearestCorner) {
          setHoverVariant(toResizeVariant(nearestCorner));
        } else if (isInRotationRing) {
          rotationHandleMetaRef.current.nearest = nearestCorner;
          setHoverVariant(
            toRotateVariant((nearestCorner ?? 'n') as ResizeDirection)
          );
        } else if (isNearVerticalEdge) {
          setHoverVariant(localX < 0 ? 'w-resize' : 'e-resize');
        } else if (isNearHorizontalEdge) {
          setHoverVariant(localY < 0 ? 'n-resize' : 's-resize');
        } else {
          clearHoverVariant();
        }
        e.currentTarget.style.cursor = cursor.startsWith('cursor-')
          ? cursor.replace('cursor-', '')
          : cursor;
      }}
      onPointerLeave={(e) => {
        clearHoverVariant();
        e.currentTarget.style.cursor = 'default';
      }}
    >

      {/* Selection border */}
      <div 
        className="absolute inset-0 pointer-events-none z-20" 
        style={{ 
          border: `1px solid ${colors.border}`,
        }}
      />

      {/* Rotation interaction zones */}
      {handles.map(({ handle, className }) => (
        <div
          key={`rotation-zone-${handle}`}
          className={cn('absolute z-10 rounded-full pointer-events-auto', className)}
          style={{
            width: rotationOuterRadius * 2,
            height: rotationOuterRadius * 2,
            backgroundColor: 'transparent',
          }}
        />
      ))}

      {/* Resize handles */}
      {handles.map(({ handle, className }) => (
        <div
          key={handle}
          data-handle={handle}
          className={cn(
            'absolute z-30 size-2.5 rounded-full shadow-md pointer-events-none',
            className
          )}
          style={{
            backgroundColor: colors.fill,
            border: `1px solid ${colors.handleBorder}`,
            touchAction: 'none',
          }}
          onPointerDown={(e) => onResizeStart(e, handle)}
        />
      ))}
    </div>
  );
}

interface TextLayerContentProps {
  layer: TextLayer;
  isEditing: boolean;
  textScale: number;
  onChange: (content: string) => void;
  onBlur: () => void;
}

function TextLayerContent({
  layer,
  isEditing,
  textScale,
  onChange,
  onBlur,
}: TextLayerContentProps) {
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const containerRef = useRef<HTMLDivElement>(null);
  const textRef = useRef<HTMLSpanElement>(null);
  const [fitScale, setFitScale] = useState(1);

  const textFit = layer.textFit || 'auto';
  const padding = layer.padding ?? 2; // Default 2% padding
  const resolvedTextFills = useMemo(() => resolveLayerFills(layer), [layer]);
  const textStyle = useMemo(() => {
    const merged = mergeTextStyle(layer.style) ?? {};
    const primary = resolvedTextFills[0];
    if (primary) {
      return { ...merged, color: toRgba(primary.color, primary.opacity) };
    }
    if (layer.fills !== undefined) {
      return { ...merged, color: 'transparent' };
    }
    return merged;
  }, [layer.style, layer.fills, layer, resolvedTextFills]);
  const renderFills = useMemo<LayerFill[]>(() => {
    if (resolvedTextFills.length > 0) return resolvedTextFills;
    if (layer.fills !== undefined) {
      return [{ id: 'empty-fill', color: 'transparent', opacity: 1, enabled: true }];
    }
    return resolvedTextFills;
  }, [layer.fills, resolvedTextFills]);
  const primaryStroke = useMemo(
    () => getPrimaryStroke(layer.strokes ?? []),
    [layer.strokes]
  );
  const textCss = getTextStyle(textStyle, textScale, primaryStroke);
  const textCssBase = useMemo(
    () => getTextStyle({ ...textStyle, color: undefined }, textScale, null),
    [textStyle, textScale]
  );
  const textCssWithStroke = useMemo(
    () => getTextStyle({ ...textStyle, color: undefined }, textScale, primaryStroke),
    [textStyle, textScale, primaryStroke]
  );
  const styleSignature = useMemo(
    () => getTextStyleSignature(textStyle, primaryStroke),
    [textStyle, primaryStroke]
  );

  // Calculate fit scale based on container and text dimensions
  useEffect(() => {
    if (textFit === 'auto' || isEditing) {
      setFitScale((prev) => (prev === 1 ? prev : 1));
      return;
    }

    const calculateFitScale = () => {
      if (!containerRef.current || !textRef.current) return;

      const container = containerRef.current;
      const text = textRef.current;
      
      // Account for padding
      const paddingPercent = padding / 100;
      const availableWidth = container.clientWidth * (1 - paddingPercent * 2);
      const availableHeight = container.clientHeight * (1 - paddingPercent * 2);

      // Reset scale to measure natural size
      text.style.transform = 'scale(1)';
      const textWidth = text.scrollWidth;
      const textHeight = text.scrollHeight;

      if (textWidth === 0 || textHeight === 0) {
        setFitScale((prev) => (prev === 1 ? prev : 1));
        return;
      }

      const scaleX = availableWidth / textWidth;
      const scaleY = availableHeight / textHeight;
      let newScale = Math.min(scaleX, scaleY);

      if (textFit === 'shrink') {
        // Only shrink, never scale up
        newScale = Math.min(1, newScale);
      }
      // For 'fill', allow both up and down scaling

      // Clamp to reasonable bounds
      newScale = Math.max(0.1, Math.min(5, newScale));
      setFitScale((prev) => (Math.abs(prev - newScale) < 0.001 ? prev : newScale));
    };

    calculateFitScale();

    // Recalculate on resize
    const observer = new ResizeObserver(calculateFitScale);
    if (containerRef.current) {
      observer.observe(containerRef.current);
    }

    return () => observer.disconnect();
  }, [textFit, padding, layer.content, styleSignature, textScale, isEditing]);

  useEffect(() => {
    if (isEditing && textareaRef.current) {
      textareaRef.current.focus();
      textareaRef.current.select();
    }
  }, [isEditing]);

  if (isEditing) {
    const editorFill = renderFills[0];
    const editorColor = editorFill ? toRgba(editorFill.color, editorFill.opacity) : textStyle?.color;
    return (
      <Textarea
        ref={textareaRef}
        value={layer.content}
        onChange={(e) => onChange(e.target.value)}
        onBlur={onBlur}
        className="w-full h-full resize-none bg-black/30 border-none focus-visible:ring-0 text-white"
        style={{ ...textCss, color: editorColor, padding: `${padding}%` }}
      />
    );
  }

  const containerStyle: React.CSSProperties = {
    ...getTextContainerStyle(textStyle),
    padding: `${padding}%`,
    overflow: layer.transform.clipContent ? 'hidden' : 'visible',
  };

  const textWrapperStyle: React.CSSProperties = {
    transform: textFit !== 'auto' ? `scale(${fitScale})` : undefined,
    transformOrigin: getTransformOrigin(textStyle),
    display: 'grid',
    width: textFit !== 'auto' ? `${100 / fitScale}%` : undefined,
  };

  return (
    <div
      ref={containerRef}
      className="w-full h-full flex select-none pointer-events-none"
      style={containerStyle}
    >
      <div style={textWrapperStyle}>
        {renderFills
          .slice()
          .reverse()
          .map((fill, index, reversed) => {
            const isTop = index === reversed.length - 1;
            const fillColor = toRgba(fill.color, fill.opacity);
            return (
              <span
                key={fill.id}
                ref={isTop ? textRef : undefined}
                className="whitespace-pre-wrap select-none"
                style={{
                  ...(isTop ? textCssWithStroke : textCssBase),
                  color: fillColor,
                  gridArea: '1 / 1',
                }}
              >
                {layer.content || 'Double-click to edit'}
              </span>
            );
          })}
      </div>
    </div>
  );
}

// Get transform origin based on text alignment

/*
interface ShapeLayerContentProps {
  layer: ShapeLayer;
}

function ShapeLayerContent({ layer }: ShapeLayerContentProps) {
  const { shapeType, style } = layer;
  const fills = resolveLayerFills(layer);
  const strokes = resolveLayerStrokes(layer);

  const baseRadius = shapeType === 'ellipse' ? null : style.cornerRadius;
  const borderRadius =
    shapeType === 'rectangle' ? `${style.cornerRadius}px` : shapeType === 'ellipse' ? '50%' : '0px';

  if (shapeType === 'line') {
    return (
      <svg className="w-full h-full" preserveAspectRatio="none">
        {strokes.map((stroke, index) => (
          <line
            key={`${stroke.id}-${index}`}
            x1="0"
            y1="50%"
            x2="100%"
            y2="50%"
            stroke={stroke.color}
            strokeWidth={stroke.width}
            strokeOpacity={stroke.opacity}
          />
        ))}
      </svg>
    );
  }

  if (shapeType === 'triangle') {
    return (
      <svg className="w-full h-full" viewBox="0 0 100 100" preserveAspectRatio="none">
        {fills.slice().reverse().map((fill, index) => (
          <polygon
            key={`${fill.id}-${index}`}
            points="50,0 100,100 0,100"
            fill={fill.color}
            fillOpacity={fill.opacity}
          />
        ))}
        {strokes.map((stroke, index) => (
          <polygon
            key={`${stroke.id}-${index}`}
            points="50,0 100,100 0,100"
            fill="none"
            stroke={stroke.color}
            strokeWidth={stroke.width}
            strokeOpacity={stroke.opacity}
          />
        ))}
      </svg>
    );
  }

  return (
    <div
      className="relative w-full h-full"
      style={{
        borderRadius,
      }}
    >
      {fills.slice().reverse().map((fill, index) => (
        <div
          key={`${fill.id}-${index}`}
          className="absolute inset-0"
          style={{
            backgroundColor: toRgba(fill.color, fill.opacity),
            borderRadius,
          }}
        />
      ))}
      {strokes.map((stroke, index) => {
        const inset = getStrokeInset(stroke);
        const sides = resolveStrokeSides(stroke);
        const radiusOffset = baseRadius === null ? 0 : Math.abs(inset);
        const resolvedRadius =
          baseRadius === null ? '50%' : `${Math.max(0, baseRadius + radiusOffset)}px`;
        const strokeColor = toRgba(stroke.color, stroke.opacity);
        return (
          <div
            key={`${stroke.id}-${index}`}
            className="absolute pointer-events-none"
            style={{
              top: inset,
              left: inset,
              right: inset,
              bottom: inset,
              borderStyle: 'solid',
              borderColor: strokeColor,
              borderTopWidth: sides.top ? stroke.width : 0,
              borderRightWidth: sides.right ? stroke.width : 0,
              borderBottomWidth: sides.bottom ? stroke.width : 0,
              borderLeftWidth: sides.left ? stroke.width : 0,
              borderRadius: resolvedRadius,
            }}
          />
        );
      })}
    </div>
  );
}

interface MediaLayerContentProps {
  layer: MediaLayer;
}

function MediaLayerContent({ layer }: MediaLayerContentProps) {
  const style: React.CSSProperties = {
    width: '100%',
    height: '100%',
    objectFit: layer.fit,
  };

  if (layer.mediaType === 'video') {
    return (
      <video
        src={layer.mediaId}
        style={style}
        loop={layer.loop}
        muted={layer.muted}
        autoPlay={layer.autoplay}
        playsInline
      />
    );
  }

  return <img src={layer.mediaId} alt="" style={style} draggable={false} />;
}

interface WebLayerContentProps {
  layer: WebLayer;
}

function WebLayerContent({ layer }: WebLayerContentProps) {
  return (
    <ScaledWebLayer
      url={layer.url}
      zoom={layer.zoom}
      baseWidth={layer.transform.width}
      baseHeight={layer.transform.height}
      interactive={false}
      title="Web content"
      className="w-full h-full bg-white"
    />
  );
}
*/

// Helper functions
function calculateResize(
  handle: ResizeHandle,
  start: LayerTransform,
  deltaX: number,
  deltaY: number,
  snapToGrid: boolean,
  gridSize: number,
  maxWidth: number,
  maxHeight: number,
  lockAspectRatio: boolean,
  resizeFromCenter: boolean
): { x: number; y: number; width: number; height: number } {
  const minSize = 5;
  const aspectRatio = start.width / start.height;
  
  // Calculate the fixed anchor point (opposite corner/edge)
  const startRight = start.x + start.width;
  const startBottom = start.y + start.height;
  const centerX = start.x + start.width / 2;
  const centerY = start.y + start.height / 2;
  
  let x = start.x;
  let y = start.y;
  let width = start.width;
  let height = start.height;

  // Calculate new dimensions based on handle
  // The key is to keep the opposite edge/corner fixed
  switch (handle) {
    case 'nw': {
      const newX = start.x + deltaX;
      const newY = start.y + deltaY;
      if (resizeFromCenter) {
        width = start.width - 2 * deltaX;
        height = start.height - 2 * deltaY;
      } else {
        width = startRight - newX;
        height = startBottom - newY;
        x = newX;
        y = newY;
      }
      break;
    }
    case 'n': {
      const newY = start.y + deltaY;
      if (resizeFromCenter) {
        height = start.height - 2 * deltaY;
      } else {
        height = startBottom - newY;
        y = newY;
      }
      break;
    }
    case 'ne': {
      const newY = start.y + deltaY;
      if (resizeFromCenter) {
        width = start.width + 2 * deltaX;
        height = start.height - 2 * deltaY;
      } else {
        width = start.width + deltaX;
        height = startBottom - newY;
        y = newY;
      }
      break;
    }
    case 'e': {
      if (resizeFromCenter) {
        width = start.width + 2 * deltaX;
      } else {
        width = start.width + deltaX;
      }
      break;
    }
    case 'se': {
      if (resizeFromCenter) {
        width = start.width + 2 * deltaX;
        height = start.height + 2 * deltaY;
      } else {
        width = start.width + deltaX;
        height = start.height + deltaY;
      }
      break;
    }
    case 's': {
      if (resizeFromCenter) {
        height = start.height + 2 * deltaY;
      } else {
        height = start.height + deltaY;
      }
      break;
    }
    case 'sw': {
      const newX = start.x + deltaX;
      if (resizeFromCenter) {
        width = start.width - 2 * deltaX;
        height = start.height + 2 * deltaY;
      } else {
        width = startRight - newX;
        height = start.height + deltaY;
        x = newX;
      }
      break;
    }
    case 'w': {
      const newX = start.x + deltaX;
      if (resizeFromCenter) {
        width = start.width - 2 * deltaX;
      } else {
        width = startRight - newX;
        x = newX;
      }
      break;
    }
  }

  if (resizeFromCenter) {
    width = Math.abs(width);
    height = Math.abs(height);
  }

  // Apply proportional scaling
  if (lockAspectRatio && aspectRatio > 0) {
    const canAdjustWidth = handle === 'e' || handle === 'w';
    const canAdjustHeight = handle === 'n' || handle === 's';
    const isCorner = !canAdjustWidth && !canAdjustHeight;
    const widthFromHeight = height * aspectRatio;
    const heightFromWidth = width / aspectRatio;

    if (isCorner) {
      if (Math.abs(width - start.width) >= Math.abs(height - start.height)) {
        height = heightFromWidth;
      } else {
        width = widthFromHeight;
      }
    } else if (canAdjustWidth) {
      height = heightFromWidth;
    } else if (canAdjustHeight) {
      width = widthFromHeight;
    }
  }

  // Apply minimum size constraints while maintaining anchor point
  if (resizeFromCenter) {
    width = Math.max(minSize, width);
    height = Math.max(minSize, height);
  } else {
    if (width < minSize) {
      const diff = minSize - width;
      width = minSize;
      // If we were resizing from left side, adjust x to maintain right edge
      if (handle === 'nw' || handle === 'w' || handle === 'sw') {
        x -= diff;
      }
    }
    
    if (height < minSize) {
      const diff = minSize - height;
      height = minSize;
      // If we were resizing from top side, adjust y to maintain bottom edge
      if (handle === 'nw' || handle === 'n' || handle === 'ne') {
        y -= diff;
      }
    }
  }

  // Apply snap to grid
  if (snapToGrid && gridSize > 0) {
    // Snap the moving edges/corners, not the anchored ones
    switch (handle) {
      case 'nw':
        x = Math.round(x / gridSize) * gridSize;
        y = Math.round(y / gridSize) * gridSize;
        width = startRight - x;
        height = startBottom - y;
        break;
      case 'n':
        y = Math.round(y / gridSize) * gridSize;
        height = startBottom - y;
        break;
      case 'ne':
        y = Math.round(y / gridSize) * gridSize;
        width = Math.round((x + width) / gridSize) * gridSize - x;
        height = startBottom - y;
        break;
      case 'e':
        width = Math.round((x + width) / gridSize) * gridSize - x;
        break;
      case 'se':
        width = Math.round((x + width) / gridSize) * gridSize - x;
        height = Math.round((y + height) / gridSize) * gridSize - y;
        break;
      case 's':
        height = Math.round((y + height) / gridSize) * gridSize - y;
        break;
      case 'sw':
        x = Math.round(x / gridSize) * gridSize;
        width = startRight - x;
        height = Math.round((y + height) / gridSize) * gridSize - y;
        break;
      case 'w':
        x = Math.round(x / gridSize) * gridSize;
        width = startRight - x;
        break;
    }
  }

  // Apply bounds constraints while maintaining anchor points
  if (resizeFromCenter) {
    const maxWidthFromCenter = Math.min(centerX * 2, (maxWidth - centerX) * 2);
    const maxHeightFromCenter = Math.min(centerY * 2, (maxHeight - centerY) * 2);

    if (lockAspectRatio && aspectRatio > 0) {
      const widthCap = Math.min(maxWidthFromCenter, maxHeightFromCenter * aspectRatio);
      width = Math.min(width, widthCap);
      height = width / aspectRatio;
      if (height > maxHeightFromCenter) {
        height = maxHeightFromCenter;
        width = height * aspectRatio;
      }
    } else {
      width = Math.min(width, maxWidthFromCenter);
      height = Math.min(height, maxHeightFromCenter);
    }

    x = centerX - width / 2;
    y = centerY - height / 2;
  } else {
    // Left boundary
    if (x < 0) {
      if (handle === 'nw' || handle === 'w' || handle === 'sw') {
        width = width + x; // Reduce width by the overflow amount
      }
      x = 0;
    }
    
    // Top boundary
    if (y < 0) {
      if (handle === 'nw' || handle === 'n' || handle === 'ne') {
        height = height + y; // Reduce height by the overflow amount
      }
      y = 0;
    }
    
    // Right boundary
    if (x + width > maxWidth) {
      width = maxWidth - x;
    }
    
    // Bottom boundary
    if (y + height > maxHeight) {
      height = maxHeight - y;
    }
  }

  // Final min size check after bounds
  width = Math.max(minSize, width);
  height = Math.max(minSize, height);

  if (resizeFromCenter) {
    x = centerX - width / 2;
    y = centerY - height / 2;
  }

  return { x, y, width, height };
}

const SNAP_THRESHOLD = 6;

function buildSnapTargets(
  layers: Layer[],
  activeLayerId: string,
  canvasWidth: number,
  canvasHeight: number
): { vertical: number[]; horizontal: number[] } {
  const vertical: number[] = [0, canvasWidth / 2, canvasWidth];
  const horizontal: number[] = [0, canvasHeight / 2, canvasHeight];

  layers.forEach((layer) => {
    if (!layer.visible || layer.id === activeLayerId) return;
    const { x, y, width, height } = layer.transform;
    vertical.push(x, x + width / 2, x + width);
    horizontal.push(y, y + height / 2, y + height);
  });

  return { vertical, horizontal };
}

function getSnapForAxis(
  value: number,
  size: number,
  targets: number[],
  orientation: GuideLine['orientation'],
  threshold = SNAP_THRESHOLD
): { value: number; guide: GuideLine | null } {
  const movingPositions = [value, value + size / 2, value + size];
  let bestDelta: number | null = null;
  let bestTarget: number | null = null;

  for (const target of targets) {
    for (const movingPosition of movingPositions) {
      const delta = target - movingPosition;
      const distance = Math.abs(delta);
      if (distance <= threshold && (bestDelta === null || distance < Math.abs(bestDelta))) {
        bestDelta = delta;
        bestTarget = target;
      }
    }
  }

  if (bestDelta === null || bestTarget === null) {
    return { value, guide: null };
  }

  return {
    value: value + bestDelta,
    guide: { orientation, position: bestTarget },
  };
}

function getLayerBounds(layers: Layer[]) {
  const left = Math.min(...layers.map((layer) => layer.transform.x));
  const top = Math.min(...layers.map((layer) => layer.transform.y));
  const right = Math.max(
    ...layers.map((layer) => layer.transform.x + layer.transform.width)
  );
  const bottom = Math.max(
    ...layers.map((layer) => layer.transform.y + layer.transform.height)
  );
  return {
    left,
    top,
    width: right - left,
    height: bottom - top,
  };
}


function getBaseSlideSize(
  aspectRatio?: '16:9' | '4:3' | '16:10',
  slideSize?: { width: number; height: number }
) {
  if (
    slideSize &&
    Number.isFinite(slideSize.width) &&
    Number.isFinite(slideSize.height) &&
    slideSize.width > 0 &&
    slideSize.height > 0
  ) {
    return {
      width: Math.round(slideSize.width),
      height: Math.round(slideSize.height),
    };
  }
  switch (aspectRatio) {
    case '4:3':
      return { width: 1440, height: 1080 };
    case '16:10':
      return { width: 1920, height: 1200 };
    case '16:9':
    default:
      return { width: 1920, height: 1080 };
  }
}

function getAspectClass(aspectRatio?: '16:9' | '4:3' | '16:10'): string {
  if (aspectRatio === '4:3') return 'aspect-[4/3]';
  if (aspectRatio === '16:10') return 'aspect-[16/10]';
  return 'aspect-video';
}

function hexToRgb(hex: string): { r: number; g: number; b: number } | null {
  hex = hex.replace(/^#/, '');
  
  if (hex.length === 3) {
    hex = hex.split('').map(c => c + c).join('');
  }
  
  if (hex.length !== 6) return null;
  
  const num = parseInt(hex, 16);
  return {
    r: (num >> 16) & 255,
    g: (num >> 8) & 255,
    b: num & 255,
  };
}

function getRelativeLuminance(r: number, g: number, b: number): number {
  const [rs, gs, bs] = [r, g, b].map(c => {
    c = c / 255;
    return c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4);
  });
  return 0.2126 * rs + 0.7152 * gs + 0.0722 * bs;
}

function getBackgroundColorScheme(background?: Background): SelectionColorScheme {
  if (!background) {
    return 'light';
  }

  let color: string | null = null;

  switch (background.type) {
    case 'solid':
      color = background.color;
      break;
    case 'gradient':
      color = background.stops?.[0]?.color;
      break;
    case 'image':
    case 'video':
      return 'light';
    default:
      return 'light';
  }

  if (!color) return 'light';

  const rgb = hexToRgb(color);
  if (!rgb) return 'light';

  const luminance = getRelativeLuminance(rgb.r, rgb.g, rgb.b);
  
  return luminance > 0.5 ? 'dark' : 'light';
}
