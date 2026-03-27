/**
 * SlideInspector - Right panel for editing slide and layer properties
 */

import { type ReactNode, useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  Type,
  Image,
  Video,
  Music,
  AlignCenterHorizontal,
  AlignCenterVertical,
  AlignEndHorizontal,
  AlignEndVertical,
  AlignHorizontalDistributeCenter,
  AlignHorizontalDistributeEnd,
  AlignHorizontalDistributeStart,
  AlignStartHorizontal,
  AlignStartVertical,
  AlignVerticalDistributeCenter,
  AlignVerticalDistributeEnd,
  AlignVerticalDistributeStart,
  AlignJustify,
  FlipHorizontal,
  FlipVertical,
  Eye,
  EyeOff,
  Grid3x3,
  Maximize,
  ArrowUpDown,
  Scan,
  Lock,
  Unlock,
  RotateCw,
  Trash2,
  Plus,
  Minus,
  Square,
  Check,
  ChevronsUpDown,
  Palette,
  Settings,
} from 'lucide-react';
import { HexAlphaColorPicker, HexColorInput } from 'react-colorful';
import {
  closestCenter,
  DndContext,
  type DragEndEvent,
  KeyboardSensor,
  PointerSensor,
  useSensor,
  useSensors,
} from '@dnd-kit/core';
import {
  SortableContext,
  sortableKeyboardCoordinates,
  useSortable,
  verticalListSortingStrategy,
} from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import { restrictToParentElement, restrictToVerticalAxis } from '@dnd-kit/modifiers';
import { Input } from '@/components/ui/input';
import {
  InputGroup,
  InputGroupAddon,
  InputGroupInput,
  InputGroupTextarea,
} from '@/components/ui/input-group';
import { ButtonGroup } from '@/components/ui/button-group';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Checkbox } from '@/components/ui/checkbox';
import { ScrollArea } from '@/components/ui/scroll-area';
import { Separator } from '@/components/ui/separator';
import { Switch } from '@/components/ui/switch';
import { Kbd, KbdGroup } from '@/components/ui/kbd';
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from '@/components/ui/tooltip';
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from '@/components/ui/popover';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from '@/components/ui/command';
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group';
import {
  ControlField,
  InspectorSection,
  ScrubbableNumberInput,
} from '@/components/editor/inspector-controls';
import { LayerContextMenu } from '@/components/editor/LayerContextMenu';
import { ColorLibraryPopover } from '@/components/editor/ColorLibraryPopover';
import { useEditorStore } from '@/lib/stores';
import {
  type SlideInspectorStore,
  useEditorSlideInspectorStore,
} from '@/lib/editor/slide-surface-store';
import { useResolvedBackgroundMediaSrc } from '@/lib/media/resolveMediaUrl';
import { useSystemFonts } from '@/hooks';
import { useLayerSelection } from '@/components/editor/use-layer-selection';
import type {
  Slide,
  SongSection,
  Background,
  Presentation,
  Layer,
  TextLayer,
  ShapeLayer,
  MediaLayer,
  WebLayer,
  LayerTransform,
  BuildStep,
  SlideTransition,
  SlideTransitionType,
  AnimationPreset,
  BuildTrigger,
  MediaCueTarget,
  SlideMediaCue,
  MediaEntry,
  GradientBackground,
  GradientStop,
  ImageBackground,
  VideoBackground,
  LayerFill,
  LayerStroke,
} from '@/lib/models';
import {
  SONG_SECTIONS,
  formatSectionLabel,
  defaultSlideTransition,
  defaultPrimaryTextStyle,
  getBackgroundStyle,
  resolveSlideBackground,
} from '@/lib/models';
import { cn } from '@/lib/utils';
import { coerceRotation } from '@/lib/models/transform-utils';
import { splitHexAlpha, toHexWithAlpha, toRgba } from '@/lib/color-utils';

const controlBgClass = 'bg-muted/30';
const strokeChipControlClass = 'h-8 min-h-8 text-sm';
const fieldGroupClass = 'flex flex-1 items-center gap-0';
const clampPercent = (value: number) => Math.max(0, Math.min(100, value));

const useChipSensors = () =>
  useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 6 } }),
    useSensor(KeyboardSensor, {
      coordinateGetter: sortableKeyboardCoordinates,
    })
  );

const reorderById = <T extends { id: string }>(
  items: T[],
  activeId: string,
  overId: string
) => {
  const fromIndex = items.findIndex((item) => item.id === activeId);
  const toIndex = items.findIndex((item) => item.id === overId);
  if (fromIndex === -1 || toIndex === -1) return items;
  const next = [...items];
  const [moved] = next.splice(fromIndex, 1);
  next.splice(toIndex, 0, moved);
  return next;
};
const RESOLUTION_PRESETS = [
  { value: '1920x1080', label: '1920 x 1080 (1080p)', width: 1920, height: 1080 },
  { value: '1280x720', label: '1280 x 720 (720p)', width: 1280, height: 720 },
  { value: '3840x2160', label: '3840 x 2160 (4K)', width: 3840, height: 2160 },
  { value: '1024x768', label: '1024 x 768 (4:3)', width: 1024, height: 768 },
  { value: '1600x1200', label: '1600 x 1200 (4:3)', width: 1600, height: 1200 },
  { value: '1920x1200', label: '1920 x 1200 (16:10)', width: 1920, height: 1200 },
];
const ASPECT_RATIO_VALUES: Record<'16:9' | '4:3' | '16:10', number> = {
  '16:9': 16 / 9,
  '4:3': 4 / 3,
  '16:10': 16 / 10,
};

type InspectorMode = 'slide' | 'theme';

interface SlideInspectorProps {
  slide: Slide | null;
  mode?: InspectorMode;
  store?: SlideInspectorStore;
  themeSlideName?: string;
  onThemeSlideNameChange?: (value: string) => void;
  themeSlideLayoutType?: string;
  onThemeSlideLayoutTypeChange?: (value: string) => void;
}

