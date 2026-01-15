/**
 * SlideCanvas - WYSIWYG canvas for editing slide content with layer support
 * Uses pointer events for drag/resize/rotate operations
 */

import { useState, useRef, useEffect, useCallback, memo, useMemo } from 'react';
import { Plus, RotateCw } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Textarea } from '@/components/ui/textarea';
import { useEditorStore, useSettingsStore } from '@/lib/stores';
import { AssetToolbar } from './AssetToolbar';
import type {
  Slide,
  Theme,
  Layer,
  TextLayer,
  ShapeLayer,
  MediaLayer,
  WebLayer,
  Background,
  LayerTransform,
} from '@/lib/models';
import { cn } from '@/lib/utils';

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

interface SlideCanvasProps {
  slide: Slide | null;
  theme: Theme | null;
}

type ResizeHandle = 'nw' | 'n' | 'ne' | 'e' | 'se' | 's' | 'sw' | 'w';

type GuideLine = {
  orientation: 'vertical' | 'horizontal';
  position: number;
};

export function SlideCanvas({ slide, theme }: SlideCanvasProps) {
  const {
    addTextLayer,
    updateLayer,
    deleteLayer,
    selection,
    selectLayer,
    selectLayers,
    beginLayerTransform,
    updateLayerTransform,
    commitLayerTransform,
    bringLayerForward,
    sendLayerBackward,
    bringLayerToFront,
    sendLayerToBack,
  } = useEditorStore();
  const { settings } = useSettingsStore();
  const containerRef = useRef<HTMLDivElement>(null);
  const slideRef = useRef<HTMLDivElement>(null);
  const [editingLayerId, setEditingLayerId] = useState<string | null>(null);
  const [textScale, setTextScale] = useState(1);
  const [guides, setGuides] = useState<GuideLine[]>([]);
  const baseSize = useMemo(() => getBaseSlideSize(theme?.aspectRatio), [theme?.aspectRatio]);

  // Get effective background
  const background = slide?.overrides?.background || theme?.background;

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
            selectedLayerIds.forEach(layerId => {
              const layer = slide.layers.find(l => l.id === layerId);
              if (layer && !layer.locked) {
                const newX = Math.max(0, Math.min(baseSize.width - layer.transform.width, layer.transform.x + deltaX));
                const newY = Math.max(0, Math.min(baseSize.height - layer.transform.height, layer.transform.y + deltaY));
                
                beginLayerTransform();
                updateLayerTransform(slide.id, layerId, { x: newX, y: newY });
                commitLayerTransform();
              }
            });
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
    commitLayerTransform,
    bringLayerForward,
    sendLayerBackward,
    bringLayerToFront,
    sendLayerToBack,
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
    if (!selection.layerIds.includes(layerId)) {
      selectLayer(layerId);
    }
  }, [selection.layerIds, selectLayer]);

  const handleLayerDoubleClick = useCallback((layer: Layer) => {
    if (layer.type === 'text') {
      setEditingLayerId(layer.id);
    }
  }, []);

  const handleCanvasClick = useCallback(() => {
    if (selection.layerIds.length > 0) {
      selectLayer(null);
    }
    setEditingLayerId(null);
  }, [selection.layerIds.length, selectLayer]);

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
    beginLayerTransform();
    updateLayerTransform(slide.id, layerId, transform);
    commitLayerTransform();
  }, [slide, beginLayerTransform, updateLayerTransform, commitLayerTransform]);

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
        <div className="mx-auto flex flex-col items-center gap-14" style={{ maxWidth: '900px' }}>
          <div
            ref={slideRef}
            className="relative slide-aspect rounded-lg overflow-hidden shadow-2xl w-full"
            style={getBackgroundStyle(background)}
          >
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
              <MotionLayerRenderer
                key={layer.id}
                layers={slide.layers}
                layer={layer}
                theme={theme}
                isSelected={selection.layerIds.includes(layer.id)}
                isEditing={editingLayerId === layer.id}
                selectionColors={selectionColors}
                slideRef={slideRef}
                baseSize={baseSize}
                editorSettings={settings.editor}
                textScale={textScale}
                onClick={(e) => handleLayerClick(e, layer.id)}
                onDoubleClick={() => handleLayerDoubleClick(layer)}
                onSelect={() => selectLayer(layer.id)}
                onChange={(content) => handleLayerTextChange(layer.id, content)}
                onBlur={handleLayerBlur}
                onTransformCommit={(transform) => handleTransformCommit(layer.id, transform)}
                onGuidesChange={handleGuidesChange}
              />
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
            {slide.layers.length === 0 && (
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

          <AssetToolbar slideId={slide.id} />
        </div>
      </div>
    </div>
  );
}

interface MotionLayerRendererProps {
  layers: Layer[];
  layer: Layer;
  theme: Theme | null;
  isSelected: boolean;
  isEditing: boolean;
  selectionColors: SelectionColors;
  slideRef: React.RefObject<HTMLDivElement | null>;
  baseSize: { width: number; height: number };
  editorSettings: { snapToGrid: boolean; gridSize: number };
  textScale: number;
  onClick: (e: React.MouseEvent) => void;
  onDoubleClick: () => void;
  onSelect: () => void;
  onChange: (content: string) => void;
  onBlur: () => void;
  onTransformCommit: (transform: Partial<LayerTransform>) => void;
  onGuidesChange: (guides: GuideLine[]) => void;
}

// Interaction mode for the layer
type InteractionMode = 'none' | 'drag' | 'resize' | 'rotate';

// High-performance layer renderer with custom pointer-based interactions
const MotionLayerRenderer = memo(function MotionLayerRenderer({
  layers,
  layer,
  theme,
  isSelected,
  isEditing,
  selectionColors,
  slideRef,
  baseSize,
  editorSettings,
  textScale,
  onClick,
  onDoubleClick,
  onSelect,
  onChange,
  onBlur,
  onTransformCommit,
  onGuidesChange,
}: MotionLayerRendererProps) {
  const layerRef = useRef<HTMLDivElement>(null);
  
  // Interaction state - using refs to avoid re-renders during interaction
  const interactionMode = useRef<InteractionMode>('none');
  const startPointer = useRef<{ x: number; y: number } | null>(null);
  const startTransform = useRef<LayerTransform | null>(null);
  const resizeHandle = useRef<ResizeHandle | null>(null);
  const rotateCenter = useRef<{ x: number; y: number } | null>(null);
  const startAngle = useRef<number>(0);

  // Get container dimensions for percentage calculations
  const getContainerRect = () => slideRef.current?.getBoundingClientRect();

  // Calculate snapped value
  const snapToGrid = (value: number) => {
    if (!editorSettings.snapToGrid || editorSettings.gridSize <= 0) return value;
    return Math.round(value / editorSettings.gridSize) * editorSettings.gridSize;
  };

  // Unified pointer move handler
  const handlePointerMove = useCallback((e: PointerEvent) => {
    if (!layerRef.current || !startPointer.current || !startTransform.current) return;
    
    const containerRect = getContainerRect();
    if (!containerRect) return;

    const deltaX = e.clientX - startPointer.current.x;
    const deltaY = e.clientY - startPointer.current.y;
    const scaleX = containerRect.width / baseSize.width;
    const scaleY = containerRect.height / baseSize.height;
    const deltaXSlide = deltaX / scaleX;
    const deltaYSlide = deltaY / scaleY;

    if (interactionMode.current === 'drag') {
      let newX = startTransform.current.x + deltaXSlide;
      let newY = startTransform.current.y + deltaYSlide;

      const snapTargets = buildSnapTargets(layers, layer.id, baseSize.width, baseSize.height);
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

      layerRef.current.style.left = `${(newX / baseSize.width) * 100}%`;
      layerRef.current.style.top = `${(newY / baseSize.height) * 100}%`;
    } else if (interactionMode.current === 'resize' && resizeHandle.current) {
      onGuidesChange([]);
      const isCornerHandle =
        resizeHandle.current === 'nw' ||
        resizeHandle.current === 'ne' ||
        resizeHandle.current === 'se' ||
        resizeHandle.current === 'sw';
      const lockAspectRatio = isCornerHandle ? !e.shiftKey : e.shiftKey;

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

      layerRef.current.style.left = `${(newTransform.x / baseSize.width) * 100}%`;
      layerRef.current.style.top = `${(newTransform.y / baseSize.height) * 100}%`;
      layerRef.current.style.width = `${(newTransform.width / baseSize.width) * 100}%`;
      layerRef.current.style.height = `${(newTransform.height / baseSize.height) * 100}%`;
    } else if (interactionMode.current === 'rotate' && rotateCenter.current) {
      const angle = Math.atan2(
        e.clientY - rotateCenter.current.y,
        e.clientX - rotateCenter.current.x
      );
      const angleDelta = angle - startAngle.current;
      let rotation = startTransform.current.rotation + (angleDelta * 180) / Math.PI;
      
      // Snap to 15 degree increments when shift is held
      if (e.shiftKey) {
        rotation = Math.round(rotation / 15) * 15;
      }

      layerRef.current.style.transform = `rotate(${rotation}deg)`;
    }
  }, [
    baseSize.height,
    baseSize.width,
    editorSettings.snapToGrid,
    editorSettings.gridSize,
    slideRef,
    layers,
    layer.id,
    onGuidesChange,
  ]);

  // Unified pointer up handler
  const handlePointerUp = useCallback(() => {
    if (!layerRef.current || interactionMode.current === 'none') return;

    const mode = interactionMode.current;
    
    // Get final values from DOM
    const finalXPercent = parseFloat(layerRef.current.style.left);
    const finalYPercent = parseFloat(layerRef.current.style.top);
    const finalWidthPercent = parseFloat(layerRef.current.style.width);
    const finalHeightPercent = parseFloat(layerRef.current.style.height);
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
      const transformStyle = layerRef.current.style.transform;
      const rotationMatch = transformStyle.match(/rotate\(([-\d.]+)deg\)/);
      if (rotationMatch) {
        finalRotation = parseFloat(rotationMatch[1]);
      }
    }

    // Reset interaction state
    interactionMode.current = 'none';
    startPointer.current = null;
    startTransform.current = null;
    resizeHandle.current = null;
    rotateCenter.current = null;

    // Remove listeners
    window.removeEventListener('pointermove', handlePointerMove);
    window.removeEventListener('pointerup', handlePointerUp);

    // Commit the transform
    if (mode === 'drag') {
      onTransformCommit({ x: finalX, y: finalY });
    } else if (mode === 'resize') {
      onTransformCommit({ x: finalX, y: finalY, width: finalWidth, height: finalHeight });
    } else if (mode === 'rotate') {
      onTransformCommit({ rotation: finalRotation });
    }

    onGuidesChange([]);
  }, [
    baseSize.height,
    baseSize.width,
    layer.transform,
    onTransformCommit,
    handlePointerMove,
    onGuidesChange,
  ]);

  // Start drag interaction
  const handleDragStart = useCallback((e: React.PointerEvent) => {
    if (layer.locked || isEditing) return;
    
    e.stopPropagation();
    e.preventDefault();
    onGuidesChange([]);
    
    // Capture pointer for smooth tracking
    (e.target as HTMLElement).setPointerCapture(e.pointerId);
    
    interactionMode.current = 'drag';
    startPointer.current = { x: e.clientX, y: e.clientY };
    startTransform.current = { ...layer.transform };

    if (!isSelected) onSelect();

    window.addEventListener('pointermove', handlePointerMove);
    window.addEventListener('pointerup', handlePointerUp);
  }, [
    layer.locked,
    layer.transform,
    isEditing,
    isSelected,
    onSelect,
    handlePointerMove,
    handlePointerUp,
    onGuidesChange,
  ]);

  // Start resize interaction
  const handleResizeStart = useCallback((e: React.PointerEvent, handle: ResizeHandle) => {
    if (layer.locked) return;
    
    e.stopPropagation();
    e.preventDefault();
    
    // Capture pointer
    (e.target as HTMLElement).setPointerCapture(e.pointerId);
    
    interactionMode.current = 'resize';
    startPointer.current = { x: e.clientX, y: e.clientY };
    startTransform.current = { ...layer.transform };
    resizeHandle.current = handle;

    if (!isSelected) onSelect();

    window.addEventListener('pointermove', handlePointerMove);
    window.addEventListener('pointerup', handlePointerUp);
  }, [layer.locked, layer.transform, isSelected, onSelect, handlePointerMove, handlePointerUp]);

  // Start rotate interaction
  const handleRotateStart = useCallback((e: React.PointerEvent) => {
    if (layer.locked) return;
    
    e.stopPropagation();
    e.preventDefault();
    
    // Capture pointer
    (e.target as HTMLElement).setPointerCapture(e.pointerId);
    
    const containerRect = getContainerRect();
    if (!containerRect) return;

    // Calculate element center in screen coordinates
    const scaleX = containerRect.width / baseSize.width;
    const scaleY = containerRect.height / baseSize.height;
    const centerX = containerRect.left + (layer.transform.x + layer.transform.width / 2) * scaleX;
    const centerY = containerRect.top + (layer.transform.y + layer.transform.height / 2) * scaleY;

    interactionMode.current = 'rotate';
    startPointer.current = { x: e.clientX, y: e.clientY };
    startTransform.current = { ...layer.transform };
    rotateCenter.current = { x: centerX, y: centerY };
    startAngle.current = Math.atan2(e.clientY - centerY, e.clientX - centerX);

    if (!isSelected) onSelect();

    window.addEventListener('pointermove', handlePointerMove);
    window.addEventListener('pointerup', handlePointerUp);
  }, [baseSize.height, baseSize.width, layer.locked, layer.transform, isSelected, onSelect, handlePointerMove, handlePointerUp, slideRef]);

  // Handle click (only if not from an interaction)
  const handleClick = useCallback((e: React.MouseEvent) => {
    // Only process click if we weren't in an interaction
    if (interactionMode.current === 'none') {
      onClick(e);
    }
  }, [onClick]);

  // Early return AFTER all hooks to maintain consistent hook order
  if (!layer.visible) return null;

  // Base styles
  const baseStyle: React.CSSProperties = {
    left: `${(layer.transform.x / baseSize.width) * 100}%`,
    top: `${(layer.transform.y / baseSize.height) * 100}%`,
    width: `${(layer.transform.width / baseSize.width) * 100}%`,
    height: `${(layer.transform.height / baseSize.height) * 100}%`,
    transform: `rotate(${layer.transform.rotation}deg)`,
    opacity: layer.transform.opacity,
    pointerEvents: layer.locked ? 'none' : 'auto',
    touchAction: 'none', // Prevent browser handling of touch
  };

  return (
    <div
      ref={layerRef}
      className={cn(
        'absolute',
        !layer.locked && !isEditing && 'cursor-move',
        isSelected && 'z-10'
      )}
      style={baseStyle}
      onPointerDown={!isEditing ? handleDragStart : undefined}
      onClick={handleClick}
      onDoubleClick={onDoubleClick}
    >
      {/* Selection overlay */}
      {isSelected && !isEditing && (
        <SelectionOverlay
          onResizeStart={handleResizeStart}
          onRotateStart={handleRotateStart}
          colors={selectionColors}
        />
      )}

      {/* Layer content */}
      {layer.type === 'text' && (
        <TextLayerContent
          layer={layer}
          theme={theme}
          isEditing={isEditing}
          textScale={textScale}
          onChange={onChange}
          onBlur={onBlur}
        />
      )}
      {layer.type === 'shape' && <ShapeLayerContent layer={layer} />}
      {layer.type === 'media' && <MediaLayerContent layer={layer} />}
      {layer.type === 'web' && <WebLayerContent layer={layer} isSelected={isSelected} />}
    </div>
  );
}, (prevProps, nextProps) => {
  return (
    prevProps.layers === nextProps.layers &&
    prevProps.layer === nextProps.layer &&
    prevProps.isSelected === nextProps.isSelected &&
    prevProps.isEditing === nextProps.isEditing &&
    prevProps.theme === nextProps.theme &&
    prevProps.selectionColors === nextProps.selectionColors &&
    prevProps.editorSettings === nextProps.editorSettings &&
    prevProps.baseSize.width === nextProps.baseSize.width &&
    prevProps.baseSize.height === nextProps.baseSize.height &&
    prevProps.textScale === nextProps.textScale
  );
});