export function SlideInspector({
  slide,
  mode = 'slide',
  store,
  themeSlideName,
  onThemeSlideNameChange,
  themeSlideLayoutType,
  onThemeSlideLayoutTypeChange,
}: SlideInspectorProps) {
  const editorStore = useEditorSlideInspectorStore();
  const {
    updateSlides,
    setSlidesBackground,
    setSlidesSection,
    setSlidesTransition,
    updateLayer,
    deleteLayer,
    bringLayerForward,
    sendLayerBackward,
    bringLayerToFront,
    sendLayerToBack,
    selectLayer,
    selectLayers,
    selection,
    presentation,
    filePath,
    pendingMedia,
    reorderLayer,
    addBuildStep,
    setPresentationResolution,
  } = store ?? editorStore;

  if (!slide) {
    return (
      <div className="flex h-full items-center justify-center p-4 text-muted-foreground text-sm">
        Select a slide to edit its properties
      </div>
    );
  }

  const targetSlideIds = useMemo(
    () => (selection.slideIds.length > 0 ? selection.slideIds : [slide.id]),
    [selection.slideIds, slide.id]
  );
  const isMultiSlideSelection = targetSlideIds.length > 1;
  const showSlideMeta = mode === 'slide';

  const selectedLayerIds = useMemo(() => new Set(selection.layerIds), [selection.layerIds]);
  const lastSelectedLayerId = useRef<string | null>(null);
  const inspectorRef = useRef<HTMLDivElement | null>(null);
  const selectedLayers = useMemo(
    () => slide.layers.filter((layer) => selectedLayerIds.has(layer.id)),
    [slide.layers, selectedLayerIds]
  );
  const selectedLayer = selectedLayers[0] || null;
  const layersInDisplayOrder = useMemo(() => [...slide.layers].reverse(), [slide.layers]);
  const presentationSize = useMemo(
    () => getBaseSlideSize(presentation?.manifest.aspectRatio, presentation?.manifest.slideSize),
    [presentation?.manifest.aspectRatio, presentation?.manifest.slideSize]
  );
  const sectionClassName = 'py-3 first:pt-0 last:pb-0';
  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 6 } }),
    useSensor(KeyboardSensor, {
      coordinateGetter: sortableKeyboardCoordinates,
    })
  );
  const { handleLayerSelection } = useLayerSelection({
    layers: slide.layers,
    selection,
    selectLayer,
    selectLayers,
    lastSelectedLayerId,
  });
  const [isAddingMediaCue, setIsAddingMediaCue] = useState(false);
  const [isAddingBuildStep, setIsAddingBuildStep] = useState(false);
  const [isBackgroundPopoverOpen, setIsBackgroundPopoverOpen] = useState(false);
  const [isResolutionLocked, setIsResolutionLocked] = useState(true);
  const [resolutionDraft, setResolutionDraft] = useState({ width: '', height: '' });
  const showPresentationAspect = mode === 'slide' && !selectedLayer && presentation;

  const transition = slide.animations?.transition || defaultSlideTransition;
  const effectiveBackground = useMemo(() => resolveSlideBackground(slide), [slide]);
  const isTransparentBackground = effectiveBackground?.type === 'transparent';
  const mediaEntries = presentation?.manifest.media ?? [];

  useEffect(() => {
    setIsAddingMediaCue(false);
    setIsAddingBuildStep(false);
  }, [slide.id]);

  useEffect(() => {
    const handlePointerDown = (event: PointerEvent) => {
      const container = inspectorRef.current;
      if (!container) return;
      const target = event.target as Node | null;
      if (target && container.contains(target)) return;
      const activeElement = document.activeElement;
      if (activeElement instanceof HTMLElement && container.contains(activeElement)) {
        activeElement.blur();
      }
    };

    document.addEventListener('pointerdown', handlePointerDown, true);
    return () => {
      document.removeEventListener('pointerdown', handlePointerDown, true);
    };
  }, []);

  useEffect(() => {
    setResolutionDraft({
      width: String(presentationSize.width),
      height: String(presentationSize.height),
    });
  }, [presentationSize.height, presentationSize.width]);

  const resolutionPresetValue = useMemo(() => {
    const match = RESOLUTION_PRESETS.find(
      (preset) =>
        preset.width === presentationSize.width && preset.height === presentationSize.height
    );
    return match?.value ?? 'custom';
  }, [presentationSize.height, presentationSize.width]);

  const commitResolutionDraft = useCallback(
    (draft: { width: string; height: string }) => {
      const nextWidth = Number(draft.width);
      const nextHeight = Number(draft.height);
      if (!Number.isFinite(nextWidth) || !Number.isFinite(nextHeight) || nextWidth <= 0 || nextHeight <= 0) {
        return;
      }
      setPresentationResolution({
        width: Math.round(nextWidth),
        height: Math.round(nextHeight),
      });
    },
    [setPresentationResolution]
  );

  const handleResolutionDraftChange = useCallback(
    (field: 'width' | 'height', value: string) => {
      setResolutionDraft((prev) => {
        const next = { ...prev, [field]: value };
        if (!isResolutionLocked) return next;
        const ratio =
          ASPECT_RATIO_VALUES[presentation?.manifest.aspectRatio ?? '16:9'] ??
          presentationSize.width / presentationSize.height;
        const numeric = Number(value);
        if (!Number.isFinite(numeric) || numeric <= 0) {
          return next;
        }
        if (field === 'width') {
          next.height = String(Math.round(numeric / ratio));
        } else {
          next.width = String(Math.round(numeric * ratio));
        }
        return next;
      });
    },
    [
      isResolutionLocked,
      presentation?.manifest.aspectRatio,
      presentationSize.height,
      presentationSize.width,
    ]
  );

  const applyResolutionPreset = useCallback(
    (preset: { width: number; height: number }) => {
      const draft = { width: String(preset.width), height: String(preset.height) };
      setResolutionDraft(draft);
      commitResolutionDraft(draft);
    },
    [commitResolutionDraft]
  );

  return (
    <div
      ref={inspectorRef}
      className="flex h-full min-h-0 flex-col [&_input[type=number]]:appearance-none [&_input[type=number]]:[-moz-appearance:textfield] [&_input[type=number]::-webkit-outer-spin-button]:appearance-none [&_input[type=number]::-webkit-inner-spin-button]:appearance-none [&_input[type=number]::-webkit-outer-spin-button]:m-0 [&_input[type=number]::-webkit-inner-spin-button]:m-0"
    >
      {isMultiSlideSelection && (
        <div className="shrink-0 border-b border-border/60 px-3 py-2 text-xs text-muted-foreground">
          Editing {targetSlideIds.length} selected slides
        </div>
      )}
      <div className="shrink-0 border-b border-border/60 px-3 py-2">
        <InspectorSection title="Layers" className={sectionClassName}>
          <div className="divide-y divide-border/60 rounded-md border border-border/60 overflow-hidden">
            {slide.layers.length === 0 ? (
              <div className="p-3 text-xs text-muted-foreground text-center">
                No layers yet
              </div>
            ) : (
              <DndContext
                sensors={sensors}
                collisionDetection={closestCenter}
                onDragEnd={({ active, over }: DragEndEvent) => {
                  if (!over || active.id === over.id) return;
                  const toIndex = slide.layers.findIndex((layer) => layer.id === over.id);
                  if (toIndex === -1) return;
                  reorderLayer(slide.id, String(active.id), toIndex);
                }}
                modifiers={[restrictToVerticalAxis]}
              >
                <SortableContext
                  items={layersInDisplayOrder.map((layer) => layer.id)}
                  strategy={verticalListSortingStrategy}
                >
                  {layersInDisplayOrder.map((layer) => (
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
                      <LayerListItem
                        layer={layer}
                        isSelected={selectedLayerIds.has(layer.id)}
                        onSelect={(event) => handleLayerSelection(event, layer.id)}
                        onToggleVisible={() =>
                          updateLayer(slide.id, layer.id, { visible: !layer.visible })
                        }
                        onToggleLock={() =>
                          updateLayer(slide.id, layer.id, { locked: !layer.locked })
                        }
                        onDelete={() => deleteLayer(slide.id, layer.id)}
                      />
                    </LayerContextMenu>
                  ))}
                </SortableContext>
              </DndContext>
            )}
          </div>
        </InspectorSection>
      </div>

      <ScrollArea className="flex-1 min-h-0">
        <div className="flex flex-col divide-y divide-border/60 px-3 py-2">
          {mode === 'theme' && (
            <InspectorSection title="Theme Slide" className={sectionClassName}>
              <ControlField label="Slide name">
                <Input
                  value={themeSlideName ?? ''}
                  placeholder="Add a slide name"
                  onChange={(event) => onThemeSlideNameChange?.(event.target.value)}
                />
              </ControlField>
              <ControlField label="Slide type">
                <Select
                  value={themeSlideLayoutType ?? '_none'}
                  onValueChange={(value) => onThemeSlideLayoutTypeChange?.(value)}
                >
                  <SelectTrigger className="w-full">
                    <SelectValue placeholder="No type" />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="_none">No type</SelectItem>
                    {SONG_SECTIONS.map((section) => {
                      const label = formatSectionLabel(section);
                      return (
                        <SelectItem key={section} value={label}>
                          {label}
                        </SelectItem>
                      );
                    })}
                  </SelectContent>
                </Select>
              </ControlField>
            </InspectorSection>
          )}
          {selectedLayer && (
            <>
              <InspectorSection title="Position" className={sectionClassName}>
                <LayerLayoutEditor
                  layers={selectedLayers}
                  slideSize={presentationSize}
                  onChange={(layerId, transform) => {
                    const targetLayer = slide.layers.find((layer) => layer.id === layerId);
                    if (!targetLayer) return;
                    updateLayer(slide.id, layerId, {
                      transform: { ...targetLayer.transform, ...transform },
                    });
                  }}
                />
              </InspectorSection>

              <InspectorSection title="Layout" className={sectionClassName}>
                <LayerSizeEditor
                  layers={selectedLayers}
                  onChange={(layerId, transform) => {
                    const targetLayer = slide.layers.find((layer) => layer.id === layerId);
                    if (!targetLayer) return;
                    updateLayer(slide.id, layerId, {
                      transform: { ...targetLayer.transform, ...transform },
                    });
                  }}
                  onToggleLock={(layerId, lockAspectRatio) => {
                    const targetLayer = slide.layers.find((layer) => layer.id === layerId);
                    if (!targetLayer) return;
                    updateLayer(slide.id, layerId, {
                      transform: {
                        ...targetLayer.transform,
                        lockAspectRatio,
                      },
                    });
                  }}
                />
                {selectedLayer.type === 'text' && (
                  <TextLayoutEditor
                    layer={selectedLayer}
                    onChange={(updates) =>
                      updateLayer(slide.id, selectedLayer.id, updates)
                    }
                  />
                )}
              </InspectorSection>

              <InspectorSection
                title="Appearance"
                actions={
                  <div className="flex items-center gap-1">
                    <Button
                      variant="ghost"
                      size="icon-sm"
                      onClick={() => {
                        const shouldShow = !selectedLayers.every((layer) => layer.visible);
                        selectedLayers.forEach((layer) =>
                          updateLayer(slide.id, layer.id, { visible: shouldShow })
                        );
                      }}
                      aria-label="Toggle layer visibility"
                    >
                      {selectedLayers.every((layer) => layer.visible) ? (
                        <Eye className="h-4 w-4" />
                      ) : (
                        <EyeOff className="h-4 w-4" />
                      )}
                    </Button>
                  </div>
                }
                className={sectionClassName}
              >
                <LayerAppearanceEditor
                  layers={selectedLayers}
                  onChange={(layerId, transform) => {
                    const targetLayer = slide.layers.find((layer) => layer.id === layerId);
                    if (!targetLayer) return;
                    updateLayer(slide.id, layerId, {
                      transform: { ...targetLayer.transform, ...transform },
                    });
                  }}
                />
              </InspectorSection>

              {selectedLayer.type === 'text' ? (
                <>
                  <InspectorSection title="Typography" className={sectionClassName}>
                    <TextTypographyEditor
                      layer={selectedLayer}
                      onChange={(updates) =>
                        updateLayer(slide.id, selectedLayer.id, updates)
                      }
                    />
                  </InspectorSection>
                  <InspectorSection
                    title="Fill"
                    actions={
                      <div className="flex items-center gap-1">
                        <ColorLibraryPopover
                          onSelect={(color) => {
                            const baseFills =
                              selectedLayer.fills !== undefined
                                ? selectedLayer.fills
                                : resolveLayerFillDefaults(selectedLayer);
                            const nextFill: LayerFill = {
                              id: crypto.randomUUID(),
                              color,
                              opacity: 1,
                              enabled: true,
                            };
                            updateLayer(slide.id, selectedLayer.id, {
                              fills: [...baseFills, nextFill],
                              ...syncLayerStyleWithFill(selectedLayer, [...baseFills, nextFill]),
                            });
                          }}
                          side="bottom"
                          align="end"
                        >
                          <Button variant="ghost" size="icon-sm" className="h-6 w-6">
                            <Palette className="h-3.5 w-3.5" />
                          </Button>
                        </ColorLibraryPopover>
                        <Button
                          variant="ghost"
                          size="icon-sm"
                          className="h-6 w-6"
                          onClick={() => {
                            const baseFills =
                              selectedLayer.fills !== undefined
                                ? selectedLayer.fills
                                : resolveLayerFillDefaults(selectedLayer);
                            const primary = getPrimaryFill(baseFills);
                            const nextFill: LayerFill = {
                              id: crypto.randomUUID(),
                              color:
                                primary?.color ??
                                (selectedLayer.type === 'text'
                                  ? defaultPrimaryTextStyle.color
                                  : '#3b82f6'),
                              opacity: primary?.opacity ?? 1,
                              enabled: true,
                            };
                            updateLayer(slide.id, selectedLayer.id, {
                              fills: [...baseFills, nextFill],
                              ...syncLayerStyleWithFill(selectedLayer, [...baseFills, nextFill]),
                            });
                          }}
                          aria-label="Add fill"
                        >
                          <Plus className="h-4 w-4" />
                        </Button>
                      </div>
                    }
                    className={sectionClassName}
                  >
                    <LayerFillEditor
                      layer={selectedLayer}
                      onChange={(updates) =>
                        updateLayer(slide.id, selectedLayer.id, updates)
                      }
                    />
                  </InspectorSection>
                  <InspectorSection
                    title="Stroke"
                    actions={
                      <div className="flex items-center gap-1">
                        <ColorLibraryPopover
                          onSelect={(color) => {
                            const baseStrokes =
                              selectedLayer.strokes && selectedLayer.strokes.length > 0
                                ? selectedLayer.strokes
                                : resolveLayerStrokeDefaults(selectedLayer);
                            const nextStroke = createDefaultStroke(selectedLayer, { color, opacity: 1 });
                            updateLayer(slide.id, selectedLayer.id, {
                              strokes: [...baseStrokes, nextStroke],
                              ...syncLayerStyleWithStroke(selectedLayer, [
                                ...baseStrokes,
                                nextStroke,
                              ]),
                            });
                          }}
                          side="bottom"
                          align="end"
                        >
                          <Button variant="ghost" size="icon-sm" className="h-6 w-6">
                            <Palette className="h-3.5 w-3.5" />
                          </Button>
                        </ColorLibraryPopover>
                        <Button
                          variant="ghost"
                          size="icon-sm"
                          className="h-6 w-6"
                          onClick={() => {
                            const baseStrokes =
                              selectedLayer.strokes && selectedLayer.strokes.length > 0
                                ? selectedLayer.strokes
                                : resolveLayerStrokeDefaults(selectedLayer);
                            const primary = getPrimaryStroke(baseStrokes);
                            const nextStroke = primary
                              ? { ...primary, id: crypto.randomUUID(), enabled: true }
                              : createDefaultStroke(selectedLayer);
                            updateLayer(slide.id, selectedLayer.id, {
                              strokes: [...baseStrokes, nextStroke],
                              ...syncLayerStyleWithStroke(selectedLayer, [
                                ...baseStrokes,
                                nextStroke,
                              ]),
                            });
                          }}
                          aria-label="Add stroke"
                        >
                          <Plus className="h-4 w-4" />
                        </Button>
                      </div>
                    }
                    className={sectionClassName}
                  >
                    <LayerStrokeEditor
                      layer={selectedLayer}
                      onChange={(updates) =>
                        updateLayer(slide.id, selectedLayer.id, updates)
                      }
                    />
                  </InspectorSection>
                </>
              ) : (
                <>
                  {selectedLayer.type === 'shape' && (
                    <InspectorSection
                      title="Fill"
                      actions={
                        <div className="flex items-center gap-1">
                          <ColorLibraryPopover
                            onSelect={(color) => {
                            const baseFills =
                              selectedLayer.fills !== undefined
                                ? selectedLayer.fills
                                : resolveLayerFillDefaults(selectedLayer);
                              const nextFill: LayerFill = {
                                id: crypto.randomUUID(),
                                color,
                                opacity: 1,
                                enabled: true,
                              };
                              updateLayer(slide.id, selectedLayer.id, {
                                fills: [...baseFills, nextFill],
                                ...syncLayerStyleWithFill(selectedLayer, [
                                  ...baseFills,
                                  nextFill,
                                ]),
                              });
                            }}
                            side="bottom"
                            align="end"
                          >
                            <Button variant="ghost" size="icon-sm" className="h-6 w-6">
                              <Palette className="h-3.5 w-3.5" />
                            </Button>
                          </ColorLibraryPopover>
                          <Button
                            variant="ghost"
                            size="icon-sm"
                            className="h-6 w-6"
                            onClick={() => {
                            const baseFills =
                              selectedLayer.fills !== undefined
                                ? selectedLayer.fills
                                : resolveLayerFillDefaults(selectedLayer);
                              const primary = getPrimaryFill(baseFills);
                              const nextFill: LayerFill = {
                                id: crypto.randomUUID(),
                                color: primary?.color ?? '#3b82f6',
                                opacity: primary?.opacity ?? 1,
                                enabled: true,
                              };
                              updateLayer(slide.id, selectedLayer.id, {
                                fills: [...baseFills, nextFill],
                                ...syncLayerStyleWithFill(selectedLayer, [
                                  ...baseFills,
                                  nextFill,
                                ]),
                              });
                            }}
                            aria-label="Add fill"
                          >
                            <Plus className="h-4 w-4" />
                          </Button>
                        </div>
                      }
                      className={sectionClassName}
                    >
                      <LayerFillEditor
                        layer={selectedLayer}
                        onChange={(updates) =>
                          updateLayer(slide.id, selectedLayer.id, updates)
                        }
                      />
                    </InspectorSection>
                  )}
                  {selectedLayer.type === 'shape' && (
                    <InspectorSection
                      title="Stroke"
                      actions={
                        <div className="flex items-center gap-1">
                          <ColorLibraryPopover
                            onSelect={(color) => {
                              const baseStrokes =
                                selectedLayer.strokes && selectedLayer.strokes.length > 0
                                  ? selectedLayer.strokes
                                  : resolveLayerStrokeDefaults(selectedLayer);
                              const nextStroke = createDefaultStroke(selectedLayer, {
                                color,
                                opacity: 1,
                              });
                              updateLayer(slide.id, selectedLayer.id, {
                                strokes: [...baseStrokes, nextStroke],
                                ...syncLayerStyleWithStroke(selectedLayer, [
                                  ...baseStrokes,
                                  nextStroke,
                                ]),
                              });
                            }}
                            side="bottom"
                            align="end"
                          >
                            <Button variant="ghost" size="icon-sm" className="h-6 w-6">
                              <Palette className="h-3.5 w-3.5" />
                            </Button>
                          </ColorLibraryPopover>
                          <Button
                            variant="ghost"
                            size="icon-sm"
                            className="h-6 w-6"
                            onClick={() => {
                              const baseStrokes =
                                selectedLayer.strokes && selectedLayer.strokes.length > 0
                                  ? selectedLayer.strokes
                                  : resolveLayerStrokeDefaults(selectedLayer);
                              const primary = getPrimaryStroke(baseStrokes);
                              const nextStroke = primary
                                ? { ...primary, id: crypto.randomUUID(), enabled: true }
                                : createDefaultStroke(selectedLayer);
                              updateLayer(slide.id, selectedLayer.id, {
                                strokes: [...baseStrokes, nextStroke],
                                ...syncLayerStyleWithStroke(selectedLayer, [
                                  ...baseStrokes,
                                  nextStroke,
                                ]),
                              });
                            }}
                            aria-label="Add stroke"
                          >
                            <Plus className="h-4 w-4" />
                          </Button>
                        </div>
                      }
                      className={sectionClassName}
                    >
                      <LayerStrokeEditor
                        layer={selectedLayer}
                        onChange={(updates) =>
                          updateLayer(slide.id, selectedLayer.id, updates)
                        }
                      />
                    </InspectorSection>
                  )}
                  <InspectorSection title="Style" className={sectionClassName}>
                    <LayerStyleEditor
                      layer={selectedLayer}
                      onChange={(updates) =>
                        updateLayer(slide.id, selectedLayer.id, updates)
                      }
                    />
                  </InspectorSection>
                </>
              )}
            </>
          )}

          {showSlideMeta && (
            <InspectorSection title="Slide" className={sectionClassName}>
              <ControlField label="Slide name">
                <Input
                  value={slide.sectionLabel || ''}
                  onChange={(e) =>
                    updateSlides(targetSlideIds, { sectionLabel: e.target.value })
                  }
                  placeholder={slide.section ? formatSectionLabel(slide.section) : 'Untitled'}
                  className={cn('h-8 text-xs w-full', controlBgClass)}
                />
              </ControlField>
              <ControlField label="Slide type">
                <Select
                  value={slide.section ?? '_none'}
                  onValueChange={(value) => {
                    if (value === '_none') {
                      updateSlides(targetSlideIds, { section: undefined, layoutType: undefined });
                      return;
                    }
                    const nextSection = value as SongSection;
                    const nextLabel = formatSectionLabel(nextSection);
                    updateSlides(targetSlideIds, {
                      section: nextSection,
                      layoutType: nextLabel,
                    });
                  }}
                >
                  <SelectTrigger className={cn('h-8 text-xs w-full', controlBgClass)}>
                    <SelectValue placeholder="No type" />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="_none">No type</SelectItem>
                    <Separator className="my-1" />
                    {SONG_SECTIONS.map((section) => (
                      <SelectItem key={section} value={section}>
                        {formatSectionLabel(section)}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </ControlField>
            </InspectorSection>
          )}

          {!selectedLayer && (
            <>
              {showPresentationAspect && (
                <InspectorSection title="Presentation" className={sectionClassName}>
                  <ControlField label="Resolution">
                    <Popover>
                      <PopoverTrigger asChild>
                        <Button
                          variant="outline"
                          className={cn('h-8 w-full justify-between text-xs', controlBgClass)}
                        >
                          <span className="tabular-nums">
                            {presentationSize.width} x {presentationSize.height}
                          </span>
                          <ChevronsUpDown className="h-3.5 w-3.5 opacity-50" />
                        </Button>
                      </PopoverTrigger>
                      <PopoverContent align="end" side="bottom" className="w-72">
                        <div className="grid gap-3">
                          <Select
                            value={resolutionPresetValue}
                            onValueChange={(value) => {
                              if (value === 'custom') return;
                              const preset = RESOLUTION_PRESETS.find(
                                (item) => item.value === value
                              );
                              if (!preset) return;
                              applyResolutionPreset(preset);
                            }}
                          >
                            <SelectTrigger className="h-8 text-xs">
                              <SelectValue placeholder="Custom resolution" />
                            </SelectTrigger>
                            <SelectContent>
                              <SelectItem value="custom">Custom</SelectItem>
                              {RESOLUTION_PRESETS.map((preset) => (
                                <SelectItem key={preset.value} value={preset.value}>
                                  {preset.label}
                                </SelectItem>
                              ))}
                            </SelectContent>
                          </Select>

                          <div className="grid grid-cols-[minmax(0,1fr)_auto_minmax(0,1fr)] items-center gap-2">
                            <InputGroup className="h-8">
                              <InputGroupAddon align="inline-start">W</InputGroupAddon>
                              <InputGroupInput
                                type="number"
                                inputMode="numeric"
                                min={1}
                                className={cn('h-8', controlBgClass)}
                                value={resolutionDraft.width}
                                onChange={(event) =>
                                  handleResolutionDraftChange('width', event.target.value)
                                }
                                onBlur={() => commitResolutionDraft(resolutionDraft)}
                                onKeyDown={(event) => {
                                  if (event.key === 'Enter') {
                                    commitResolutionDraft(resolutionDraft);
                                  }
                                }}
                              />
                            </InputGroup>
                            <Button
                              variant={isResolutionLocked ? 'secondary' : 'outline'}
                              size="icon-sm"
                              onClick={() => setIsResolutionLocked((prev) => !prev)}
                              aria-pressed={isResolutionLocked}
                              aria-label={
                                isResolutionLocked ? 'Unlock aspect ratio' : 'Lock aspect ratio'
                              }
                            >
                              {isResolutionLocked ? (
                                <Lock className="h-3.5 w-3.5" />
                              ) : (
                                <Unlock className="h-3.5 w-3.5" />
                              )}
                            </Button>
                            <InputGroup className="h-8">
                              <InputGroupAddon align="inline-start">H</InputGroupAddon>
                              <InputGroupInput
                                type="number"
                                inputMode="numeric"
                                min={1}
                                className={cn('h-8', controlBgClass)}
                                value={resolutionDraft.height}
                                onChange={(event) =>
                                  handleResolutionDraftChange('height', event.target.value)
                                }
                                onBlur={() => commitResolutionDraft(resolutionDraft)}
                                onKeyDown={(event) => {
                                  if (event.key === 'Enter') {
                                    commitResolutionDraft(resolutionDraft);
                                  }
                                }}
                              />
                            </InputGroup>
                          </div>
                        </div>
                      </PopoverContent>
                    </Popover>
                  </ControlField>
                </InspectorSection>
              )}
              <InspectorSection
                title="Background"
                actions={
                  <div className="flex items-center gap-1">
                    <ColorLibraryPopover
                      onSelect={(color) =>
                        setSlidesBackground(targetSlideIds, { type: 'solid', color })
                      }
                      side="bottom"
                      align="end"
                    >
                      <Button variant="ghost" size="icon-sm" className="h-6 w-6">
                        <Palette className="h-3.5 w-3.5" />
                      </Button>
                    </ColorLibraryPopover>
                    {isTransparentBackground && (
                      <Button
                        variant="ghost"
                        size="icon-sm"
                        className="h-6 w-6"
                        onClick={() => setIsBackgroundPopoverOpen(true)}
                        aria-label="Add background"
                      >
                        <Plus className="h-4 w-4" />
                      </Button>
                    )}
                  </div>
                }
                className={sectionClassName}
              >
                <SlideBackgroundEditor
                  slide={slide}
                  mediaEntries={mediaEntries}
                  presentation={presentation}
                  presentationPath={filePath ?? null}
                  pendingMedia={pendingMedia}
                  open={isBackgroundPopoverOpen}
                  onOpenChange={setIsBackgroundPopoverOpen}
                  onSetBackground={(background) => setSlidesBackground(targetSlideIds, background)}
                />
              </InspectorSection>
            </>
          )}

          {showSlideMeta && (
            <>
              <InspectorSection title="Transition" className={sectionClassName}>
            <TransitionEditor
              transition={transition}
              onChange={(t) =>
                setSlidesTransition(targetSlideIds, { ...transition, ...t } as SlideTransition)
              }
            />
              </InspectorSection>

              <InspectorSection
                title="Media Cues"
                actions={
                  <Button
                    variant="ghost"
                    size="icon-sm"
                    onClick={() => setIsAddingMediaCue((prev) => !prev)}
                    aria-label="Add media cue"
                  >
                    <Plus className="h-4 w-4" />
                  </Button>
                }
                className={sectionClassName}
              >
                <MediaCuesEditor
                  slide={slide}
                  mediaEntries={mediaEntries}
                  onChange={(nextCues) => updateSlides(targetSlideIds, { mediaCues: nextCues })}
                  showAdd={isAddingMediaCue}
                />
              </InspectorSection>

              <InspectorSection
                title="Build"
                actions={
                  <Button
                    variant="ghost"
                    size="icon-sm"
                    onClick={() => setIsAddingBuildStep((prev) => !prev)}
                    aria-label="Add build step"
                  >
                    <Plus className="h-4 w-4" />
                  </Button>
                }
                className={sectionClassName}
              >
                <div className="space-y-3">
                  {slide.animations?.buildIn && slide.animations.buildIn.length > 0 && (
                    <div className="text-xs text-muted-foreground">
                      {`${slide.animations.buildIn.length} build step(s)`}
                    </div>
                  )}
                  {isAddingBuildStep && (
                    <BuildAnimationEditor
                      slide={slide}
                      onAddStep={(step) => addBuildStep(slide.id, step)}
                    />
                  )}
                </div>
              </InspectorSection>

              <InspectorSection title="Notes" className={sectionClassName}>
                <ControlField label="Speaker Notes" hint="Visible to the presenter only.">
                  <InputGroup className="min-h-[96px]">
                    <InputGroupTextarea
                      value={slide.notes || ''}
                      onChange={(e) => updateSlides(targetSlideIds, { notes: e.target.value })}
                      placeholder="Add notes for this slide..."
                      className={cn('text-xs min-h-[96px]', controlBgClass)}
                    />
                  </InputGroup>
                </ControlField>
              </InspectorSection>
            </>
          )}

        </div>
      </ScrollArea>
    </div>
  );
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

// Layer List Item
interface LayerListItemProps extends React.HTMLAttributes<HTMLDivElement> {
  layer: Layer;
  isSelected: boolean;
  onSelect: React.MouseEventHandler<HTMLDivElement>;
  onToggleVisible: () => void;
  onToggleLock: () => void;
  onDelete: () => void;
}

function LayerListItem({
  layer,
  isSelected,
  onSelect,
  onToggleVisible,
  onToggleLock,
  onDelete,
  className,
  onClick,
  ...rest
}: LayerListItemProps) {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({
    id: layer.id,
  });
  const style = {
    transform: CSS.Transform.toString(transform),
    transition,
  };
  const layerTypeIcons = {
    text: <Type className="h-3 w-3" />,
    shape: <div className="w-3 h-3 bg-current rounded-sm" />,
    media: <Image className="h-3 w-3" />,
    web: <Settings className="h-3 w-3" />,
  };

  return (
    <div
      ref={setNodeRef}
      style={style}
      className={cn(
        'group flex items-center gap-2 px-2 py-2 cursor-grab active:cursor-grabbing',
        isSelected && 'bg-accent',
        !layer.visible && 'opacity-50',
        isDragging && 'opacity-70',
        className
      )}
      onClick={(event) => {
        onClick?.(event);
        onSelect(event);
      }}
      {...rest}
      {...attributes}
      {...listeners}
    >
      <span className="text-muted-foreground">{layerTypeIcons[layer.type]}</span>
      <span className="flex-1 text-xs truncate">{layer.name}</span>
      <div className="flex items-center gap-0.5">
        <Button
          variant="ghost"
          size="icon"
          className="h-5 w-5 opacity-0 transition-opacity group-hover:opacity-100 focus-visible:opacity-100 text-muted-foreground hover:bg-accent hover:text-accent-foreground"
          onClick={(e) => {
            e.stopPropagation();
            onToggleVisible();
          }}
        >
          {layer.visible ? <Eye className="h-2.5 w-2.5" /> : <EyeOff className="h-2.5 w-2.5" />}
        </Button>
        <Button
          variant="ghost"
          size="icon"
          className="h-5 w-5 opacity-0 transition-opacity group-hover:opacity-100 focus-visible:opacity-100 text-muted-foreground hover:bg-accent hover:text-accent-foreground"
          onClick={(e) => {
            e.stopPropagation();
            onToggleLock();
          }}
        >
          {layer.locked ? <Lock className="h-2.5 w-2.5" /> : <Unlock className="h-2.5 w-2.5" />}
        </Button>
        <Button
          variant="ghost"
          size="icon"
          className="h-5 w-5 opacity-0 transition-opacity group-hover:opacity-100 focus-visible:opacity-100 text-destructive/80 hover:bg-destructive/10 hover:text-destructive"
          onClick={(e) => {
            e.stopPropagation();
            onDelete();
          }}
        >
          <Trash2 className="h-2.5 w-2.5" />
        </Button>
      </div>
    </div>
  );
}

interface SortableChipRowProps {
  id: string;
  ariaLabel: string;
  children: ReactNode;
  align?: 'center' | 'start';
  className?: string;
}

function SortableChipRow({
  id,
  ariaLabel,
  children,
  align = 'center',
  className,
}: SortableChipRowProps) {
  const {
    attributes,
    listeners,
    setNodeRef,
    transform,
    transition,
    isDragging,
  } = useSortable({ id });
  const style = {
    transform: CSS.Transform.toString(transform),
    transition,
  };
  const isInteractiveTarget = (event: React.SyntheticEvent) => {
    const target = event.target as HTMLElement | null;
    return Boolean(
      target?.closest(
        'input, textarea, select, button, [data-slot="input-group"], [data-no-dnd="true"]'
      )
    );
  };
  const dragListeners = {
    ...listeners,
    onPointerDown: (event: React.PointerEvent) => {
      if (isInteractiveTarget(event)) return;
      listeners.onPointerDown?.(event);
    },
    onKeyDown: (event: React.KeyboardEvent) => {
      if (isInteractiveTarget(event)) return;
      listeners.onKeyDown?.(event);
    },
  };

  return (
    <div
      ref={setNodeRef}
      style={style}
      className={cn(
        'relative flex gap-2 cursor-grab active:cursor-grabbing after:absolute after:-inset-y-1 after:-left-3 after:-right-3 after:bg-muted/30 after:opacity-0 after:content-[\'\'] after:pointer-events-none focus-within:after:opacity-100',
        align === 'start' ? 'items-start' : 'items-center',
        isDragging && 'opacity-70',
        className
      )}
      role="button"
      tabIndex={0}
      aria-label={ariaLabel}
      {...attributes}
      {...dragListeners}
    >
      <div className="relative z-10 flex flex-1 items-center gap-2">{children}</div>
    </div>
  );
}

// Layer Layout Editor
interface LayerLayoutEditorProps {
  layers: Layer[];
  slideSize: { width: number; height: number };
  onChange: (layerId: string, transform: Partial<LayerTransform>) => void;
}

function LayerLayoutEditor({ layers, slideSize, onChange }: LayerLayoutEditorProps) {
  const primaryLayer = layers[0];
  if (!primaryLayer) return null;

  const { transform } = primaryLayer;
  const selectionBounds = getLayerBounds(layers);
  const canDistribute = layers.length > 1;
  const alignBounds =
    layers.length > 1
      ? selectionBounds
      : { left: 0, top: 0, width: slideSize.width, height: slideSize.height };

  const applyToSelection = (updates: Partial<LayerTransform>) => {
    layers.forEach((layer) => onChange(layer.id, updates));
  };

  const alignLayers = (direction: 'left' | 'center' | 'right' | 'top' | 'middle' | 'bottom') => {
    layers.forEach((layer) => {
      const layerTransform = layer.transform;
      if (direction === 'left') {
        onChange(layer.id, { x: alignBounds.left });
      } else if (direction === 'center') {
        onChange(layer.id, {
          x: alignBounds.left + (alignBounds.width - layerTransform.width) / 2,
        });
      } else if (direction === 'right') {
        onChange(layer.id, {
          x: alignBounds.left + alignBounds.width - layerTransform.width,
        });
      } else if (direction === 'top') {
        onChange(layer.id, { y: alignBounds.top });
      } else if (direction === 'middle') {
        onChange(layer.id, {
          y: alignBounds.top + (alignBounds.height - layerTransform.height) / 2,
        });
      } else if (direction === 'bottom') {
        onChange(layer.id, {
          y: alignBounds.top + alignBounds.height - layerTransform.height,
        });
      }
    });
  };

  const distributeLayers = (axis: 'horizontal' | 'vertical') => {
    if (layers.length < 2) return;
    if (axis === 'horizontal') {
      const sorted = [...layers].sort((a, b) => a.transform.x - b.transform.x);
      const totalWidth = sorted.reduce((sum, layer) => sum + layer.transform.width, 0);
      const gap = (selectionBounds.width - totalWidth) / (sorted.length - 1 || 1);
      let cursor = selectionBounds.left;
      sorted.forEach((layer) => {
        onChange(layer.id, { x: cursor });
        cursor += layer.transform.width + gap;
      });
    } else {
      const sorted = [...layers].sort((a, b) => a.transform.y - b.transform.y);
      const totalHeight = sorted.reduce((sum, layer) => sum + layer.transform.height, 0);
      const gap = (selectionBounds.height - totalHeight) / (sorted.length - 1 || 1);
      let cursor = selectionBounds.top;
      sorted.forEach((layer) => {
        onChange(layer.id, { y: cursor });
        cursor += layer.transform.height + gap;
      });
    }
  };

  const rotateSelection = (delta: number) => {
    layers.forEach((layer) => {
      const next = coerceRotation(layer.transform.rotation + delta);
      onChange(layer.id, { rotation: next });
    });
  };

  const toggleFlip = (axis: 'x' | 'y') => {
    layers.forEach((layer) => {
      if (axis === 'x') {
        onChange(layer.id, { flipX: !(layer.transform.flipX ?? false) });
      } else {
        onChange(layer.id, { flipY: !(layer.transform.flipY ?? false) });
      }
    });
  };

  const [isShiftDown, setIsShiftDown] = useState(false);
  const isDistributeMode = canDistribute && isShiftDown;
  const tooltipDelay = 500;
  const modifierKey =
    typeof navigator !== 'undefined' && navigator.platform.toUpperCase().includes('MAC')
      ? 'Cmd'
      : 'Ctrl';

  const renderShortcut = (keys: string[]) => (
    <KbdGroup className="ml-2">
      {keys.map((key) => (
        <Kbd key={key}>{key}</Kbd>
      ))}
    </KbdGroup>
  );

  useEffect(() => {
    if (typeof window === 'undefined') return;

    const handleKeyEvent = (event: KeyboardEvent) => {
      setIsShiftDown(event.shiftKey);
    };
    const handleBlur = () => setIsShiftDown(false);

    window.addEventListener('keydown', handleKeyEvent);
    window.addEventListener('keyup', handleKeyEvent);
    window.addEventListener('blur', handleBlur);

    return () => {
      window.removeEventListener('keydown', handleKeyEvent);
      window.removeEventListener('keyup', handleKeyEvent);
      window.removeEventListener('blur', handleBlur);
    };
  }, []);

  return (
    <div className="space-y-3">
      <div className="grid grid-cols-2 gap-2">
        <ButtonGroup className="w-full">
          <Tooltip delayDuration={tooltipDelay}>
            <TooltipTrigger asChild>
              <Button
                variant="outline"
                size="icon-sm"
                className="flex-1"
                onClick={() =>
                  isDistributeMode ? distributeLayers('horizontal') : alignLayers('left')
                }
              >
                {isDistributeMode ? (
                  <AlignHorizontalDistributeStart className="h-3.5 w-3.5" />
                ) : (
                  <AlignStartVertical className="h-3.5 w-3.5" />
                )}
              </Button>
            </TooltipTrigger>
            <TooltipContent>
              <div className="flex items-center">
                {isDistributeMode ? 'Distribute Horizontally' : 'Align Left'}
                {!isDistributeMode && renderShortcut(['Shift', modifierKey, '←'])}
              </div>
            </TooltipContent>
          </Tooltip>
          <Tooltip delayDuration={tooltipDelay}>
            <TooltipTrigger asChild>
              <Button
                variant="outline"
                size="icon-sm"
                className="flex-1"
                onClick={() =>
                  isDistributeMode ? distributeLayers('horizontal') : alignLayers('center')
                }
              >
                {isDistributeMode ? (
                  <AlignHorizontalDistributeCenter className="h-3.5 w-3.5" />
                ) : (
                  <AlignCenterVertical className="h-3.5 w-3.5" />
                )}
              </Button>
            </TooltipTrigger>
            <TooltipContent>
              <div className="flex items-center">
                {isDistributeMode ? 'Distribute Horizontally' : 'Align Center'}
                {!isDistributeMode && renderShortcut(['Shift', modifierKey, 'H'])}
              </div>
            </TooltipContent>
          </Tooltip>
          <Tooltip delayDuration={tooltipDelay}>
            <TooltipTrigger asChild>
              <Button
                variant="outline"
                size="icon-sm"
                className="flex-1"
                onClick={() =>
                  isDistributeMode ? distributeLayers('horizontal') : alignLayers('right')
                }
              >
                {isDistributeMode ? (
                  <AlignHorizontalDistributeEnd className="h-3.5 w-3.5" />
                ) : (
                  <AlignEndVertical className="h-3.5 w-3.5" />
                )}
              </Button>
            </TooltipTrigger>
            <TooltipContent>
              <div className="flex items-center">
                {isDistributeMode ? 'Distribute Horizontally' : 'Align Right'}
                {!isDistributeMode && renderShortcut(['Shift', modifierKey, '→'])}
              </div>
            </TooltipContent>
          </Tooltip>
        </ButtonGroup>
        <ButtonGroup className="w-full">
          <Tooltip delayDuration={tooltipDelay}>
            <TooltipTrigger asChild>
              <Button
                variant="outline"
                size="icon-sm"
                className="flex-1"
                onClick={() =>
                  isDistributeMode ? distributeLayers('vertical') : alignLayers('top')
                }
              >
                {isDistributeMode ? (
                  <AlignVerticalDistributeStart className="h-3.5 w-3.5" />
                ) : (
                  <AlignStartHorizontal className="h-3.5 w-3.5" />
                )}
              </Button>
            </TooltipTrigger>
            <TooltipContent>
              <div className="flex items-center">
                {isDistributeMode ? 'Distribute Vertically' : 'Align Top'}
                {!isDistributeMode && renderShortcut(['Shift', modifierKey, '↑'])}
              </div>
            </TooltipContent>
          </Tooltip>
          <Tooltip delayDuration={tooltipDelay}>
            <TooltipTrigger asChild>
              <Button
                variant="outline"
                size="icon-sm"
                className="flex-1"
                onClick={() =>
                  isDistributeMode ? distributeLayers('vertical') : alignLayers('middle')
                }
              >
                {isDistributeMode ? (
                  <AlignVerticalDistributeCenter className="h-3.5 w-3.5" />
                ) : (
                  <AlignCenterHorizontal className="h-3.5 w-3.5" />
                )}
              </Button>
            </TooltipTrigger>
            <TooltipContent>
              <div className="flex items-center">
                {isDistributeMode ? 'Distribute Vertically' : 'Align Middle'}
                {!isDistributeMode && renderShortcut(['Shift', modifierKey, 'M'])}
              </div>
            </TooltipContent>
          </Tooltip>
          <Tooltip delayDuration={tooltipDelay}>
            <TooltipTrigger asChild>
              <Button
                variant="outline"
                size="icon-sm"
                className="flex-1"
                onClick={() =>
                  isDistributeMode ? distributeLayers('vertical') : alignLayers('bottom')
                }
              >
                {isDistributeMode ? (
                  <AlignVerticalDistributeEnd className="h-3.5 w-3.5" />
                ) : (
                  <AlignEndHorizontal className="h-3.5 w-3.5" />
                )}
              </Button>
            </TooltipTrigger>
            <TooltipContent>
              <div className="flex items-center">
                {isDistributeMode ? 'Distribute Vertically' : 'Align Bottom'}
                {!isDistributeMode && renderShortcut(['Shift', modifierKey, '↓'])}
              </div>
            </TooltipContent>
          </Tooltip>
        </ButtonGroup>
      </div>

      <div className="grid grid-cols-2 gap-2">
        <InputGroup className="h-8">
          <InputGroupAddon align="inline-start">X</InputGroupAddon>
          <InputGroupInput
            type="number"
            inputMode="decimal"
            className={cn('h-8', controlBgClass)}
            value={Math.round(transform.x)}
            onValueChange={(next) => applyToSelection({ x: next })}
          />
        </InputGroup>
        <InputGroup className="h-8">
          <InputGroupAddon align="inline-start">Y</InputGroupAddon>
          <InputGroupInput
            type="number"
            inputMode="decimal"
            className={cn('h-8', controlBgClass)}
            value={Math.round(transform.y)}
            onValueChange={(next) => applyToSelection({ y: next })}
          />
        </InputGroup>
      </div>

      <div className="grid grid-cols-[minmax(0,1fr)_minmax(0,1fr)] gap-2">
        <InputGroup className="h-8">
          <InputGroupAddon align="inline-start">
            <RotateCw className="h-3.5 w-3.5" />
          </InputGroupAddon>
          <InputGroupInput
            type="number"
            inputMode="decimal"
            className={cn('h-8', controlBgClass)}
            value={Math.round(transform.rotation)}
            onValueChange={(next) => {
              const coerced = coerceRotation(next);
              applyToSelection({ rotation: coerced });
            }}
          />
          <InputGroupAddon align="inline-end">°</InputGroupAddon>
        </InputGroup>
        <ButtonGroup className="w-full">
          <Tooltip delayDuration={tooltipDelay}>
            <TooltipTrigger asChild>
              <Button
                variant="outline"
                size="icon-sm"
                className="flex-1"
                onClick={() => rotateSelection(90)}
              >
                <RotateCw className="h-3.5 w-3.5" />
              </Button>
            </TooltipTrigger>
            <TooltipContent>
              <div className="flex items-center">Rotate 90 deg</div>
            </TooltipContent>
          </Tooltip>
          <Tooltip delayDuration={tooltipDelay}>
            <TooltipTrigger asChild>
              <Button
                variant="outline"
                size="icon-sm"
                className="flex-1"
                onClick={() => toggleFlip('x')}
              >
                <FlipHorizontal className="h-3.5 w-3.5" />
              </Button>
            </TooltipTrigger>
            <TooltipContent>
              <div className="flex items-center">Flip Horizontal</div>
            </TooltipContent>
          </Tooltip>
          <Tooltip delayDuration={tooltipDelay}>
            <TooltipTrigger asChild>
              <Button
                variant="outline"
                size="icon-sm"
                className="flex-1"
                onClick={() => toggleFlip('y')}
              >
                <FlipVertical className="h-3.5 w-3.5" />
              </Button>
            </TooltipTrigger>
            <TooltipContent>
              <div className="flex items-center">Flip Vertical</div>
            </TooltipContent>
          </Tooltip>
        </ButtonGroup>
      </div>

    </div>
  );
}

// Layer Size Editor
interface LayerSizeEditorProps {
  layers: Layer[];
  onChange: (layerId: string, transform: Partial<LayerTransform>) => void;
  onToggleLock: (layerId: string, lockAspectRatio: boolean) => void;
}

function LayerSizeEditor({ layers, onChange, onToggleLock }: LayerSizeEditorProps) {
  const primaryLayer = layers[0];
  if (!primaryLayer) return null;

  const { transform } = primaryLayer;
  const isAspectLocked = layers.every(
    (layer) => layer.transform.lockAspectRatio ?? false
  );
  const clipStates = layers.map((layer) => layer.transform.clipContent ?? false);
  const isClipAll = clipStates.every(Boolean);
  const isClipMixed = !isClipAll && clipStates.some(Boolean);

  const applyWidth = (nextWidth: number) => {
    layers.forEach((layer) => {
      const ratio = layer.transform.height > 0
        ? layer.transform.width / layer.transform.height
        : 1;
      const nextHeight = (layer.transform.lockAspectRatio ?? false) && ratio > 0
        ? Math.max(1, nextWidth / ratio)
        : layer.transform.height;
      onChange(layer.id, { width: nextWidth, height: nextHeight });
    });
  };

  const applyHeight = (nextHeight: number) => {
    layers.forEach((layer) => {
      const ratio = layer.transform.height > 0
        ? layer.transform.width / layer.transform.height
        : 1;
      const nextWidth = (layer.transform.lockAspectRatio ?? false) && ratio > 0
        ? Math.max(1, nextHeight * ratio)
        : layer.transform.width;
      onChange(layer.id, { width: nextWidth, height: nextHeight });
    });
  };

  const toggleAspectLock = () => {
    layers.forEach((layer) => onToggleLock(layer.id, !isAspectLocked));
  };

  return (
    <div className="space-y-3">
      <div className="grid grid-cols-[minmax(0,1fr)_minmax(0,1fr)_auto] gap-2 items-center">
        <InputGroup className="h-8">
          <InputGroupAddon align="inline-start">W</InputGroupAddon>
          <InputGroupInput
            type="number"
            inputMode="decimal"
            className={cn('h-8', controlBgClass)}
            value={Math.round(transform.width)}
            onValueChange={(next) => applyWidth(Math.max(1, next))}
          />
        </InputGroup>
        <InputGroup className="h-8">
          <InputGroupAddon align="inline-start">H</InputGroupAddon>
          <InputGroupInput
            type="number"
            inputMode="decimal"
            className={cn('h-8', controlBgClass)}
            value={Math.round(transform.height)}
            onValueChange={(next) => applyHeight(Math.max(1, next))}
          />
        </InputGroup>
        <Button
          variant={isAspectLocked ? 'secondary' : 'outline'}
          size="icon-sm"
          onClick={toggleAspectLock}
          aria-pressed={isAspectLocked}
          aria-label={isAspectLocked ? 'Unlock aspect ratio' : 'Lock aspect ratio'}
        >
          {isAspectLocked ? <Lock className="h-3.5 w-3.5" /> : <Unlock className="h-3.5 w-3.5" />}
        </Button>
      </div>
      <div className="flex items-center gap-2 text-xs text-muted-foreground">
        <Checkbox
          checked={isClipMixed ? 'indeterminate' : isClipAll}
          onCheckedChange={(value) => {
            const nextValue = value === 'indeterminate' ? true : value;
            layers.forEach((layer) =>
              onChange(layer.id, { clipContent: Boolean(nextValue) })
            );
          }}
        />
        <span>Clip overflow</span>
      </div>
    </div>
  );
}

// Layer Appearance Editor
interface LayerAppearanceEditorProps {
  layers: Layer[];
  onChange: (layerId: string, transform: Partial<LayerTransform>) => void;
}

function LayerAppearanceEditor({ layers, onChange }: LayerAppearanceEditorProps) {
  const primaryLayer = layers[0];
  if (!primaryLayer) return null;

  const { transform } = primaryLayer;
  const [showCornerDetails, setShowCornerDetails] = useState(false);

  const baseRadius = transform.cornerRadius ?? 0;
  const cornerValues = {
    topLeft: transform.cornerRadiusTopLeft ?? baseRadius,
    topRight: transform.cornerRadiusTopRight ?? baseRadius,
    bottomRight: transform.cornerRadiusBottomRight ?? baseRadius,
    bottomLeft: transform.cornerRadiusBottomLeft ?? baseRadius,
  };
  const radiusValue = Math.round(cornerValues.topLeft);

  const applyOpacity = (nextPercent: number) => {
    const clamped = Math.max(0, Math.min(100, nextPercent));
    layers.forEach((layer) =>
      onChange(layer.id, { opacity: clamped / 100 })
    );
  };

  const applyUniformRadius = (nextValue: number) => {
    const clamped = Math.max(0, nextValue);
    layers.forEach((layer) =>
      onChange(layer.id, {
        cornerRadius: clamped,
        cornerRadiusTopLeft: clamped,
        cornerRadiusTopRight: clamped,
        cornerRadiusBottomRight: clamped,
        cornerRadiusBottomLeft: clamped,
      })
    );
  };

  const applyCornerRadius = (
    corner: 'topLeft' | 'topRight' | 'bottomRight' | 'bottomLeft',
    nextValue: number
  ) => {
    const clamped = Math.max(0, nextValue);
    layers.forEach((layer) => {
      const base = layer.transform.cornerRadius ?? 0;
      const current = {
        topLeft: layer.transform.cornerRadiusTopLeft ?? base,
        topRight: layer.transform.cornerRadiusTopRight ?? base,
        bottomRight: layer.transform.cornerRadiusBottomRight ?? base,
        bottomLeft: layer.transform.cornerRadiusBottomLeft ?? base,
      };
      const updates = { ...current, [corner]: clamped };
      onChange(layer.id, {
        cornerRadius: base,
        cornerRadiusTopLeft: updates.topLeft,
        cornerRadiusTopRight: updates.topRight,
        cornerRadiusBottomRight: updates.bottomRight,
        cornerRadiusBottomLeft: updates.bottomLeft,
      });
    });
  };

  return (
    <div className="space-y-3">
      <div className="grid grid-cols-[minmax(0,1fr)_minmax(0,1fr)_auto] gap-2 items-center">
        <InputGroup className="h-8">
          <InputGroupAddon align="inline-start">
            <Grid3x3 className="h-3.5 w-3.5" />
          </InputGroupAddon>
          <InputGroupInput
            type="number"
            inputMode="decimal"
            className={cn('h-8', controlBgClass)}
            value={Math.round((transform.opacity ?? 1) * 100)}
            onValueChange={applyOpacity}
          />
          <InputGroupAddon align="inline-end">%</InputGroupAddon>
        </InputGroup>
        <InputGroup className="h-8">
          <InputGroupAddon align="inline-start">
            <Maximize className="h-3.5 w-3.5" />
          </InputGroupAddon>
          <InputGroupInput
            type="number"
            inputMode="decimal"
            className={cn('h-8', controlBgClass)}
            value={radiusValue}
            onValueChange={applyUniformRadius}
          />
        </InputGroup>
        <Button
          variant={showCornerDetails ? 'secondary' : 'outline'}
          size="icon-sm"
          onClick={() => setShowCornerDetails((prev) => !prev)}
          aria-pressed={showCornerDetails}
          aria-label="Toggle corner details"
        >
          <Scan className="h-3.5 w-3.5" />
        </Button>
      </div>

      {showCornerDetails && (
        <div className="grid grid-cols-2 gap-2">
          <InputGroup className="h-8">
            <InputGroupAddon align="inline-start">TL</InputGroupAddon>
            <InputGroupInput
              type="number"
              inputMode="decimal"
              className={cn('h-8', controlBgClass)}
              value={Math.round(cornerValues.topLeft)}
              onValueChange={(next) => applyCornerRadius('topLeft', next)}
            />
          </InputGroup>
          <InputGroup className="h-8">
            <InputGroupAddon align="inline-start">TR</InputGroupAddon>
            <InputGroupInput
              type="number"
              inputMode="decimal"
              className={cn('h-8', controlBgClass)}
              value={Math.round(cornerValues.topRight)}
              onValueChange={(next) => applyCornerRadius('topRight', next)}
            />
          </InputGroup>
          <InputGroup className="h-8">
            <InputGroupAddon align="inline-start">BL</InputGroupAddon>
            <InputGroupInput
              type="number"
              inputMode="decimal"
              className={cn('h-8', controlBgClass)}
              value={Math.round(cornerValues.bottomLeft)}
              onValueChange={(next) => applyCornerRadius('bottomLeft', next)}
            />
          </InputGroup>
          <InputGroup className="h-8">
            <InputGroupAddon align="inline-start">BR</InputGroupAddon>
            <InputGroupInput
              type="number"
              inputMode="decimal"
              className={cn('h-8', controlBgClass)}
              value={Math.round(cornerValues.bottomRight)}
              onValueChange={(next) => applyCornerRadius('bottomRight', next)}
            />
          </InputGroup>
        </div>
      )}
    </div>
  );
}

// Layer Style Editor
interface LayerStyleEditorProps {
  layer: Layer;
  onChange: (updates: Partial<Layer>) => void;
}

function LayerStyleEditor({ layer, onChange }: LayerStyleEditorProps) {
  if (layer.type === 'text') {
    return null;
  }
  switch (layer.type) {
    case 'shape':
      return <ShapeStyleEditor layer={layer} onChange={onChange} />;
    case 'media':
      return <MediaStyleEditor layer={layer} onChange={onChange} />;
    case 'web':
      return <WebStyleEditor layer={layer} onChange={onChange} />;
    default:
      return null;
  }
}

// Text Layout Editor
interface TextLayoutEditorProps {
  layer: TextLayer;
  onChange: (updates: Partial<TextLayer>) => void;
}

function TextLayoutEditor({ layer, onChange }: TextLayoutEditorProps) {
  const textMode = layer.textMode || 'custom';
  const padding = layer.padding ?? 2;
  const textModeHint =
    textMode === 'lyrics'
      ? 'Large centered text that shrinks to fit'
      : textMode === 'paragraph'
        ? 'Left-aligned readable text for notes'
        : 'Manually configure all settings';

  // Apply preset mode
  const applyTextMode = (mode: TextLayer['textMode']) => {
    switch (mode) {
      case 'lyrics':
        onChange({
          textMode: mode,
          textFit: 'shrink',
          padding: 5,
          style: {
            ...layer.style,
            alignment: 'center',
            verticalAlignment: 'middle',
            font: {
              ...layer.style?.font,
              family: layer.style?.font?.family || 'Inter',
              size: 72,
              weight: 700,
              lineHeight: 1.3,
            },
          },
        } as Partial<TextLayer>);
        break;
      case 'paragraph':
        onChange({
          textMode: mode,
          textFit: 'auto',
          padding: 5,
          style: {
            ...layer.style,
            alignment: 'left',
            verticalAlignment: 'top',
            font: {
              ...layer.style?.font,
              family: layer.style?.font?.family || 'Inter',
              size: 32,
              weight: 400,
              lineHeight: 1.6,
            },
          },
        } as Partial<TextLayer>);
        break;
      case 'custom':
        onChange({ textMode: mode } as Partial<TextLayer>);
        break;
    }
  };

  return (
    <div className="space-y-3">
      <ControlField label="Display Mode" hint={textModeHint}>
        <Select value={textMode} onValueChange={(v) => applyTextMode(v as TextLayer['textMode'])}>
          <SelectTrigger className={cn('h-8 text-xs w-full', controlBgClass)}>
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="lyrics">Lyrics (Music)</SelectItem>
            <SelectItem value="paragraph">Paragraph (Notes)</SelectItem>
            <SelectItem value="custom">Custom</SelectItem>
          </SelectContent>
        </Select>
      </ControlField>

      <ControlField label="Inner Padding">
        <div className="flex items-center gap-2">
          <ScrubbableNumberInput
            value={padding}
            onValueChange={(next) =>
              onChange({ padding: next, textMode: 'custom' } as Partial<TextLayer>)
            }
            min={0}
            max={20}
            step={1}
          />
          <span className="text-[10px] text-muted-foreground">%</span>
        </div>
      </ControlField>
    </div>
  );
}

// Shape Style Editor
interface ShapeStyleEditorProps {
  layer: ShapeLayer;
  onChange: (updates: Partial<ShapeLayer>) => void;
}

function ShapeStyleEditor({ layer, onChange }: ShapeStyleEditorProps) {
  const { style } = layer;

  if (layer.shapeType !== 'rectangle') {
    return null;
  }

  return (
    <div className="space-y-4">
      <ControlField label="Corner Radius">
        <ScrubbableNumberInput
          value={Math.round(style.cornerRadius)}
          onValueChange={(next) =>
            onChange({ style: { ...style, cornerRadius: next } })
          }
          min={0}
          max={50}
          step={1}
        />
      </ControlField>
    </div>
  );
}

// Media Style Editor
interface MediaStyleEditorProps {
  layer: MediaLayer;
  onChange: (updates: Partial<MediaLayer>) => void;
}

function MediaStyleEditor({ layer, onChange }: MediaStyleEditorProps) {
  return (
    <div className="space-y-4">
      <ControlField label="Fit">
        <Select value={layer.fit} onValueChange={(v) => onChange({ fit: v as any })}>
          <SelectTrigger className={cn('h-8 text-xs w-full', controlBgClass)}>
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="cover">Cover</SelectItem>
            <SelectItem value="contain">Contain</SelectItem>
            <SelectItem value="fill">Fill</SelectItem>
            <SelectItem value="none">None</SelectItem>
          </SelectContent>
        </Select>
      </ControlField>

      {layer.mediaType === 'video' && (
        <>
          <Separator />
          <div className="space-y-2">
            <ControlField label="Loop" inline>
              <Switch
                checked={layer.loop}
                onCheckedChange={(checked) => onChange({ loop: checked })}
              />
            </ControlField>
            <ControlField label="Muted" inline>
              <Switch
                checked={layer.muted}
                onCheckedChange={(checked) => onChange({ muted: checked })}
              />
            </ControlField>
            <ControlField label="Autoplay" inline>
              <Switch
                checked={layer.autoplay}
                onCheckedChange={(checked) => onChange({ autoplay: checked })}
              />
            </ControlField>
          </div>
        </>
      )}
    </div>
  );
}

// Web Style Editor
interface WebStyleEditorProps {
  layer: WebLayer;
  onChange: (updates: Partial<WebLayer>) => void;
}

function WebStyleEditor({ layer, onChange }: WebStyleEditorProps) {
  return (
    <div className="space-y-4">
      <ControlField label="URL">
        <Input
          value={layer.url}
          onChange={(e) => onChange({ url: e.target.value })}
          placeholder="https://..."
          className={cn('w-full h-8 text-xs', controlBgClass)}
        />
      </ControlField>

      <ControlField label="Zoom">
        <div className="flex items-center gap-2">
          <ScrubbableNumberInput
            value={Math.round(layer.zoom * 100)}
            onValueChange={(next) => onChange({ zoom: next / 100 })}
            min={25}
            max={200}
            step={1}
          />
          <span className="text-[10px] text-muted-foreground">%</span>
        </div>
      </ControlField>

      <ControlField label="Interactive" inline>
        <Switch
          checked={layer.interactive}
          onCheckedChange={(checked) => onChange({ interactive: checked })}
        />
      </ControlField>
    </div>
  );
}

// Text Style Editor
interface TextTypographyEditorProps {
  layer: TextLayer;
  onChange: (updates: Partial<TextLayer>) => void;
}

function TextTypographyEditor({ layer, onChange }: TextTypographyEditorProps) {
  const style = layer.style || defaultPrimaryTextStyle;
  const { presentation, ensureFontFamilyBundled } = useEditorStore();
  const { fonts: systemFonts, isLoading: isFontsLoading, refresh: refreshFonts } = useSystemFonts();
  const [fontPickerOpen, setFontPickerOpen] = useState(false);
  const [lineHeightDraft, setLineHeightDraft] = useState('');
  const [isLineHeightFocused, setIsLineHeightFocused] = useState(false);

  const fontFamily = style?.font?.family || 'Inter';
  const fontSize = style?.font?.size || 72;
  const fontWeight = style?.font?.weight || 700;
  const letterSpacing = style?.font?.letterSpacing ?? 0;
  const letterSpacingPercent = Math.round((letterSpacing / fontSize) * 100);
  const lineHeight = style?.font?.lineHeight;
  const hasCustomLineHeight = typeof lineHeight === 'number';
  const computedLineHeight = style?.font?.lineHeight ?? 1.2;
  const lineHeightPlaceholder = String(computedLineHeight);
  const lineHeightValue = isLineHeightFocused
    ? lineHeightDraft
    : hasCustomLineHeight
      ? lineHeightDraft || String(lineHeight)
      : 'auto';
  const textFit = layer.textFit || 'auto';
  const textFitHint =
    textFit === 'auto'
      ? 'Text displays at its natural size'
      : textFit === 'shrink'
        ? 'Text shrinks if it overflows the box'
        : 'Text scales to fill the entire box';
  const availableFonts = useMemo(() => {
    const families = new Set(systemFonts.map((font) => font.family));
    return Array.from(families).sort((a, b) => a.localeCompare(b));
  }, [systemFonts]);

  const fontWeightOptions = useMemo(() => {
    const weights = new Set<number>();
    systemFonts.forEach((font) => {
      if (font.family === fontFamily) {
        weights.add(font.weight);
      }
    });
    const sortedWeights = Array.from(weights).sort((a, b) => a - b);
    if (sortedWeights.length === 0) {
      return [fontWeight];
    }
    return sortedWeights;
  }, [systemFonts, fontFamily, fontWeight]);

  const fontWeightLabels: Record<number, string> = {
    100: 'Thin',
    200: 'Extra Light',
    300: 'Light',
    400: 'Regular',
    500: 'Medium',
    600: 'Semibold',
    700: 'Bold',
    800: 'Extra Bold',
    900: 'Black',
  };
  const bundledFamilies = useMemo(() => {
    if (!presentation?.manifest?.fonts?.length) return new Set<string>();
    return new Set(presentation.manifest.fonts.map((font) => font.family));
  }, [presentation?.manifest?.fonts]);

  const bundleCandidates = useMemo(
    () => systemFonts.filter((font) => font.path),
    [systemFonts]
  );

  const handleFontSelect = (family: string) => {
    onChange({ style: { ...style, font: { ...style?.font!, family } } } as Partial<TextLayer>);
    if (bundleCandidates.length > 0) {
      void ensureFontFamilyBundled(family, bundleCandidates);
    }
    setFontPickerOpen(false);
  };

  useEffect(() => {
    if (fontPickerOpen) {
      void refreshFonts();
    }
  }, [
    fontPickerOpen,
    refreshFonts,
    isFontsLoading,
    systemFonts.length,
    availableFonts.length,
    fontFamily,
  ]);

  useEffect(() => {
    if (isLineHeightFocused) return;
    if (typeof lineHeight === 'number') {
      setLineHeightDraft(String(lineHeight));
    } else {
      setLineHeightDraft('');
    }
  }, [isLineHeightFocused, lineHeight]);

  return (
    <div className="space-y-3">
      <Popover open={fontPickerOpen} onOpenChange={setFontPickerOpen}>
        <PopoverTrigger asChild>
          <Button variant="outline" size="sm" className="w-full justify-between text-xs">
            <span className="flex-1 truncate text-left text-sm leading-none" style={{ fontFamily }}>
              {fontFamily}
            </span>
            <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
          </Button>
        </PopoverTrigger>
        <PopoverContent
          className="w-(--radix-popper-anchor-width) min-w-(--radix-popper-anchor-width) p-0"
          align="start"
        >
          <Command>
            <CommandInput placeholder="Search fonts..." />
            <CommandList>
              {isFontsLoading ? (
                <div className="px-3 py-2 text-xs text-muted-foreground">Loading fonts...</div>
              ) : (
                <>
                  <CommandEmpty>No fonts found.</CommandEmpty>
                  <CommandGroup>
                    {availableFonts.map((family) => (
                      <CommandItem
                        key={family}
                        value={family}
                        onSelect={() => handleFontSelect(family)}
                      >
                        <Check
                          className={cn(
                            'h-4 w-4',
                            fontFamily === family ? 'opacity-100' : 'opacity-0'
                          )}
                        />
                        <span className="flex-1 truncate" style={{ fontFamily: family }}>
                          {family}
                        </span>
                        {bundledFamilies.has(family) && (
                          <Badge variant="secondary" className="text-[10px]">
                            Bundled
                          </Badge>
                        )}
                      </CommandItem>
                    ))}
                  </CommandGroup>
                </>
              )}
            </CommandList>
          </Command>
        </PopoverContent>
      </Popover>

      <div className="grid grid-cols-2 gap-2">
        <Select
          value={String(fontWeight)}
          onValueChange={(v) =>
            onChange({ style: { ...style, font: { ...style?.font!, weight: parseInt(v) } } } as Partial<TextLayer>)
          }
        >
        <SelectTrigger size="sm" className="w-full text-xs">
          <SelectValue />
        </SelectTrigger>
          <SelectContent>
          {fontWeightOptions.map((weight) => (
            <SelectItem key={weight} value={String(weight)}>
              <span className="truncate" style={{ fontFamily, fontWeight: weight }}>
                {fontWeightLabels[weight] ?? `Weight ${weight}`}
              </span>
            </SelectItem>
          ))}
          </SelectContent>
        </Select>
        <InputGroup className="h-8">
          <InputGroupInput
            type="number"
            inputMode="decimal"
            className={cn('h-8', controlBgClass)}
            value={Math.round(fontSize)}
            onValueChange={(next) =>
              onChange({ style: { ...style, font: { ...style?.font!, size: next } } } as Partial<TextLayer>)
            }
          />
          <InputGroupAddon align="inline-end">px</InputGroupAddon>
        </InputGroup>
      </div>

      <div className="grid grid-cols-2 gap-2">
        <InputGroup className="h-8">
          <InputGroupAddon align="inline-start">
            <ArrowUpDown className="h-3.5 w-3.5" />
          </InputGroupAddon>
          <InputGroupInput
            type="text"
            inputMode="decimal"
            className={cn('h-8', controlBgClass)}
            value={lineHeightValue}
            placeholder={isLineHeightFocused ? lineHeightPlaceholder : undefined}
            onFocus={() => {
              setIsLineHeightFocused(true);
              if (!hasCustomLineHeight) {
                setLineHeightDraft('');
              }
            }}
            onBlur={(event) => {
              setIsLineHeightFocused(false);
              const nextValue = Number.parseFloat(event.target.value);
              if (!Number.isFinite(nextValue)) {
                onChange({
                  style: {
                    ...style,
                    font: {
                      ...style?.font!,
                      lineHeight: undefined,
                    },
                  },
                } as Partial<TextLayer>);
                setLineHeightDraft('');
                return;
              }
              onChange({
                style: {
                  ...style,
                  font: {
                    ...style?.font!,
                    lineHeight: nextValue,
                  },
                },
              } as Partial<TextLayer>);
            }}
            onChange={(event) => {
              const nextText = event.target.value;
              setLineHeightDraft(nextText);
              const nextValue = Number.parseFloat(nextText);
              if (!Number.isFinite(nextValue)) return;
              onChange({
                style: {
                  ...style,
                  font: {
                    ...style?.font!,
                    lineHeight: nextValue,
                  },
                },
              } as Partial<TextLayer>);
            }}
          />
        </InputGroup>
        <InputGroup className="h-8">
          <InputGroupAddon align="inline-start">A |</InputGroupAddon>
          <InputGroupInput
            type="number"
            inputMode="decimal"
            className={cn('h-8', controlBgClass)}
            value={letterSpacingPercent}
            onValueChange={(next) => {
              const nextValue = (next / 100) * fontSize;
              onChange({
                style: { ...style, font: { ...style?.font!, letterSpacing: nextValue } },
              } as Partial<TextLayer>);
            }}
          />
          <InputGroupAddon align="inline-end">%</InputGroupAddon>
        </InputGroup>
      </div>

      <div className="grid grid-cols-2 gap-2">
        <ToggleGroup
          type="single"
          value={style?.alignment || 'center'}
          onValueChange={(value) =>
            value && onChange({ style: { ...style, alignment: value as any } } as Partial<TextLayer>)
          }
          variant="outline"
          size="sm"
          className="w-full"
          spacing={0}
        >
          <ToggleGroupItem value="left" className="flex-1 text-xs">
            <AlignStartVertical className="h-3.5 w-3.5" />
          </ToggleGroupItem>
          <ToggleGroupItem value="center" className="flex-1 text-xs">
            <AlignCenterVertical className="h-3.5 w-3.5" />
          </ToggleGroupItem>
          <ToggleGroupItem value="right" className="flex-1 text-xs">
            <AlignEndVertical className="h-3.5 w-3.5" />
          </ToggleGroupItem>
        </ToggleGroup>
        <ToggleGroup
          type="single"
          value={style?.verticalAlignment || 'middle'}
          onValueChange={(value) =>
            value && onChange({ style: { ...style, verticalAlignment: value as any } } as Partial<TextLayer>)
          }
          variant="outline"
          size="sm"
          className="w-full"
          spacing={0}
        >
          <ToggleGroupItem value="top" className="flex-1 text-xs">
            <AlignStartHorizontal className="h-3.5 w-3.5" />
          </ToggleGroupItem>
          <ToggleGroupItem value="middle" className="flex-1 text-xs">
            <AlignCenterHorizontal className="h-3.5 w-3.5" />
          </ToggleGroupItem>
          <ToggleGroupItem value="bottom" className="flex-1 text-xs">
            <AlignEndHorizontal className="h-3.5 w-3.5" />
          </ToggleGroupItem>
        </ToggleGroup>
      </div>

      <ControlField label="Text Fit" hint={textFitHint}>
        <Select
          value={textFit}
          onValueChange={(v) =>
            onChange({ textFit: v as TextLayer['textFit'], textMode: 'custom' } as Partial<TextLayer>)
          }
        >
          <SelectTrigger className={cn('h-8 text-xs w-full px-2', controlBgClass)}>
            <Scan className="mr-2 h-3.5 w-3.5 text-muted-foreground" />
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="auto">Auto (Natural Size)</SelectItem>
            <SelectItem value="shrink">Shrink to Fit</SelectItem>
            <SelectItem value="fill">Fill Container</SelectItem>
          </SelectContent>
        </Select>
      </ControlField>
    </div>
  );
}

interface LayerFillEditorProps {
  layer: Layer;
  onChange: (updates: Partial<Layer>) => void;
}

const resolveLayerFillDefaults = (layer: Layer): LayerFill[] => {
  if (layer.type === 'text') {
    const color = layer.style?.color ?? defaultPrimaryTextStyle.color;
    return [{ id: crypto.randomUUID(), color, opacity: 1, enabled: true }];
  }
  if (layer.type === 'shape') {
    const color = layer.style?.fill ?? '#3b82f6';
    const opacity = layer.style?.fillOpacity ?? 1;
    return [{ id: crypto.randomUUID(), color, opacity, enabled: true }];
  }
  return [];
};

const getPrimaryFill = (fills: LayerFill[]) => {
  const enabled = fills.filter((fill) => fill.enabled !== false);
  return enabled[enabled.length - 1] ?? enabled[0] ?? null;
};

const syncLayerStyleWithFill = (layer: Layer, fills: LayerFill[]): Partial<Layer> => {
  const primary = getPrimaryFill(fills);
  if (!primary) return {};
  if (layer.type === 'text') {
    return {
      style: { ...layer.style, color: primary.color },
    } as Partial<TextLayer>;
  }
  if (layer.type === 'shape') {
    return {
      style: {
        ...layer.style,
        fill: primary.color,
        fillOpacity: primary.opacity,
      },
    } as Partial<ShapeLayer>;
  }
  return {};
};

function LayerFillEditor({ layer, onChange }: LayerFillEditorProps) {
  const hasExplicitFills = layer.fills !== undefined;
  const fallbackFills = useMemo(() => resolveLayerFillDefaults(layer), [layer]);
  const fills = hasExplicitFills ? layer.fills ?? [] : fallbackFills;
  const chipSensors = useChipSensors();

  useEffect(() => {
    if (!hasExplicitFills && fills.length > 0) {
      onChange({ fills, ...syncLayerStyleWithFill(layer, fills) });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [layer.id]);

  if (fills.length === 0) {
    return null;
  }

  const updateFills = (nextFills: LayerFill[]) => {
    onChange({ fills: nextFills, ...syncLayerStyleWithFill(layer, nextFills) });
  };


  const handleUpdateFill = (fillId: string, updates: Partial<LayerFill>) => {
    const nextFills = fills.map((fill) => (fill.id === fillId ? { ...fill, ...updates } : fill));
    updateFills(nextFills);
  };

  const handleRemoveFill = (fillId: string) => {
    const nextFills = fills.filter((fill) => fill.id !== fillId);
    updateFills(nextFills);
  };

  return (
    <DndContext
      sensors={chipSensors}
      collisionDetection={closestCenter}
      onDragEnd={({ active, over }: DragEndEvent) => {
        if (!over || active.id === over.id) return;
        const nextFills = reorderById(fills, String(active.id), String(over.id));
        updateFills(nextFills);
      }}
      modifiers={[restrictToVerticalAxis, restrictToParentElement]}
    >
      <SortableContext items={fills.map((fill) => fill.id)} strategy={verticalListSortingStrategy}>
        <div className="space-y-2">
          {fills.map((fill) => (
            <SortableChipRow
              key={fill.id}
              id={fill.id}
              ariaLabel="Reorder fill"
              className=""
            >
              <ColorFieldGroup
                color={fill.color}
                opacity={fill.opacity}
                onChange={(next) =>
                  handleUpdateFill(fill.id, { color: next.color, opacity: next.opacity })
                }
                colorInputLabel="Fill color value"
                opacityInputLabel="Fill opacity"
              />
              <Button
                variant="ghost"
                size="icon"
                className="h-6 w-6"
                onClick={() =>
                  handleUpdateFill(fill.id, { enabled: fill.enabled === false ? true : false })
                }
                aria-label={fill.enabled === false ? 'Show fill' : 'Hide fill'}
              >
                {fill.enabled === false ? (
                  <EyeOff className="h-3 w-3" />
                ) : (
                  <Eye className="h-3 w-3" />
                )}
              </Button>
              <Button
                variant="ghost"
                size="icon"
                className="h-6 w-6 text-muted-foreground hover:text-destructive"
                onClick={() => handleRemoveFill(fill.id)}
                aria-label="Remove fill"
              >
                <Minus className="h-3.5 w-3.5" />
              </Button>
            </SortableChipRow>
          ))}
        </div>
      </SortableContext>
    </DndContext>
  );
}

interface LayerStrokeEditorProps {
  layer: Layer;
  onChange: (updates: Partial<Layer>) => void;
}

const resolveLayerStrokeDefaults = (layer: Layer): LayerStroke[] => {
  if (layer.type === 'shape') {
    const color = layer.style?.stroke ?? '#1d4ed8';
    const width = layer.style?.strokeWidth ?? 2;
    const opacity = layer.style?.strokeOpacity ?? 1;
    return [
      {
        id: crypto.randomUUID(),
        color,
        opacity,
        width,
        position: 'inside',
        sides: 'all',
        enabled: true,
      },
    ];
  }
  return [];
};

const createDefaultStroke = (layer: Layer, overrides: Partial<LayerStroke> = {}): LayerStroke => {
  const isShape = layer.type === 'shape';
  const defaultColor = isShape
    ? layer.style?.stroke ?? '#1d4ed8'
    : layer.style?.color ?? defaultPrimaryTextStyle.color;
  const defaultOpacity = isShape ? layer.style?.strokeOpacity ?? 1 : 1;
  const defaultWidth = isShape ? layer.style?.strokeWidth ?? 2 : 2;
  const defaultPosition: LayerStroke['position'] = isShape ? 'inside' : 'center';
  return {
    id: crypto.randomUUID(),
    color: overrides.color ?? defaultColor,
    opacity: overrides.opacity ?? defaultOpacity,
    width: overrides.width ?? defaultWidth,
    position: overrides.position ?? defaultPosition,
    sides: overrides.sides ?? 'all',
    customSides: overrides.customSides,
    enabled: overrides.enabled ?? true,
  };
};

const getPrimaryStroke = (strokes: LayerStroke[]) => {
  const enabled = strokes.filter((stroke) => stroke.enabled !== false);
  return enabled[enabled.length - 1] ?? enabled[0] ?? strokes[0] ?? null;
};

const syncLayerStyleWithStroke = (layer: Layer, strokes: LayerStroke[]): Partial<Layer> => {
  const primary = getPrimaryStroke(strokes);
  if (!primary) return {};
  if (layer.type === 'shape') {
    return {
      style: {
        ...layer.style,
        stroke: primary.color,
        strokeWidth: primary.width,
        strokeOpacity: primary.opacity,
      },
    } as Partial<ShapeLayer>;
  }
  return {};
};

function LayerStrokeEditor({ layer, onChange }: LayerStrokeEditorProps) {
  const hasStrokes = layer.strokes && layer.strokes.length > 0;
  const fallbackStrokes = useMemo(() => resolveLayerStrokeDefaults(layer), [layer]);
  const strokes = hasStrokes ? layer.strokes! : fallbackStrokes;
  const chipSensors = useChipSensors();
  const isShape = layer.type === 'shape';

  useEffect(() => {
    if (!hasStrokes && strokes.length > 0) {
      onChange({ strokes, ...syncLayerStyleWithStroke(layer, strokes) });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [layer.id]);

  if (strokes.length === 0) {
    return null;
  }

  const updateStrokes = (nextStrokes: LayerStroke[]) => {
    onChange({ strokes: nextStrokes, ...syncLayerStyleWithStroke(layer, nextStrokes) });
  };

  const handleUpdateStroke = (strokeId: string, updates: Partial<LayerStroke>) => {
    const nextStrokes = strokes.map((stroke) =>
      stroke.id === strokeId
        ? {
            ...stroke,
            ...updates,
            customSides:
              updates.sides === 'custom'
                ? stroke.customSides ?? {
                    top: true,
                    right: true,
                    bottom: true,
                    left: true,
                  }
                : updates.customSides ?? stroke.customSides,
          }
        : stroke
    );
    updateStrokes(nextStrokes);
  };

  const handleRemoveStroke = (strokeId: string) => {
    const nextStrokes = strokes.filter((stroke) => stroke.id !== strokeId);
    if (nextStrokes.length > 0) {
      updateStrokes(nextStrokes);
      return;
    }
    if (layer.type === 'shape') {
      updateStrokes(resolveLayerStrokeDefaults(layer));
    } else {
      onChange({ strokes: [] });
    }
  };

  const formatSideLabel = (stroke: LayerStroke) => {
    const sides = stroke.sides ?? 'all';
    switch (sides) {
      case 'top':
        return 'Top';
      case 'bottom':
        return 'Bottom';
      case 'left':
        return 'Left';
      case 'right':
        return 'Right';
      case 'custom':
        return 'Custom';
      default:
        return 'All';
    }
  };

  const formatPositionLabel = (stroke: LayerStroke) => {
    const position = stroke.position ?? 'inside';
    return position === 'center' ? 'Center' : position === 'outside' ? 'Outside' : 'Inside';
  };

  return (
    <DndContext
      sensors={chipSensors}
      collisionDetection={closestCenter}
      onDragEnd={({ active, over }: DragEndEvent) => {
        if (!over || active.id === over.id) return;
        const nextStrokes = reorderById(strokes, String(active.id), String(over.id));
        updateStrokes(nextStrokes);
      }}
      modifiers={[restrictToVerticalAxis, restrictToParentElement]}
    >
      <SortableContext
        items={strokes.map((stroke) => stroke.id)}
        strategy={verticalListSortingStrategy}
      >
        <div className="space-y-2">
          {strokes.map((stroke) => {
            const strokeColor = toRgba(stroke.color, stroke.opacity);
            const resolvedSides = stroke.sides ?? 'all';
            const resolvedCustomSides =
              resolvedSides === 'custom'
                ? stroke.customSides ?? {
                    top: true,
                    right: true,
                    bottom: true,
                    left: true,
                  }
                : {
                    top: resolvedSides === 'all' || resolvedSides === 'top',
                    right: resolvedSides === 'all' || resolvedSides === 'right',
                    bottom: resolvedSides === 'all' || resolvedSides === 'bottom',
                    left: resolvedSides === 'all' || resolvedSides === 'left',
                  };
            const customSideValues = Object.entries(resolvedCustomSides)
              .filter(([, enabled]) => enabled)
              .map(([side]) => side);
            return (
              <SortableChipRow
                key={stroke.id}
                id={stroke.id}
                ariaLabel="Reorder stroke"
                align="start"
              >
                <div className="flex flex-1 flex-col gap-2">
                  <div className="flex items-center gap-2">
                    <ColorFieldGroup
                      color={stroke.color}
                      opacity={stroke.opacity}
                      onChange={(next) =>
                        handleUpdateStroke(stroke.id, { color: next.color, opacity: next.opacity })
                      }
                      previewStyle={{
                        borderWidth: Math.max(1, Math.round(stroke.width)),
                        borderColor: strokeColor,
                      }}
                      colorInputLabel="Stroke color value"
                      opacityInputLabel="Stroke opacity"
                    />
                    <Button
                      variant="ghost"
                      size="icon"
                      className="h-6 w-6"
                      onClick={() =>
                        handleUpdateStroke(stroke.id, {
                          enabled: stroke.enabled === false ? true : false,
                        })
                      }
                      aria-label={stroke.enabled === false ? 'Show stroke' : 'Hide stroke'}
                    >
                      {stroke.enabled === false ? (
                        <EyeOff className="h-3 w-3" />
                      ) : (
                        <Eye className="h-3 w-3" />
                      )}
                    </Button>
                    <Button
                      variant="ghost"
                      size="icon"
                      className="h-6 w-6 text-muted-foreground hover:text-destructive"
                      onClick={() => handleRemoveStroke(stroke.id)}
                      aria-label="Remove stroke"
                    >
                      <Minus className="h-3.5 w-3.5" />
                    </Button>
                  </div>
                  <div className="flex items-center gap-2">
                    <Select
                      value={stroke.position ?? 'inside'}
                      onValueChange={(value) =>
                        handleUpdateStroke(stroke.id, {
                          position: value as LayerStroke['position'],
                        })
                      }
                      disabled={!isShape}
                    >
                      <SelectTrigger className={cn('w-[110px]', controlBgClass)}>
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="inside">Inside</SelectItem>
                        <SelectItem value="center">Center</SelectItem>
                        <SelectItem value="outside">Outside</SelectItem>
                      </SelectContent>
                    </Select>
                    <InputGroup className="h-8 w-[96px] bg-transparent shadow-none rounded-md border border-border/60">
                      <InputGroupAddon align="inline-start" className="pl-2 pr-1">
                        <AlignJustify className="h-3.5 w-3.5 text-muted-foreground" />
                      </InputGroupAddon>
                      <InputGroupInput
                        type="number"
                        inputMode="decimal"
                        className={strokeChipControlClass}
                        value={Math.round(stroke.width)}
                        onValueChange={(next) => handleUpdateStroke(stroke.id, { width: next })}
                        min={0}
                        max={40}
                        aria-label="Stroke width"
                      />
                    </InputGroup>
                    <div className="ml-auto flex items-center gap-1">
                      <DropdownMenu>
                        <DropdownMenuTrigger asChild>
                          <Button
                            variant="ghost"
                            size="icon-sm"
                            className="h-6 w-6"
                            aria-label="Stroke sides"
                            disabled={!isShape}
                          >
                            <Square className="h-3.5 w-3.5" />
                          </Button>
                        </DropdownMenuTrigger>
                        <DropdownMenuContent align="end" className="w-40">
                          <DropdownMenuRadioGroup
                            value={resolvedSides}
                            onValueChange={(value) => {
                              if (value === 'custom') {
                                handleUpdateStroke(stroke.id, {
                                  sides: 'custom',
                                  customSides: resolvedCustomSides,
                                });
                                return;
                              }
                              handleUpdateStroke(stroke.id, {
                                sides: value as LayerStroke['sides'],
                                customSides: undefined,
                              });
                            }}
                          >
                            <DropdownMenuRadioItem value="all">All</DropdownMenuRadioItem>
                            <DropdownMenuRadioItem value="top">Top</DropdownMenuRadioItem>
                            <DropdownMenuRadioItem value="bottom">Bottom</DropdownMenuRadioItem>
                            <DropdownMenuRadioItem value="left">Left</DropdownMenuRadioItem>
                            <DropdownMenuRadioItem value="right">Right</DropdownMenuRadioItem>
                            <DropdownMenuRadioItem value="custom">Custom</DropdownMenuRadioItem>
                          </DropdownMenuRadioGroup>
                          {resolvedSides === 'custom' && (
                            <>
                              <DropdownMenuSeparator />
                              <div className="px-2 pb-2 pt-1">
                                <ToggleGroup
                                  type="multiple"
                                  value={customSideValues}
                                  onValueChange={(values) =>
                                    handleUpdateStroke(stroke.id, {
                                      customSides: {
                                        top: values.includes('top'),
                                        right: values.includes('right'),
                                        bottom: values.includes('bottom'),
                                        left: values.includes('left'),
                                      },
                                    })
                                  }
                                  variant="outline"
                                  size="sm"
                                  className="w-full"
                                  spacing={0}
                                >
                                  <ToggleGroupItem value="top" className="flex-1 text-xs">
                                    Top
                                  </ToggleGroupItem>
                                  <ToggleGroupItem value="right" className="flex-1 text-xs">
                                    Right
                                  </ToggleGroupItem>
                                  <ToggleGroupItem value="bottom" className="flex-1 text-xs">
                                    Bottom
                                  </ToggleGroupItem>
                                  <ToggleGroupItem value="left" className="flex-1 text-xs">
                                    Left
                                  </ToggleGroupItem>
                                </ToggleGroup>
                              </div>
                            </>
                          )}
                        </DropdownMenuContent>
                      </DropdownMenu>
                    </div>
                  </div>
                </div>
              </SortableChipRow>
            );
          })}
        </div>
      </SortableContext>
    </DndContext>
  );
}

interface ColorPickerPanelProps {
  color: string;
  opacity: number;
  onChange: (next: { color: string; opacity: number }) => void;
}

function ColorPickerPanel({ color, opacity, onChange }: ColorPickerPanelProps) {
  const hexWithAlpha = useMemo(() => toHexWithAlpha(color, opacity), [color, opacity]);
  const [hexDraft, setHexDraft] = useState(hexWithAlpha);
  const addColorToLibrary = useEditorStore((state) => state.addColorToLibrary);

  useEffect(() => {
    setHexDraft(hexWithAlpha);
  }, [hexWithAlpha]);

  return (
    <div className="space-y-3 [&_.react-colorful]:h-[180px] [&_.react-colorful]:w-full [&_.react-colorful]:rounded-xl [&_.react-colorful__saturation]:rounded-t-xl [&_.react-colorful__saturation]:rounded-b-lg [&_.react-colorful__hue]:mt-2.5 [&_.react-colorful__alpha]:mt-2.5 [&_.react-colorful__hue]:h-[14px] [&_.react-colorful__alpha]:h-[14px] [&_.react-colorful__hue]:rounded-full [&_.react-colorful__alpha]:rounded-full [&_.react-colorful__pointer]:h-3.5 [&_.react-colorful__pointer]:w-3.5 [&_.react-colorful__pointer]:rounded-full [&_.react-colorful__pointer]:shadow-[0_0_0_2px_hsl(var(--background))] [&_.react-colorful__hue-pointer]:h-3.5 [&_.react-colorful__hue-pointer]:w-3.5 [&_.react-colorful__hue-pointer]:rounded-full [&_.react-colorful__hue-pointer]:shadow-[0_0_0_2px_hsl(var(--background))] [&_.react-colorful__alpha-pointer]:h-3.5 [&_.react-colorful__alpha-pointer]:w-3.5 [&_.react-colorful__alpha-pointer]:rounded-full [&_.react-colorful__alpha-pointer]:shadow-[0_0_0_2px_hsl(var(--background))]">
      <HexAlphaColorPicker
        color={hexWithAlpha}
        onChange={(next) => {
          const parsed = splitHexAlpha(next);
          onChange(parsed);
        }}
      />
      <div className="flex items-center gap-2">
        <span
          className="h-6 w-6 rounded-full border"
          style={{ backgroundColor: toRgba(color, opacity) }}
        />
        <HexColorInput
          color={hexDraft}
          onChange={(next) => {
            setHexDraft(next);
            const parsed = splitHexAlpha(next);
            onChange(parsed);
          }}
          prefixed
          alpha
          className={cn('h-8 w-full rounded-md border border-border bg-muted/40 px-2 text-xs font-mono')}
        />
        <div className="flex items-center gap-1">
          <Input
            type="number"
            inputMode="decimal"
            className={cn('h-8 w-16 text-xs', controlBgClass)}
            value={Math.round(opacity * 100)}
            onChange={(event) => {
              const next = Number.parseFloat(event.target.value);
              const nextOpacity = Number.isFinite(next)
                ? Math.max(0, Math.min(100, next)) / 100
                : 1;
              onChange({ color, opacity: nextOpacity });
            }}
            onKeyDown={(event) => {
              if (!event.shiftKey) return;
              if (event.key !== 'ArrowUp' && event.key !== 'ArrowDown') return;
              if (event.currentTarget.disabled || event.currentTarget.readOnly) return;
              event.preventDefault();
              if (event.key === 'ArrowUp') {
                event.currentTarget.stepUp(10);
              } else {
                event.currentTarget.stepDown(10);
              }
              event.currentTarget.dispatchEvent(new Event('input', { bubbles: true }));
            }}
          />
          <span className="text-[10px] text-muted-foreground">%</span>
        </div>
      </div>
      <Button
        variant="outline"
        size="sm"
        className="w-full text-xs"
        onClick={() => addColorToLibrary(color)}
      >
        Save to Library
      </Button>
    </div>
  );
}

interface ColorFieldGroupProps {
  color: string;
  opacity: number;
  onChange: (next: { color: string; opacity: number }) => void;
  previewStyle?: React.CSSProperties;
  className?: string;
  colorInputLabel?: string;
  opacityInputLabel?: string;
  popoverContent?: ReactNode;
  popoverClassName?: string;
}

function ColorFieldGroup({
  color,
  opacity,
  onChange,
  previewStyle,
  className,
  colorInputLabel = 'Color value',
  opacityInputLabel = 'Opacity',
  popoverContent,
  popoverClassName,
}: ColorFieldGroupProps) {
  const [hexDraft, setHexDraft] = useState(color);

  useEffect(() => {
    setHexDraft(color);
  }, [color]);

  const handleColorChange = (next: string) => {
    const normalized = next.startsWith('#') ? next : `#${next}`;
    setHexDraft(normalized);
    onChange({ color: normalized, opacity });
  };

  const handleOpacityChange = (value: number) => {
    const nextOpacity = Number.isFinite(value) ? clampPercent(value) / 100 : 1;
    onChange({ color, opacity: nextOpacity });
  };

  return (
    <div className={cn(fieldGroupClass, className)}>
      <Popover>
        <PopoverContent side="left" align="start" className={cn('w-72 p-3', popoverClassName)}>
          {popoverContent ?? (
            <ColorPickerPanel
              color={color}
              opacity={opacity}
              onChange={(next) => onChange({ color: next.color, opacity: next.opacity })}
            />
          )}
        </PopoverContent>
        <InputGroup className="h-8 flex-1 min-w-[120px] bg-transparent shadow-none rounded-none rounded-l-md border-r-0 border-border/60">
          <InputGroupAddon align="inline-start" className="pl-2 pr-1">
            <PopoverTrigger asChild>
              <button
                type="button"
                className="flex items-center pl-1.5"
                aria-label={colorInputLabel}
              >
                <span
                  className="h-4 w-4 rounded-sm border border-border/70"
                  style={previewStyle ?? { backgroundColor: toRgba(color, opacity) }}
                />
              </button>
            </PopoverTrigger>
          </InputGroupAddon>
          <InputGroupInput
            type="text"
            value={hexDraft}
            onChange={(event) => handleColorChange(event.target.value)}
            className="h-8 text-sm font-mono"
            aria-label={colorInputLabel}
          />
        </InputGroup>
      </Popover>
      <InputGroup className="h-8 w-[84px] bg-transparent shadow-none rounded-none rounded-r-md border-l border-border/60">
        <InputGroupInput
          type="number"
          inputMode="decimal"
          className="h-8 text-sm"
          value={Math.round(opacity * 100)}
          onValueChange={handleOpacityChange}
          aria-label={opacityInputLabel}
        />
        <InputGroupAddon align="inline-end" className="text-sm">
          %
        </InputGroupAddon>
      </InputGroup>
    </div>
  );
}

// Transition Editor
interface TransitionEditorProps {
  transition: { type: SlideTransitionType; duration: number; easing?: string };
  onChange: (transition: Partial<{ type: SlideTransitionType; duration: number }>) => void;
}

function TransitionEditor({ transition, onChange }: TransitionEditorProps) {
  const transitionTypes: { value: SlideTransitionType; label: string }[] = [
    { value: 'none', label: 'None' },
    { value: 'fade', label: 'Fade' },
    { value: 'slide-left', label: 'Slide Left' },
    { value: 'slide-right', label: 'Slide Right' },
    { value: 'slide-up', label: 'Slide Up' },
    { value: 'slide-down', label: 'Slide Down' },
    { value: 'zoom-in', label: 'Zoom In' },
    { value: 'zoom-out', label: 'Zoom Out' },
    { value: 'flip', label: 'Flip' },
  ];

  return (
    <div className="space-y-3">
      <ControlField label="Type">
        <Select
          value={transition.type}
          onValueChange={(v) => onChange({ type: v as SlideTransitionType })}
        >
          <SelectTrigger className={cn('h-8 text-xs w-full', controlBgClass)}>
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {transitionTypes.map((t) => (
              <SelectItem key={t.value} value={t.value}>
                {t.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </ControlField>

      {transition.type !== 'none' && (
        <ControlField label="Duration">
          <InputGroup>
              <InputGroupInput
                type="number"
                inputMode="decimal"
                className={cn(controlBgClass)}
                value={transition.duration}
                onValueChange={(next) => onChange({ duration: Math.round(next) })}
              />
            <InputGroupAddon align="inline-end">ms</InputGroupAddon>
          </InputGroup>
        </ControlField>
      )}
    </div>
  );
}

// Media Cues Editor
interface MediaCuesEditorProps {
  slide: Slide;
  mediaEntries: MediaEntry[];
  onChange: (cues: SlideMediaCue[]) => void;
  showAdd: boolean;
}

function MediaCuesEditor({ slide, mediaEntries, onChange, showAdd }: MediaCuesEditorProps) {
  const [selectedMediaId, setSelectedMediaId] = useState('');
  const [selectedTarget, setSelectedTarget] = useState<MediaCueTarget>('mediaUnderlay');
  const cues = slide.mediaCues ?? [];

  const selectedEntry = mediaEntries.find((entry) => entry.id === selectedMediaId) || null;
  const isAudioSelection = selectedEntry?.type === 'audio';
  const effectiveTarget = isAudioSelection ? 'audio' : selectedTarget;

  const handleAddCue = () => {
    if (!selectedEntry) return;
    const nextCue: SlideMediaCue = {
      id: crypto.randomUUID(),
      mediaId: selectedEntry.id,
      mediaType: selectedEntry.type,
      target: effectiveTarget,
      fit: selectedEntry.type === 'audio' ? undefined : 'cover',
      loop: selectedEntry.type === 'video',
      muted: selectedEntry.type === 'video',
      autoplay: selectedEntry.type !== 'image',
    };
    onChange([...cues, nextCue]);
    setSelectedMediaId('');
    setSelectedTarget('mediaUnderlay');
  };

  const handleUpdateCue = (cueId: string, updates: Partial<SlideMediaCue>) => {
    onChange(cues.map((cue) => (cue.id === cueId ? { ...cue, ...updates } : cue)));
  };

  const handleRemoveCue = (cueId: string) => {
    onChange(cues.filter((cue) => cue.id !== cueId));
  };

  const targetOptions: { value: MediaCueTarget; label: string }[] = [
    { value: 'mediaUnderlay', label: 'Behind Background (Underlay)' },
    { value: 'mediaOverlay', label: 'Behind Elements (Overlay)' },
    { value: 'audio', label: 'Audio' },
  ];

  return (
    <div className="space-y-3">
      {cues.length > 0 && (
        <div className="space-y-2">
          {cues.map((cue) => {
            const entry = mediaEntries.find((media) => media.id === cue.mediaId);
            const cueLabel = entry?.filename ?? 'Unknown media';
            const icon =
              cue.mediaType === 'audio'
                ? <Music className="h-3 w-3" />
                : cue.mediaType === 'video'
                  ? <Video className="h-3 w-3" />
                  : <Image className="h-3 w-3" />;

            return (
              <div key={cue.id} className="rounded-md border border-border/60 p-2 space-y-2">
                <div className="flex items-center gap-2">
                  <span className="text-muted-foreground">{icon}</span>
                  <div className="flex-1 min-w-0">
                    <div className="text-xs font-medium truncate">{cueLabel}</div>
                    <div className="text-[10px] text-muted-foreground capitalize">
                      {cue.mediaType}
                    </div>
                  </div>
                  <Button
                    variant="ghost"
                    size="icon"
                    className="h-6 w-6 text-destructive"
                    onClick={() => handleRemoveCue(cue.id)}
                  >
                    <Trash2 className="h-3 w-3" />
                  </Button>
                </div>

                <div className="grid grid-cols-2 gap-2">
                  <ControlField label="Target">
                    <Select
                      value={cue.target}
                      onValueChange={(value) =>
                        handleUpdateCue(cue.id, { target: value as MediaCueTarget })
                      }
                      disabled={cue.mediaType === 'audio'}
                    >
                    <SelectTrigger className={cn('h-7 text-xs w-full', controlBgClass)}>
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        {targetOptions
                          .filter((option) => cue.mediaType !== 'audio' || option.value === 'audio')
                          .map((option) => (
                            <SelectItem key={option.value} value={option.value}>
                              {option.label}
                            </SelectItem>
                          ))}
                      </SelectContent>
                    </Select>
                  </ControlField>
                </div>
              </div>
            );
          })}
        </div>
      )}

      {showAdd && (
        <>
          <Separator />
          <div className="grid grid-cols-2 gap-2">
            <ControlField label="Media">
              <Select value={selectedMediaId} onValueChange={setSelectedMediaId}>
                <SelectTrigger className={cn('h-8 text-xs w-full', controlBgClass)}>
                  <SelectValue placeholder="Select media..." />
                </SelectTrigger>
                <SelectContent>
                  {mediaEntries.length === 0 ? (
                    <SelectItem value="__none" disabled>
                      No media available
                    </SelectItem>
                  ) : (
                    mediaEntries.map((entry) => (
                      <SelectItem key={entry.id} value={entry.id}>
                        {entry.filename}
                      </SelectItem>
                    ))
                  )}
                </SelectContent>
              </Select>
            </ControlField>

            {!isAudioSelection && (
              <ControlField label="Target">
                <Select
                  value={effectiveTarget}
                  onValueChange={(value) => setSelectedTarget(value as MediaCueTarget)}
                >
                  <SelectTrigger className={cn('h-8 text-xs w-full', controlBgClass)}>
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {targetOptions
                      .filter((option) => option.value !== 'audio')
                      .map((option) => (
                        <SelectItem key={option.value} value={option.value}>
                          {option.label}
                        </SelectItem>
                      ))}
                  </SelectContent>
                </Select>
              </ControlField>
            )}
          </div>

          <Button
            size="sm"
            className="w-full"
            onClick={handleAddCue}
            disabled={!selectedMediaId || !selectedEntry}
          >
            Add Cue
          </Button>
        </>
      )}
    </div>
  );
}

// Build Animation Editor
interface BuildAnimationEditorProps {
  slide: Slide;
  onAddStep: (step: Omit<import('@/lib/models').BuildStep, 'id'>) => void;
}

function BuildAnimationEditor({ slide, onAddStep }: BuildAnimationEditorProps) {
  const [selectedLayerId, setSelectedLayerId] = useState<string>('');
  const [preset, setPreset] = useState<AnimationPreset>('fade-in');
  const [trigger, setTrigger] = useState<BuildTrigger>('onAdvance');
  const [duration, setDuration] = useState(300);

  const presets: { value: AnimationPreset; label: string }[] = [
    { value: 'fade-in', label: 'Fade In' },
    { value: 'fade-out', label: 'Fade Out' },
    { value: 'slide-in-left', label: 'Slide In Left' },
    { value: 'slide-in-right', label: 'Slide In Right' },
    { value: 'slide-in-up', label: 'Slide In Up' },
    { value: 'slide-in-down', label: 'Slide In Down' },
    { value: 'scale-in', label: 'Scale In' },
    { value: 'scale-out', label: 'Scale Out' },
    { value: 'bounce-in', label: 'Bounce In' },
    { value: 'spin-in', label: 'Spin In' },
  ];

  const triggers: { value: BuildTrigger; label: string }[] = [
    { value: 'onEnter', label: 'On Enter' },
    { value: 'onAdvance', label: 'On Click/Advance' },
    { value: 'withPrevious', label: 'With Previous' },
    { value: 'afterPrevious', label: 'After Previous' },
    { value: 'onExit', label: 'On Exit' },
  ];

  const handleAdd = () => {
    if (!selectedLayerId) return;
    onAddStep({
      layerId: selectedLayerId,
      preset,
      trigger,
      delay: 0,
      duration,
    });
  };

  return (
    <div className="space-y-3">
      <div className="grid grid-cols-2 gap-2">
        <ControlField label="Layer">
          <Select value={selectedLayerId} onValueChange={setSelectedLayerId}>
            <SelectTrigger className={cn('h-8 text-xs w-full', controlBgClass)}>
              <SelectValue placeholder="Select layer" />
            </SelectTrigger>
            <SelectContent>
              {slide.layers.map((layer) => (
                <SelectItem key={layer.id} value={layer.id}>
                  {layer.name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </ControlField>

        <ControlField label="Trigger">
          <Select value={trigger} onValueChange={(v) => setTrigger(v as BuildTrigger)}>
            <SelectTrigger className={cn('h-8 text-xs w-full', controlBgClass)}>
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {triggers.map((t) => (
                <SelectItem key={t.value} value={t.value}>
                  {t.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </ControlField>
      </div>

      <ControlField label="Animation">
        <Select value={preset} onValueChange={(v) => setPreset(v as AnimationPreset)}>
          <SelectTrigger className={cn('h-8 text-xs w-full', controlBgClass)}>
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {presets.map((p) => (
              <SelectItem key={p.value} value={p.value}>
                {p.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </ControlField>

      <ControlField label="Duration">
        <InputGroup>
          <InputGroupInput
            type="number"
            inputMode="decimal"
            className={cn(controlBgClass)}
            value={duration}
            onValueChange={(next) => setDuration(Math.round(next))}
          />
          <InputGroupAddon align="inline-end">ms</InputGroupAddon>
        </InputGroup>
      </ControlField>

      <Button
        className="w-full"
        onClick={handleAdd}
        disabled={!selectedLayerId}
      >
        Add Build Step
      </Button>
    </div>
  );
}

// Background Editor
interface SlideBackgroundEditorProps {
  slide: Slide;
  mediaEntries: MediaEntry[];
  presentation: Presentation | null;
  presentationPath: string | null;
  pendingMedia?: Map<string, string>;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onSetBackground: (background: Background) => void;
}

const normalizeGradientStops = (stops: GradientStop[], fallback: GradientStop[]) => {
  const sanitized = stops
    .filter((stop) => Boolean(stop?.color))
    .map((stop) => ({
      color: stop.color,
      position: clampPercent(stop.position),
    }));
  const nextStops = sanitized.length > 0 ? sanitized : fallback;
  if (nextStops.length === 1) {
    return [
      nextStops[0],
      { color: nextStops[0].color, position: 100 },
    ];
  }
  return [...nextStops].sort((a, b) => a.position - b.position);
};

function SlideBackgroundEditor({
  slide,
  mediaEntries,
  presentation,
  presentationPath,
  pendingMedia,
  open,
  onOpenChange,
  onSetBackground,
}: SlideBackgroundEditorProps) {
  const effectiveBackground = useMemo(() => resolveSlideBackground(slide), [slide]);
  const isTransparent = effectiveBackground.type === 'transparent';
  const resolvedBackgroundSrc = useResolvedBackgroundMediaSrc({
    background: effectiveBackground,
    presentation,
    presentationPath,
    pendingMedia,
  });
  const [activeTab, setActiveTab] = useState<string>(
    effectiveBackground.type === 'transparent' ? 'solid' : effectiveBackground.type
  );

  useEffect(() => {
    const nextTab =
      effectiveBackground.type === 'transparent' ? 'solid' : effectiveBackground.type;
    setActiveTab(nextTab);
  }, [effectiveBackground.type]);

  const baseGradientStops = useMemo<GradientStop[]>(() => {
    if (effectiveBackground.type === 'solid') {
      return [
        { color: effectiveBackground.color, position: 0 },
        { color: effectiveBackground.color, position: 100 },
      ];
    }
    return [
      { color: '#111827', position: 0 },
      { color: '#ffffff', position: 100 },
    ];
  }, [effectiveBackground]);

  const solidPicker = useMemo(() => {
    const sourceColor =
      effectiveBackground.type === 'solid'
        ? effectiveBackground.color
        : baseGradientStops[0]?.color ?? '#1a1a2e';
    return splitHexAlpha(sourceColor);
  }, [effectiveBackground, baseGradientStops]);
  const [solidHexDraft, setSolidHexDraft] = useState(solidPicker.color);

  useEffect(() => {
    setSolidHexDraft(solidPicker.color);
  }, [solidPicker.color]);

  const gradient: GradientBackground =
    effectiveBackground.type === 'gradient'
      ? {
          ...effectiveBackground,
          stops: normalizeGradientStops(effectiveBackground.stops, baseGradientStops),
        }
      : {
          type: 'gradient',
          angle: 90,
          stops: normalizeGradientStops([], baseGradientStops),
        };

  const imageBackground: ImageBackground =
    effectiveBackground.type === 'image'
      ? effectiveBackground
      : {
          type: 'image',
          mediaId: '',
          fit: 'cover',
          position: { x: 50, y: 50 },
          opacity: 1,
        };

  const videoBackground: VideoBackground =
    effectiveBackground.type === 'video'
      ? effectiveBackground
      : {
          type: 'video',
          mediaId: '',
          fit: 'cover',
          loop: true,
          muted: true,
          opacity: 1,
        };

  const chipPreviewStyle = useMemo<React.CSSProperties>(() => {
    if (effectiveBackground.type === 'solid') {
      return { backgroundColor: effectiveBackground.color };
    }
    if (effectiveBackground.type === 'gradient') {
      return getBackgroundStyle(effectiveBackground);
    }
    if (effectiveBackground.type === 'image') {
      const src = resolvedBackgroundSrc ?? '';
      const position = effectiveBackground.position ?? { x: 50, y: 50 };
      return {
        backgroundImage: src ? `url(${src})` : undefined,
        backgroundSize: 'cover',
        backgroundPosition: `${position.x}% ${position.y}%`,
      };
    }
    if (effectiveBackground.type === 'video') {
      return {
        backgroundImage:
          'linear-gradient(135deg, rgba(15, 23, 42, 0.9), rgba(15, 23, 42, 0.4))',
      };
    }
    return {};
  }, [effectiveBackground, resolvedBackgroundSrc]);

  const chipLabel = useMemo(() => {
    switch (effectiveBackground.type) {
      case 'solid':
        return effectiveBackground.color.replace('#', '').toUpperCase();
      case 'gradient':
        return 'Gradient';
      case 'image': {
        const entry = mediaEntries.find((media) => media.id === effectiveBackground.mediaId);
        return entry?.filename ?? 'Image';
      }
      case 'video': {
        const entry = mediaEntries.find((media) => media.id === effectiveBackground.mediaId);
        return entry?.filename ?? 'Video';
      }
      default:
        return 'Background';
    }
  }, [effectiveBackground, mediaEntries]);

  const chipMeta = useMemo(() => {
    if (effectiveBackground.type === 'gradient') {
      return `${effectiveBackground.stops.length} stops`;
    }
    if (effectiveBackground.type === 'image') {
      return `Fit: ${effectiveBackground.fit}`;
    }
    if (effectiveBackground.type === 'video') {
      return `Fit: ${effectiveBackground.fit}`;
    }
    return '';
  }, [effectiveBackground]);

  const updateGradient = (updates: Partial<GradientBackground>) => {
    const nextStops = normalizeGradientStops(
      updates.stops ?? gradient.stops,
      baseGradientStops
    );
    onSetBackground({
      ...gradient,
      ...updates,
      type: 'gradient',
      stops: nextStops,
    });
  };

  const updateImage = (updates: Partial<ImageBackground>) => {
    const next = {
      ...imageBackground,
      ...updates,
      position: {
        x: clampPercent(updates.position?.x ?? imageBackground.position.x),
        y: clampPercent(updates.position?.y ?? imageBackground.position.y),
      },
      opacity: Math.max(0, Math.min(1, updates.opacity ?? imageBackground.opacity)),
    };
    if (!next.mediaId) return;
    onSetBackground(next);
  };

  const updateVideo = (updates: Partial<VideoBackground>) => {
    const next = {
      ...videoBackground,
      ...updates,
      opacity: Math.max(0, Math.min(1, updates.opacity ?? videoBackground.opacity)),
    };
    if (!next.mediaId) return;
    onSetBackground(next);
  };

  const imageEntries = useMemo(
    () => mediaEntries.filter((entry) => entry.type === 'image'),
    [mediaEntries]
  );
  const videoEntries = useMemo(
    () => mediaEntries.filter((entry) => entry.type === 'video'),
    [mediaEntries]
  );

  return (
    <div className="space-y-2">
      <Popover open={open} onOpenChange={onOpenChange}>
        {!isTransparent && (
          <div className="flex items-center gap-2">
            {effectiveBackground.type === 'solid' ? (
              <div className={fieldGroupClass}>
                <InputGroup className="h-7 flex-1 min-w-[140px] bg-transparent shadow-none rounded-none rounded-l-md border-r-0 border-border/60">
                  <InputGroupAddon align="inline-start" className="pl-2 pr-1">
                    <PopoverTrigger asChild>
                      <button
                        type="button"
                        className="flex items-center pl-1.5"
                        aria-label="Edit background"
                      >
                        <span
                          className="h-4 w-4 rounded-sm border border-border/70"
                          style={chipPreviewStyle}
                        />
                      </button>
                    </PopoverTrigger>
                  </InputGroupAddon>
                  <InputGroupInput
                    type="text"
                    value={solidHexDraft}
                    onChange={(event) => {
                      const normalized = event.target.value.startsWith('#')
                        ? event.target.value
                        : `#${event.target.value}`;
                      setSolidHexDraft(normalized);
                      onSetBackground({
                        type: 'solid',
                        color: toHexWithAlpha(normalized, solidPicker.opacity),
                      });
                    }}
                    className="text-xs font-mono"
                    aria-label="Background color value"
                  />
                </InputGroup>
                <InputGroup className="h-7 w-[84px] bg-transparent shadow-none rounded-none rounded-r-md border-l border-border/60">
                  <InputGroupInput
                    type="number"
                    inputMode="decimal"
                    className="text-xs"
                    value={Math.round(solidPicker.opacity * 100)}
                    onValueChange={(value) => {
                      const nextOpacity = Number.isFinite(value) ? clampPercent(value) / 100 : 1;
                      onSetBackground({
                        type: 'solid',
                        color: toHexWithAlpha(solidPicker.color, nextOpacity),
                      });
                    }}
                    aria-label="Background opacity"
                  />
                  <InputGroupAddon align="inline-end">%</InputGroupAddon>
                </InputGroup>
              </div>
            ) : (
              <div className={fieldGroupClass}>
                <PopoverTrigger asChild>
                  <button type="button" className="flex flex-1 items-center gap-2 text-left">
                    <span
                      className="h-5 w-5 rounded-sm border border-border/70"
                      style={chipPreviewStyle}
                    />
                    <div className="flex-1 min-w-0">
                      <div className="text-xs font-medium truncate">{chipLabel}</div>
                      {chipMeta && (
                        <div className="text-[10px] text-muted-foreground">{chipMeta}</div>
                      )}
                    </div>
                    <span className="text-muted-foreground">
                      {effectiveBackground.type === 'gradient' && (
                        <Grid3x3 className="h-3.5 w-3.5" />
                      )}
                      {effectiveBackground.type === 'image' && <Image className="h-3.5 w-3.5" />}
                      {effectiveBackground.type === 'video' && <Video className="h-3.5 w-3.5" />}
                    </span>
                  </button>
                </PopoverTrigger>
              </div>
            )}
            <Button
              variant="ghost"
              size="icon-sm"
              className="h-6 w-6"
              onClick={() => {
                onSetBackground({ type: 'transparent' });
                onOpenChange(false);
              }}
              aria-label="Remove background"
            >
              <Minus className="h-3.5 w-3.5" />
            </Button>
          </div>
        )}
        <PopoverContent side="left" align="start" className="w-80 p-3">
          <Tabs value={activeTab} onValueChange={setActiveTab}>
            <TabsList className="grid grid-cols-4 w-full">
              <TabsTrigger value="solid" aria-label="Solid">
                <Palette className="h-4 w-4" />
              </TabsTrigger>
              <TabsTrigger value="gradient" aria-label="Gradient">
                <Grid3x3 className="h-4 w-4" />
              </TabsTrigger>
              <TabsTrigger value="image" aria-label="Image">
                <Image className="h-4 w-4" />
              </TabsTrigger>
              <TabsTrigger value="video" aria-label="Video">
                <Video className="h-4 w-4" />
              </TabsTrigger>
            </TabsList>

            <TabsContent value="solid" className="mt-3">
              <ColorPickerPanel
                color={solidPicker.color}
                opacity={solidPicker.opacity}
                onChange={(next) =>
                  onSetBackground({
                    type: 'solid',
                    color: toHexWithAlpha(next.color, next.opacity),
                  })
                }
              />
            </TabsContent>

            <TabsContent value="gradient" className="mt-3 space-y-3">
              <ControlField label="Angle">
                <ScrubbableNumberInput
                  value={gradient.angle}
                  min={0}
                  max={360}
                  step={1}
                  onValueChange={(value) => updateGradient({ angle: value })}
                />
              </ControlField>

              <div className="space-y-2">
                {gradient.stops.map((stop, index) => {
                  const parsedStop = splitHexAlpha(stop.color);
                  return (
                    <div key={`${stop.color}-${index}`} className="flex items-center gap-2">
                      <ColorFieldGroup
                        color={parsedStop.color}
                        opacity={parsedStop.opacity}
                        onChange={(next) => {
                          const nextStops = gradient.stops.map((entry, stopIndex) =>
                            stopIndex === index
                              ? {
                                  ...entry,
                                  color: toHexWithAlpha(next.color, next.opacity),
                                }
                              : entry
                          );
                          updateGradient({ stops: nextStops });
                        }}
                        colorInputLabel="Gradient stop color value"
                        opacityInputLabel="Gradient stop opacity"
                      />
                      <InputGroup className="w-[86px]">
                        <InputGroupInput
                          type="number"
                          inputMode="decimal"
                          className={cn('h-7', controlBgClass)}
                          value={Math.round(stop.position)}
                          onValueChange={(value) => {
                            const nextStops = gradient.stops.map((entry, stopIndex) =>
                              stopIndex === index
                                ? { ...entry, position: clampPercent(value) }
                                : entry
                            );
                            updateGradient({ stops: nextStops });
                          }}
                          aria-label="Gradient stop position"
                        />
                        <InputGroupAddon align="inline-end">%</InputGroupAddon>
                      </InputGroup>
                      <Button
                        variant="ghost"
                        size="icon"
                        className="h-6 w-6 text-muted-foreground hover:text-destructive"
                        onClick={() => {
                          if (gradient.stops.length <= 2) return;
                          updateGradient({
                            stops: gradient.stops.filter((_, stopIndex) => stopIndex !== index),
                          });
                        }}
                        disabled={gradient.stops.length <= 2}
                        aria-label="Remove stop"
                      >
                        <Minus className="h-3.5 w-3.5" />
                      </Button>
                    </div>
                  );
                })}
                <Button
                  size="sm"
                  variant="outline"
                  className="w-full"
                  onClick={() => {
                    const lastColor = gradient.stops[gradient.stops.length - 1]?.color ?? '#ffffff';
                    updateGradient({
                      stops: [
                        ...gradient.stops,
                        { color: lastColor, position: 50 },
                      ],
                    });
                  }}
                >
                  <Plus className="mr-2 h-3.5 w-3.5" />
                  Add Stop
                </Button>
              </div>
            </TabsContent>

            <TabsContent value="image" className="mt-3 space-y-3">
              <ControlField label="Image">
                <Select
                  value={imageBackground.mediaId || ''}
                  onValueChange={(value) =>
                    onSetBackground({
                      ...imageBackground,
                      mediaId: value,
                    })
                  }
                >
                  <SelectTrigger className={cn('h-8 text-xs w-full', controlBgClass)}>
                    <SelectValue placeholder="Select image..." />
                  </SelectTrigger>
                  <SelectContent>
                    {imageEntries.length === 0 ? (
                      <SelectItem value="__none" disabled>
                        No images available
                      </SelectItem>
                    ) : (
                      imageEntries.map((entry) => (
                        <SelectItem key={entry.id} value={entry.id}>
                          {entry.filename}
                        </SelectItem>
                      ))
                    )}
                  </SelectContent>
                </Select>
              </ControlField>

              <div className="grid grid-cols-2 gap-2">
                <ControlField label="Fit">
                  <Select
                    value={imageBackground.fit}
                    onValueChange={(value) =>
                      updateImage({ fit: value as ImageBackground['fit'] })
                    }
                  >
                    <SelectTrigger className={cn('h-8 text-xs w-full', controlBgClass)}>
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="cover">Cover</SelectItem>
                      <SelectItem value="contain">Contain</SelectItem>
                      <SelectItem value="fill">Fill</SelectItem>
                      <SelectItem value="none">None</SelectItem>
                    </SelectContent>
                  </Select>
                </ControlField>
                <ControlField label="Opacity">
                  <InputGroup>
                    <InputGroupInput
                      type="number"
                      inputMode="decimal"
                      className={cn('h-8', controlBgClass)}
                      value={Math.round((imageBackground.opacity ?? 1) * 100)}
                      onValueChange={(value) =>
                        updateImage({ opacity: clampPercent(value) / 100 })
                      }
                    />
                    <InputGroupAddon align="inline-end">%</InputGroupAddon>
                  </InputGroup>
                </ControlField>
              </div>

              <div className="grid grid-cols-2 gap-2">
                <ControlField label="Position X">
                  <InputGroup>
                    <InputGroupInput
                      type="number"
                      inputMode="decimal"
                      className={cn('h-8', controlBgClass)}
                      value={Math.round(imageBackground.position.x)}
                      onValueChange={(value) =>
                        updateImage({
                          position: { ...imageBackground.position, x: clampPercent(value) },
                        })
                      }
                    />
                    <InputGroupAddon align="inline-end">%</InputGroupAddon>
                  </InputGroup>
                </ControlField>
                <ControlField label="Position Y">
                  <InputGroup>
                    <InputGroupInput
                      type="number"
                      inputMode="decimal"
                      className={cn('h-8', controlBgClass)}
                      value={Math.round(imageBackground.position.y)}
                      onValueChange={(value) =>
                        updateImage({
                          position: { ...imageBackground.position, y: clampPercent(value) },
                        })
                      }
                    />
                    <InputGroupAddon align="inline-end">%</InputGroupAddon>
                  </InputGroup>
                </ControlField>
              </div>
            </TabsContent>

            <TabsContent value="video" className="mt-3 space-y-3">
              <ControlField label="Video">
                <Select
                  value={videoBackground.mediaId || ''}
                  onValueChange={(value) =>
                    onSetBackground({
                      ...videoBackground,
                      mediaId: value,
                    })
                  }
                >
                  <SelectTrigger className={cn('h-8 text-xs w-full', controlBgClass)}>
                    <SelectValue placeholder="Select video..." />
                  </SelectTrigger>
                  <SelectContent>
                    {videoEntries.length === 0 ? (
                      <SelectItem value="__none" disabled>
                        No videos available
                      </SelectItem>
                    ) : (
                      videoEntries.map((entry) => (
                        <SelectItem key={entry.id} value={entry.id}>
                          {entry.filename}
                        </SelectItem>
                      ))
                    )}
                  </SelectContent>
                </Select>
              </ControlField>

              <div className="grid grid-cols-2 gap-2">
                <ControlField label="Fit">
                  <Select
                    value={videoBackground.fit}
                    onValueChange={(value) =>
                      updateVideo({ fit: value as VideoBackground['fit'] })
                    }
                  >
                    <SelectTrigger className={cn('h-8 text-xs w-full', controlBgClass)}>
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="cover">Cover</SelectItem>
                      <SelectItem value="contain">Contain</SelectItem>
                      <SelectItem value="fill">Fill</SelectItem>
                    </SelectContent>
                  </Select>
                </ControlField>
                <ControlField label="Opacity">
                  <InputGroup>
                    <InputGroupInput
                      type="number"
                      inputMode="decimal"
                      className={cn('h-8', controlBgClass)}
                      value={Math.round((videoBackground.opacity ?? 1) * 100)}
                      onValueChange={(value) =>
                        updateVideo({ opacity: clampPercent(value) / 100 })
                      }
                    />
                    <InputGroupAddon align="inline-end">%</InputGroupAddon>
                  </InputGroup>
                </ControlField>
              </div>

              <div className="grid grid-cols-2 gap-2">
                <ControlField label="Loop" inline>
                  <Switch
                    checked={videoBackground.loop}
                    onCheckedChange={(checked) => updateVideo({ loop: checked })}
                  />
                </ControlField>
                <ControlField label="Muted" inline>
                  <Switch
                    checked={videoBackground.muted}
                    onCheckedChange={(checked) => updateVideo({ muted: checked })}
                  />
                </ControlField>
              </div>
            </TabsContent>

          </Tabs>
        </PopoverContent>
      </Popover>
    </div>
  );
}