interface SelectionOverlayProps {
  onResizeStart: (e: React.PointerEvent, handle: ResizeHandle) => void;
  onRotateStart: (e: React.PointerEvent) => void;
  colors: SelectionColors;
}

function SelectionOverlay({ onResizeStart, onRotateStart, colors }: SelectionOverlayProps) {
  const hitSize = 14;
  const edgeThickness = 8;
  const handles: { handle: ResizeHandle; className: string }[] = [
    { handle: 'nw', className: 'top-0 left-0 -translate-x-1/2 -translate-y-1/2 cursor-nw-resize' },
    { handle: 'n', className: 'top-0 left-1/2 -translate-x-1/2 -translate-y-1/2 cursor-n-resize' },
    { handle: 'ne', className: 'top-0 right-0 translate-x-1/2 -translate-y-1/2 cursor-ne-resize' },
    { handle: 'e', className: 'top-1/2 right-0 translate-x-1/2 -translate-y-1/2 cursor-e-resize' },
    { handle: 'se', className: 'bottom-0 right-0 translate-x-1/2 translate-y-1/2 cursor-se-resize' },
    { handle: 's', className: 'bottom-0 left-1/2 -translate-x-1/2 translate-y-1/2 cursor-s-resize' },
    { handle: 'sw', className: 'bottom-0 left-0 -translate-x-1/2 translate-y-1/2 cursor-sw-resize' },
    { handle: 'w', className: 'top-1/2 left-0 -translate-x-1/2 -translate-y-1/2 cursor-w-resize' },
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

  return (
    <>
      {/* Resize hit areas */}
      {hitAreas.map(({ handle, className, style }) => (
        <div
          key={`hit-${handle}`}
          className={cn('absolute', className)}
          style={{ ...style, touchAction: 'none' }}
          onPointerDown={(e) => onResizeStart(e, handle)}
        />
      ))}

      {/* Selection border */}
      <div 
        className="absolute inset-0 pointer-events-none" 
        style={{ 
          border: `2px solid ${colors.border}`,
          boxShadow: `0 0 0 1px rgba(0,0,0,0.1), inset 0 0 0 1px rgba(255,255,255,0.1)`,
        }}
      />

      {/* Resize handles */}
      {handles.map(({ handle, className }) => (
        <div
          key={handle}
          className={cn(
            'absolute w-3 h-3 rounded-sm shadow-md transition-transform hover:scale-125 active:scale-90',
            className
          )}
          style={{
            backgroundColor: colors.fill,
            border: `2px solid ${colors.handleBorder}`,
            touchAction: 'none',
          }}
          onPointerDown={(e) => onResizeStart(e, handle)}
        />
      ))}

      {/* Rotation handle with connector line */}
      <div
        className="absolute -top-8 left-1/2 -translate-x-1/2 cursor-grab active:cursor-grabbing transition-transform hover:scale-110 active:scale-90"
        style={{ touchAction: 'none' }}
        onPointerDown={onRotateStart}
      >
        {/* Connector line behind handle */}
        <div 
          className="absolute top-1/2 left-1/2 -translate-x-1/2 w-px h-8 pointer-events-none"
          style={{ backgroundColor: colors.border }}
        />
        {/* Handle circle */}
        <div 
          className="relative w-5 h-5 rounded-full flex items-center justify-center shadow-md"
          style={{ 
            backgroundColor: colors.fill,
            border: `2px solid ${colors.handleBorder}`,
          }}
        >
          <RotateCw className="h-2.5 w-2.5" style={{ color: colors.text }} />
        </div>
      </div>
    </>
  );
}

interface TextLayerContentProps {
  layer: TextLayer;
  theme: Theme | null;
  isEditing: boolean;
  textScale: number;
  onChange: (content: string) => void;
  onBlur: () => void;
}

function TextLayerContent({ layer, theme, isEditing, textScale, onChange, onBlur }: TextLayerContentProps) {
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const containerRef = useRef<HTMLDivElement>(null);
  const textRef = useRef<HTMLSpanElement>(null);
  const [fitScale, setFitScale] = useState(1);

  const textFit = layer.textFit || 'auto';
  const padding = layer.padding ?? 2; // Default 2% padding
  const textStyle = layer.style || theme?.primaryText;
  const textCss = getTextStyle(textStyle, textScale);
  const styleSignature = useMemo(() => getTextStyleSignature(textStyle), [textStyle]);

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
    return (
      <Textarea
        ref={textareaRef}
        value={layer.content}
        onChange={(e) => onChange(e.target.value)}
        onBlur={onBlur}
        className="w-full h-full resize-none bg-black/30 border-none focus-visible:ring-0 text-white"
        style={{ ...textCss, padding: `${padding}%` }}
      />
    );
  }

  const containerStyle: React.CSSProperties = {
    ...getTextContainerStyle(textStyle),
    padding: `${padding}%`,
    overflow: 'hidden',
  };

  const textWrapperStyle: React.CSSProperties = {
    transform: textFit !== 'auto' ? `scale(${fitScale})` : undefined,
    transformOrigin: getTransformOrigin(textStyle),
    display: 'flex',
    flexDirection: 'column',
    width: textFit !== 'auto' ? `${100 / fitScale}%` : undefined,
  };

  return (
    <div ref={containerRef} className="w-full h-full flex" style={containerStyle}>
      <div style={textWrapperStyle}>
        <span
          ref={textRef}
          className="whitespace-pre-wrap"
          style={textCss}
        >
          {layer.content || 'Double-click to edit'}
        </span>
      </div>
    </div>
  );
}

// Get transform origin based on text alignment
function getTransformOrigin(style?: Partial<Theme['primaryText']>): string {
  const alignment = style?.alignment || 'center';
  const verticalAlignment = style?.verticalAlignment || 'middle';
  
  const horizontal = {
    left: 'left',
    center: 'center',
    right: 'right',
  }[alignment];

  const vertical = {
    top: 'top',
    middle: 'center',
    bottom: 'bottom',
  }[verticalAlignment];

  return `${horizontal} ${vertical}`;
}

function getTextStyleSignature(style?: Partial<Theme['primaryText']>): string {
  if (!style) return '';
  const font = style.font;
  const shadow = style.shadow;
  const outline = style.outline;
  return [
    style.alignment ?? '',
    style.verticalAlignment ?? '',
    style.color ?? '',
    font?.family ?? '',
    font?.size ?? '',
    font?.weight ?? '',
    font?.italic ?? '',
    font?.lineHeight ?? '',
    font?.letterSpacing ?? '',
    shadow?.enabled ?? '',
    shadow?.offsetX ?? '',
    shadow?.offsetY ?? '',
    shadow?.blur ?? '',
    shadow?.color ?? '',
    outline?.enabled ?? '',
    outline?.width ?? '',
    outline?.color ?? '',
  ].join('|');
}

interface ShapeLayerContentProps {
  layer: ShapeLayer;
}

function ShapeLayerContent({ layer }: ShapeLayerContentProps) {
  const { shapeType, style } = layer;

  const commonStyle: React.CSSProperties = {
    width: '100%',
    height: '100%',
    backgroundColor: style.fill,
    opacity: style.fillOpacity,
    border: `${style.strokeWidth}px solid ${style.stroke}`,
    borderRadius: shapeType === 'rectangle' ? style.cornerRadius : shapeType === 'ellipse' ? '50%' : 0,
  };

  if (shapeType === 'line') {
    return (
      <svg className="w-full h-full" preserveAspectRatio="none">
        <line
          x1="0"
          y1="50%"
          x2="100%"
          y2="50%"
          stroke={style.stroke}
          strokeWidth={style.strokeWidth}
          strokeOpacity={style.strokeOpacity}
        />
      </svg>
    );
  }

  if (shapeType === 'triangle') {
    return (
      <svg className="w-full h-full" viewBox="0 0 100 100" preserveAspectRatio="none">
        <polygon
          points="50,0 100,100 0,100"
          fill={style.fill}
          fillOpacity={style.fillOpacity}
          stroke={style.stroke}
          strokeWidth={style.strokeWidth}
          strokeOpacity={style.strokeOpacity}
        />
      </svg>
    );
  }

  return <div style={commonStyle} />;
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
  isSelected: boolean;
}

function WebLayerContent({ layer, isSelected }: WebLayerContentProps) {
  return (
    <div className="w-full h-full relative bg-white overflow-hidden">
      <iframe
        src={layer.url}
        className="w-full h-full border-0"
        style={{
          transform: `scale(${layer.zoom})`,
          transformOrigin: 'top left',
          width: `${100 / layer.zoom}%`,
          height: `${100 / layer.zoom}%`,
          pointerEvents: layer.interactive && !isSelected ? 'auto' : 'none',
        }}
        title="Web content"
        sandbox="allow-scripts allow-same-origin"
      />
      {!layer.interactive && (
        <div className="absolute inset-0 bg-transparent" />
      )}
    </div>
  );
}

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

function getBackgroundStyle(background?: Background): React.CSSProperties {
  if (!background) {
    return { backgroundColor: '#1a1a2e' };
  }

  switch (background.type) {
    case 'solid':
      return { backgroundColor: background.color };

    case 'gradient': {
      const stops = background.stops
        .map((s: { color: string; position: number }) => `${s.color} ${s.position}%`)
        .join(', ');
      return {
        background: `linear-gradient(${background.angle}deg, ${stops})`,
      };
    }

    case 'image':
      return {
        backgroundImage: `url(${background.mediaId})`,
        backgroundSize: background.fit,
        backgroundPosition: `${background.position.x}% ${background.position.y}%`,
        backgroundRepeat: 'no-repeat',
      };

    case 'video':
      return { backgroundColor: '#000' };

    default:
      return { backgroundColor: '#1a1a2e' };
  }
}

function getTextContainerStyle(style?: Partial<Theme['primaryText']>): React.CSSProperties {
  if (!style) return {};

  const alignItems = {
    top: 'flex-start',
    middle: 'center',
    bottom: 'flex-end',
  }[style.verticalAlignment || 'middle'];

  const justifyContent = {
    left: 'flex-start',
    center: 'center',
    right: 'flex-end',
  }[style.alignment || 'center'];

  return { alignItems, justifyContent };
}

function getTextStyle(style?: Partial<Theme['primaryText']>, scale = 1): React.CSSProperties {
  if (!style) {
    return {
      color: '#ffffff',
      fontSize: `${32 * scale}px`,
      fontWeight: 700,
      textAlign: 'center',
    };
  }

  const css: React.CSSProperties = {
    color: style.color || '#ffffff',
    textAlign: style.alignment || 'center',
  };

  if (style.font) {
    css.fontFamily = style.font.family;
    css.fontSize = style.font.size * scale;
    css.fontWeight = style.font.weight;
    css.fontStyle = style.font.italic ? 'italic' : 'normal';
    css.lineHeight = style.font.lineHeight;
    css.letterSpacing = style.font.letterSpacing * scale;
  }

  if (style.shadow?.enabled) {
    css.textShadow = `${style.shadow.offsetX * scale}px ${style.shadow.offsetY * scale}px ${style.shadow.blur * scale}px ${style.shadow.color}`;
  }

  if (style.outline?.enabled) {
    css.WebkitTextStroke = `${style.outline.width * scale}px ${style.outline.color}`;
  }

  return css;
}

function getBaseSlideSize(aspectRatio?: Theme['aspectRatio']) {
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
