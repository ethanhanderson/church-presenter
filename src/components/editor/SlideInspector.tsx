/**
 * SlideInspector - Right panel for editing slide and layer properties
 */

import { useMemo, useState } from 'react';
import {
  Type,
  Image,
  ChevronUp,
  ChevronDown,
  Eye,
  EyeOff,
  Lock,
  Unlock,
  Trash2,
  Play,
  Settings2,
  Check,
  ChevronsUpDown,
} from 'lucide-react';
import { Label } from '@/components/ui/label';
import { Input } from '@/components/ui/input';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Switch } from '@/components/ui/switch';
import { ScrollArea } from '@/components/ui/scroll-area';
import { Separator } from '@/components/ui/separator';
import { Textarea } from '@/components/ui/textarea';
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from '@/components/ui/popover';
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
import { useEditorStore } from '@/lib/stores';
import { useSystemFonts } from '@/hooks';
import type {
  Slide,
  Theme,
  Background,
  TextStyle,
  Layer,
  TextLayer,
  ShapeLayer,
  MediaLayer,
  WebLayer,
  LayerTransform,
  SlideTransitionType,
  AnimationPreset,
  BuildTrigger,
} from '@/lib/models';
import { SONG_SECTIONS, formatSectionLabel, defaultSlideTransition } from '@/lib/models';
import { cn } from '@/lib/utils';

interface SlideInspectorProps {
  slide: Slide | null;
  theme: Theme | null;
}

export function SlideInspector({ slide, theme }: SlideInspectorProps) {
  const {
    updateSlide,
    setSlideBackground,
    setSlideSection,
    setSlideTransition,
    updateLayer,
    deleteLayer,
    selectLayer,
    selection,
    presentation,
    applyTheme,
    saveThemeFromSlide,
    bringLayerForward,
    sendLayerBackward,
    addBuildStep,
  } = useEditorStore();

  if (!slide) {
    return (
      <div className="flex h-full items-center justify-center p-4 text-muted-foreground text-sm">
        Select a slide to edit its properties
      </div>
    );
  }

  const selectedLayers = useMemo(
    () => slide.layers.filter((layer) => selection.layerIds.includes(layer.id)),
    [slide.layers, selection.layerIds]
  );
  const selectedLayer = selectedLayers[0] || null;
  const slideSize = useMemo(() => getBaseSlideSize(theme?.aspectRatio), [theme?.aspectRatio]);

  const effectiveBackground = slide.overrides?.background || theme?.background;
  const transition = slide.animations?.transition || defaultSlideTransition;

  return (
    <ScrollArea className="h-full">
      <div className="space-y-4 p-3">
        {selectedLayer ? (
          <>
            <InspectorSection title="Selection">
              <ControlField label="Name">
                <Input
                  value={selectedLayer.name}
                  onChange={(e) =>
                    updateLayer(slide.id, selectedLayer.id, { name: e.target.value })
                  }
                  className="h-7 text-xs"
                />
              </ControlField>
              <ControlField label="Type" inline>
                <div className="text-xs text-muted-foreground capitalize">
                  {selectedLayer.type}
                </div>
              </ControlField>
              <ControlField label="Visible" inline>
                <Switch
                  checked={selectedLayer.visible}
                  onCheckedChange={(checked) =>
                    updateLayer(slide.id, selectedLayer.id, { visible: checked })
                  }
                />
              </ControlField>
              <ControlField label="Locked" inline>
                <Switch
                  checked={selectedLayer.locked}
                  onCheckedChange={(checked) =>
                    updateLayer(slide.id, selectedLayer.id, { locked: checked })
                  }
                />
              </ControlField>
              <Button
                variant="destructive"
                size="sm"
                className="w-full"
                onClick={() => deleteLayer(slide.id, selectedLayer.id)}
              >
                <Trash2 className="mr-2 h-3 w-3" />
                Delete Layer
              </Button>
            </InspectorSection>

            <InspectorSection title="Transform">
              <LayerLayoutEditor
                layers={selectedLayers}
                slideSize={slideSize}
                onChange={(layerId, transform) => {
                  const targetLayer = slide.layers.find((layer) => layer.id === layerId);
                  if (!targetLayer) return;
                  updateLayer(slide.id, layerId, {
                    transform: { ...targetLayer.transform, ...transform },
                  });
                }}
              />
            </InspectorSection>

            <InspectorSection title="Appearance">
              <LayerStyleEditor
                layer={selectedLayer}
                theme={theme}
                onChange={(updates) =>
                  updateLayer(slide.id, selectedLayer.id, updates)
                }
              />
            </InspectorSection>
          </>
        ) : (
          <>
            <InspectorSection title="Layers">
              <div className="border rounded-md divide-y overflow-hidden">
                {slide.layers.length === 0 ? (
                  <div className="p-3 text-xs text-muted-foreground text-center">
                    No layers yet
                  </div>
                ) : (
                  [...slide.layers].reverse().map((layer) => (
                    <LayerListItem
                      key={layer.id}
                      layer={layer}
                      isSelected={selection.layerIds.includes(layer.id)}
                      onSelect={() => selectLayer(layer.id)}
                      onToggleVisible={() =>
                        updateLayer(slide.id, layer.id, { visible: !layer.visible })
                      }
                      onToggleLock={() =>
                        updateLayer(slide.id, layer.id, { locked: !layer.locked })
                      }
                      onDelete={() => deleteLayer(slide.id, layer.id)}
                      onMoveUp={() => bringLayerForward(slide.id, layer.id)}
                      onMoveDown={() => sendLayerBackward(slide.id, layer.id)}
                    />
                  ))
                )}
              </div>
            </InspectorSection>

            <InspectorSection title="Slide">
              <ControlField label="Section">
                <Select
                  value={slide.section || '_none'}
                  onValueChange={(v) =>
                    setSlideSection(slide.id, v === '_none' ? undefined! : (v as any))
                  }
                >
                  <SelectTrigger className="h-8 text-xs">
                    <SelectValue placeholder="No section" />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="_none">No section</SelectItem>
                    <Separator className="my-1" />
                    {SONG_SECTIONS.map((section) => (
                      <SelectItem key={section} value={section}>
                        {formatSectionLabel(section)}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </ControlField>

              {slide.section && (
                <ControlField label="Section Label">
                  <Input
                    value={slide.sectionLabel || ''}
                    onChange={(e) =>
                      updateSlide(slide.id, { sectionLabel: e.target.value })
                    }
                    placeholder={formatSectionLabel(slide.section)}
                    className="h-7 text-xs"
                  />
                </ControlField>
              )}
            </InspectorSection>

            <InspectorSection title="Background">
              <BackgroundEditor
                background={effectiveBackground}
                onChange={(bg) => setSlideBackground(slide.id, bg)}
              />
            </InspectorSection>

            <InspectorSection title="Transition">
              <TransitionEditor
                transition={transition}
                onChange={(t) => setSlideTransition(slide.id, t)}
              />
            </InspectorSection>

            <InspectorSection
              title="Build"
              actions={
                <BuildAnimationPopover
                  slide={slide}
                  onAddStep={(step) => addBuildStep(slide.id, step)}
                />
              }
            >
              <div className="text-xs text-muted-foreground">
                {slide.animations?.buildIn && slide.animations.buildIn.length > 0
                  ? `${slide.animations.buildIn.length} build step(s)`
                  : 'No build animations'}
              </div>
            </InspectorSection>

            <InspectorSection title="Notes">
              <Textarea
                value={slide.notes || ''}
                onChange={(e) => updateSlide(slide.id, { notes: e.target.value })}
                placeholder="Add notes for this slide..."
                className="min-h-[96px] text-xs"
              />
            </InspectorSection>

            <InspectorSection title="Theme">
              <ControlField label="Active Theme">
                <Select
                  value={presentation?.manifest.themeId || ''}
                  onValueChange={applyTheme}
                >
                  <SelectTrigger className="h-8 text-xs">
                    <SelectValue placeholder="Select theme" />
                  </SelectTrigger>
                  <SelectContent>
                    {presentation?.themes.map((t) => (
                      <SelectItem key={t.id} value={t.id}>
                        {t.name}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </ControlField>
              <Separator />
              <div className="space-y-2">
                <Label className="text-[10px] text-muted-foreground">Save as Theme</Label>
                <p className="text-[10px] text-muted-foreground">
                  Save the current slide's style as a new theme
                </p>
                <ThemeSaveButton
                  slideId={slide.id}
                  onSave={(name) => saveThemeFromSlide(slide.id, name)}
                />
              </div>
            </InspectorSection>
          </>
        )}
      </div>
    </ScrollArea>
  );
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
interface LayerListItemProps {
  layer: Layer;
  isSelected: boolean;
  onSelect: () => void;
  onToggleVisible: () => void;
  onToggleLock: () => void;
  onDelete: () => void;
  onMoveUp: () => void;
  onMoveDown: () => void;
}

function LayerListItem({
  layer,
  isSelected,
  onSelect,
  onToggleVisible,
  onToggleLock,
  onDelete,
  onMoveUp,
  onMoveDown,
}: LayerListItemProps) {
  const layerTypeIcons = {
    text: <Type className="h-3 w-3" />,
    shape: <div className="w-3 h-3 bg-current rounded-sm" />,
    media: <Image className="h-3 w-3" />,
    web: <Settings2 className="h-3 w-3" />,
  };

  return (
    <div
      className={cn(
        'flex items-center gap-1 p-1.5 cursor-pointer',
        isSelected && 'bg-accent',
        !layer.visible && 'opacity-50'
      )}
      onClick={onSelect}
    >
      <span className="text-muted-foreground">{layerTypeIcons[layer.type]}</span>
      <span className="flex-1 text-xs truncate">{layer.name}</span>
      <div className="flex items-center gap-0.5">
        <Button
          variant="ghost"
          size="icon"
          className="h-5 w-5"
          onClick={(e) => {
            e.stopPropagation();
            onMoveUp();
          }}
        >
          <ChevronUp className="h-3 w-3" />
        </Button>
        <Button
          variant="ghost"
          size="icon"
          className="h-5 w-5"
          onClick={(e) => {
            e.stopPropagation();
            onMoveDown();
          }}
        >
          <ChevronDown className="h-3 w-3" />
        </Button>
        <Button
          variant="ghost"
          size="icon"
          className="h-5 w-5"
          onClick={(e) => {
            e.stopPropagation();
            onToggleVisible();
          }}
        >
          {layer.visible ? <Eye className="h-3 w-3" /> : <EyeOff className="h-3 w-3" />}
        </Button>
        <Button
          variant="ghost"
          size="icon"
          className="h-5 w-5"
          onClick={(e) => {
            e.stopPropagation();
            onToggleLock();
          }}
        >
          {layer.locked ? <Lock className="h-3 w-3" /> : <Unlock className="h-3 w-3" />}
        </Button>
        <Button
          variant="ghost"
          size="icon"
          className="h-5 w-5 text-destructive"
          onClick={(e) => {
            e.stopPropagation();
            onDelete();
          }}
        >
          <Trash2 className="h-3 w-3" />
        </Button>
      </div>
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

  return (
    <div className="space-y-3">
      <div className="grid grid-cols-2 gap-2">
        <ControlField label="X Position">
          <ScrubbableNumberInput
            value={Math.round(transform.x)}
            onValueChange={(next) => applyToSelection({ x: next })}
            step={1}
          />
        </ControlField>
        <ControlField label="Y Position">
          <ScrubbableNumberInput
            value={Math.round(transform.y)}
            onValueChange={(next) => applyToSelection({ y: next })}
            step={1}
          />
        </ControlField>
        <ControlField label="Width">
          <ScrubbableNumberInput
            value={Math.round(transform.width)}
            onValueChange={(next) => applyToSelection({ width: Math.max(1, next) })}
            step={1}
            min={1}
          />
        </ControlField>
        <ControlField label="Height">
          <ScrubbableNumberInput
            value={Math.round(transform.height)}
            onValueChange={(next) => applyToSelection({ height: Math.max(1, next) })}
            step={1}
            min={1}
          />
        </ControlField>
      </div>

      <div className="grid grid-cols-2 gap-2">
        <ControlField label="Rotation">
          <div className="flex items-center gap-2">
            <ScrubbableNumberInput
              value={Math.round(transform.rotation)}
              onValueChange={(next) => applyToSelection({ rotation: next })}
              min={-180}
              max={180}
              step={1}
            />
            <span className="text-[10px] text-muted-foreground">deg</span>
          </div>
        </ControlField>
        <ControlField label="Opacity">
          <div className="flex items-center gap-2">
            <ScrubbableNumberInput
              value={Math.round(transform.opacity * 100)}
              onValueChange={(next) => applyToSelection({ opacity: next / 100 })}
              min={0}
              max={100}
              step={1}
            />
            <span className="text-[10px] text-muted-foreground">%</span>
          </div>
        </ControlField>
      </div>

      <Separator />

      <ControlField label="Align">
        <div className="grid grid-cols-3 gap-2">
          <Button variant="outline" size="sm" onClick={() => alignLayers('left')}>
            Left
          </Button>
          <Button variant="outline" size="sm" onClick={() => alignLayers('center')}>
            Center
          </Button>
          <Button variant="outline" size="sm" onClick={() => alignLayers('right')}>
            Right
          </Button>
          <Button variant="outline" size="sm" onClick={() => alignLayers('top')}>
            Top
          </Button>
          <Button variant="outline" size="sm" onClick={() => alignLayers('middle')}>
            Middle
          </Button>
          <Button variant="outline" size="sm" onClick={() => alignLayers('bottom')}>
            Bottom
          </Button>
        </div>
      </ControlField>

      <ControlField label="Distribute">
        <div className="grid grid-cols-2 gap-2">
          <Button
            variant="outline"
            size="sm"
            onClick={() => distributeLayers('horizontal')}
            disabled={!canDistribute}
          >
            Horizontal
          </Button>
          <Button
            variant="outline"
            size="sm"
            onClick={() => distributeLayers('vertical')}
            disabled={!canDistribute}
          >
            Vertical
          </Button>
        </div>
      </ControlField>
    </div>
  );
}

// Layer Style Editor
interface LayerStyleEditorProps {
  layer: Layer;
  theme: Theme | null;
  onChange: (updates: Partial<Layer>) => void;
}

function LayerStyleEditor({ layer, theme, onChange }: LayerStyleEditorProps) {
  switch (layer.type) {
    case 'text':
      return (
        <TextLayerStyleEditor
          layer={layer}
          theme={theme}
          onChange={onChange}
        />
      );
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

// Text Layer Style Editor (includes fit settings)
interface TextLayerStyleEditorProps {
  layer: TextLayer;
  theme: Theme | null;
  onChange: (updates: Partial<TextLayer>) => void;
}

function TextLayerStyleEditor({ layer, theme, onChange }: TextLayerStyleEditorProps) {
  const textFit = layer.textFit || 'auto';
  const textMode = layer.textMode || 'custom';
  const padding = layer.padding ?? 2;

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
    <div className="space-y-4">
      {/* Text Display Mode Presets */}
      <div className="space-y-2">
        <Label className="text-xs">Display Mode</Label>
        <Select value={textMode} onValueChange={(v) => applyTextMode(v as TextLayer['textMode'])}>
          <SelectTrigger>
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="lyrics">Lyrics (Music)</SelectItem>
            <SelectItem value="paragraph">Paragraph (Notes)</SelectItem>
            <SelectItem value="custom">Custom</SelectItem>
          </SelectContent>
        </Select>
        <p className="text-[10px] text-muted-foreground">
          {textMode === 'lyrics' && 'Large centered text that shrinks to fit'}
          {textMode === 'paragraph' && 'Left-aligned readable text for notes'}
          {textMode === 'custom' && 'Manually configure all settings'}
        </p>
      </div>

      <Separator />

      {/* Text Fit Mode */}
      <div className="space-y-2">
        <Label className="text-xs">Text Fit</Label>
        <Select
          value={textFit}
          onValueChange={(v) => onChange({ textFit: v as TextLayer['textFit'], textMode: 'custom' } as Partial<TextLayer>)}
        >
          <SelectTrigger>
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="auto">Auto (Natural Size)</SelectItem>
            <SelectItem value="shrink">Shrink to Fit</SelectItem>
            <SelectItem value="fill">Fill Container</SelectItem>
          </SelectContent>
        </Select>
        <p className="text-[10px] text-muted-foreground">
          {textFit === 'auto' && 'Text displays at its natural size'}
          {textFit === 'shrink' && 'Text shrinks if it overflows the box'}
          {textFit === 'fill' && 'Text scales to fill the entire box'}
        </p>
      </div>

      {/* Padding */}
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

      <Separator />

      {/* Standard Text Style Editor */}
      <TextStyleEditor
        style={layer.style || theme?.primaryText}
        onChange={(style) => onChange({ style, textMode: 'custom' } as Partial<TextLayer>)}
      />
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

  return (
    <div className="space-y-4">
      <div className="space-y-2">
        <Label className="text-xs">Fill Color</Label>
        <div className="flex items-center gap-2">
          <div
            className="w-8 h-8 rounded border"
            style={{ backgroundColor: style.fill }}
          />
          <Input
            type="color"
            value={style.fill}
            onChange={(e) =>
              onChange({ style: { ...style, fill: e.target.value } })
            }
            className="flex-1 h-8"
          />
        </div>
      </div>

      <ControlField label="Fill Opacity">
        <div className="flex items-center gap-2">
          <ScrubbableNumberInput
            value={Math.round(style.fillOpacity * 100)}
            onValueChange={(next) =>
              onChange({ style: { ...style, fillOpacity: next / 100 } })
            }
            min={0}
            max={100}
            step={1}
          />
          <span className="text-[10px] text-muted-foreground">%</span>
        </div>
      </ControlField>

      <Separator />

      <div className="space-y-2">
        <Label className="text-xs">Stroke Color</Label>
        <div className="flex items-center gap-2">
          <div
            className="w-8 h-8 rounded border"
            style={{ backgroundColor: style.stroke }}
          />
          <Input
            type="color"
            value={style.stroke}
            onChange={(e) =>
              onChange({ style: { ...style, stroke: e.target.value } })
            }
            className="flex-1 h-8"
          />
        </div>
      </div>

      <ControlField label="Stroke Width">
        <ScrubbableNumberInput
          value={Math.round(style.strokeWidth)}
          onValueChange={(next) =>
            onChange({ style: { ...style, strokeWidth: next } })
          }
          min={0}
          max={20}
          step={1}
        />
      </ControlField>

      {layer.shapeType === 'rectangle' && (
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
      )}
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
      <div className="space-y-2">
        <Label className="text-xs">Fit</Label>
        <Select value={layer.fit} onValueChange={(v) => onChange({ fit: v as any })}>
          <SelectTrigger>
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="cover">Cover</SelectItem>
            <SelectItem value="contain">Contain</SelectItem>
            <SelectItem value="fill">Fill</SelectItem>
            <SelectItem value="none">None</SelectItem>
          </SelectContent>
        </Select>
      </div>

      {layer.mediaType === 'video' && (
        <>
          <Separator />
          <div className="space-y-3">
            <div className="flex items-center justify-between">
              <Label className="text-xs">Loop</Label>
              <Switch
                checked={layer.loop}
                onCheckedChange={(checked) => onChange({ loop: checked })}
              />
            </div>
            <div className="flex items-center justify-between">
              <Label className="text-xs">Muted</Label>
              <Switch
                checked={layer.muted}
                onCheckedChange={(checked) => onChange({ muted: checked })}
              />
            </div>
            <div className="flex items-center justify-between">
              <Label className="text-xs">Autoplay</Label>
              <Switch
                checked={layer.autoplay}
                onCheckedChange={(checked) => onChange({ autoplay: checked })}
              />
            </div>
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
      <div className="space-y-2">
        <Label className="text-xs">URL</Label>
        <Input
          value={layer.url}
          onChange={(e) => onChange({ url: e.target.value })}
          placeholder="https://..."
        />
      </div>

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

      <div className="flex items-center justify-between">
        <Label className="text-xs">Interactive</Label>
        <Switch
          checked={layer.interactive}
          onCheckedChange={(checked) => onChange({ interactive: checked })}
        />
      </div>
    </div>
  );
}

// Text Style Editor
interface TextStyleEditorProps {
  style?: Partial<TextStyle>;
  onChange: (style: Partial<TextStyle>) => void;
}

function TextStyleEditor({ style, onChange }: TextStyleEditorProps) {
  const { presentation, ensureFontFamilyBundled } = useEditorStore();
  const { fonts: systemFonts, isLoading: isFontsLoading } = useSystemFonts();
  const [fontPickerOpen, setFontPickerOpen] = useState(false);

  const fontFamily = style?.font?.family || 'Inter';
  const availableFonts = useMemo(() => {
    const families = new Set(systemFonts.map((font) => font.family));
    return Array.from(families).sort((a, b) => a.localeCompare(b));
  }, [systemFonts]);

  const bundledFamilies = useMemo(() => {
    if (!presentation?.manifest?.fonts?.length) return new Set<string>();
    return new Set(presentation.manifest.fonts.map((font) => font.family));
  }, [presentation?.manifest?.fonts]);

  const bundleCandidates = useMemo(
    () => systemFonts.filter((font) => font.path),
    [systemFonts]
  );

  const handleFontSelect = (family: string) => {
    onChange({ ...style, font: { ...style?.font!, family } });
    if (bundleCandidates.length > 0) {
      void ensureFontFamilyBundled(family, bundleCandidates);
    }
    setFontPickerOpen(false);
  };

  return (
    <div className="space-y-4">
      <div className="space-y-2">
        <Label className="text-xs">Font Family</Label>
        <Popover open={fontPickerOpen} onOpenChange={setFontPickerOpen}>
          <PopoverTrigger asChild>
            <Button variant="outline" size="sm" className="w-full justify-between">
              <span className="truncate text-left" style={{ fontFamily }}>
                {fontFamily}
              </span>
              <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
            </Button>
          </PopoverTrigger>
          <PopoverContent className="w-[260px] p-0" align="start">
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
      </div>

      <ControlField label="Font Size">
        <div className="flex items-center gap-2">
          <ScrubbableNumberInput
            value={style?.font?.size || 72}
            onValueChange={(next) =>
              onChange({ ...style, font: { ...style?.font!, size: next } })
            }
            min={12}
            max={200}
            step={1}
          />
          <span className="text-[10px] text-muted-foreground">px</span>
        </div>
      </ControlField>

      <div className="space-y-2">
        <Label className="text-xs">Font Weight</Label>
        <Select
          value={String(style?.font?.weight || 700)}
          onValueChange={(v) =>
            onChange({ ...style, font: { ...style?.font!, weight: parseInt(v) } })
          }
        >
          <SelectTrigger>
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="100">Thin</SelectItem>
            <SelectItem value="300">Light</SelectItem>
            <SelectItem value="400">Regular</SelectItem>
            <SelectItem value="500">Medium</SelectItem>
            <SelectItem value="600">Semibold</SelectItem>
            <SelectItem value="700">Bold</SelectItem>
            <SelectItem value="800">Extra Bold</SelectItem>
            <SelectItem value="900">Black</SelectItem>
          </SelectContent>
        </Select>
      </div>

      <div className="space-y-2">
        <Label className="text-xs">Text Color</Label>
        <div className="flex items-center gap-2">
          <div
            className="w-8 h-8 rounded border"
            style={{ backgroundColor: style?.color || '#ffffff' }}
          />
          <Input
            type="color"
            value={style?.color || '#ffffff'}
            onChange={(e) => onChange({ ...style, color: e.target.value })}
            className="flex-1 h-8"
          />
        </div>
      </div>

      <ControlField label="Alignment">
        <ToggleGroup
          type="single"
          value={style?.alignment || 'center'}
          onValueChange={(value) => value && onChange({ ...style, alignment: value as any })}
          variant="outline"
          size="sm"
          className="w-full"
          spacing={0}
        >
          <ToggleGroupItem value="left" className="flex-1 text-xs">
            Left
          </ToggleGroupItem>
          <ToggleGroupItem value="center" className="flex-1 text-xs">
            Center
          </ToggleGroupItem>
          <ToggleGroupItem value="right" className="flex-1 text-xs">
            Right
          </ToggleGroupItem>
        </ToggleGroup>
      </ControlField>

      <Separator />

      <div className="space-y-2">
        <div className="flex items-center justify-between">
          <Label className="text-xs">Text Shadow</Label>
          <Switch
            checked={style?.shadow?.enabled || false}
            onCheckedChange={(checked) =>
              onChange({
                ...style,
                shadow: {
                  enabled: checked,
                  color: style?.shadow?.color || 'rgba(0,0,0,0.8)',
                  offsetX: style?.shadow?.offsetX || 2,
                  offsetY: style?.shadow?.offsetY || 2,
                  blur: style?.shadow?.blur || 8,
                },
              })
            }
          />
        </div>
        {style?.shadow?.enabled && (
          <div className="space-y-2 pl-2 border-l-2">
            <ControlField label="Blur">
              <ScrubbableNumberInput
                value={style.shadow.blur}
                onValueChange={(next) =>
                  onChange({ ...style, shadow: { ...style.shadow!, blur: next } })
                }
                min={0}
                max={20}
                step={1}
              />
            </ControlField>
          </div>
        )}
      </div>

      <div className="space-y-2">
        <div className="flex items-center justify-between">
          <Label className="text-xs">Text Outline</Label>
          <Switch
            checked={style?.outline?.enabled || false}
            onCheckedChange={(checked) =>
              onChange({
                ...style,
                outline: {
                  enabled: checked,
                  color: style?.outline?.color || '#000000',
                  width: style?.outline?.width || 2,
                },
              })
            }
          />
        </div>
        {style?.outline?.enabled && (
          <div className="space-y-2 pl-2 border-l-2">
            <ControlField label="Width">
              <ScrubbableNumberInput
                value={style.outline.width}
                onValueChange={(next) =>
                  onChange({ ...style, outline: { ...style.outline!, width: next } })
                }
                min={1}
                max={10}
                step={1}
              />
            </ControlField>
          </div>
        )}
      </div>
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
      <Select
        value={transition.type}
        onValueChange={(v) => onChange({ type: v as SlideTransitionType })}
      >
        <SelectTrigger>
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

      {transition.type !== 'none' && (
        <ControlField label="Duration">
          <div className="flex items-center gap-2">
            <ScrubbableNumberInput
              value={transition.duration}
              onValueChange={(next) => onChange({ duration: next })}
              min={100}
              max={1000}
              step={10}
            />
            <span className="text-[10px] text-muted-foreground">ms</span>
          </div>
        </ControlField>
      )}
    </div>
  );
}

// Build Animation Popover
interface BuildAnimationPopoverProps {
  slide: Slide;
  onAddStep: (step: Omit<import('@/lib/models').BuildStep, 'id'>) => void;
}

function BuildAnimationPopover({ slide, onAddStep }: BuildAnimationPopoverProps) {
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
    <Popover>
      <PopoverTrigger asChild>
        <Button variant="outline" size="sm">
          <Play className="mr-1 h-3 w-3" />
          Add
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-80" align="end">
        <div className="space-y-4">
          <div className="space-y-2">
            <h4 className="font-medium text-sm">Add Build Animation</h4>
            <p className="text-xs text-muted-foreground">
              Animate layers when entering or advancing the slide
            </p>
          </div>

          <div className="space-y-2">
            <Label className="text-xs">Layer</Label>
            <Select value={selectedLayerId} onValueChange={setSelectedLayerId}>
              <SelectTrigger>
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
          </div>

          <div className="space-y-2">
            <Label className="text-xs">Animation</Label>
            <Select value={preset} onValueChange={(v) => setPreset(v as AnimationPreset)}>
              <SelectTrigger>
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
          </div>

          <div className="space-y-2">
            <Label className="text-xs">Trigger</Label>
            <Select value={trigger} onValueChange={(v) => setTrigger(v as BuildTrigger)}>
              <SelectTrigger>
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
          </div>

          <ControlField label="Duration">
            <div className="flex items-center gap-2">
              <ScrubbableNumberInput
                value={duration}
                onValueChange={(next) => setDuration(next)}
                min={100}
                max={1000}
                step={10}
              />
              <span className="text-[10px] text-muted-foreground">ms</span>
            </div>
          </ControlField>

          <Button
            className="w-full"
            onClick={handleAdd}
            disabled={!selectedLayerId}
          >
            Add Build Step
          </Button>
        </div>
      </PopoverContent>
    </Popover>
  );
}

// Background Editor
interface BackgroundEditorProps {
  background?: Background;
  onChange: (bg: Background) => void;
}

function BackgroundEditor({ background, onChange }: BackgroundEditorProps) {
  const [type, setType] = useState<Background['type']>(background?.type || 'solid');

  const handleTypeChange = (newType: Background['type']) => {
    setType(newType);
    
    switch (newType) {
      case 'solid':
        onChange({ type: 'solid', color: '#1a1a2e' });
        break;
      case 'gradient':
        onChange({
          type: 'gradient',
          angle: 180,
          stops: [
            { color: '#1a1a2e', position: 0 },
            { color: '#16213e', position: 100 },
          ],
        });
        break;
    }
  };

  return (
    <div className="space-y-3">
      <Select value={type} onValueChange={(v) => handleTypeChange(v as Background['type'])}>
        <SelectTrigger>
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value="solid">Solid Color</SelectItem>
          <SelectItem value="gradient">Gradient</SelectItem>
          <SelectItem value="image">Image</SelectItem>
          <SelectItem value="video">Video</SelectItem>
        </SelectContent>
      </Select>

      {type === 'solid' && background?.type === 'solid' && (
        <div className="flex items-center gap-2">
          <div
            className="w-8 h-8 rounded border cursor-pointer"
            style={{ backgroundColor: background.color }}
          />
          <Input
            type="color"
            value={background.color}
            onChange={(e) => onChange({ ...background, color: e.target.value })}
            className="w-full h-8"
          />
        </div>
      )}

      {type === 'gradient' && background?.type === 'gradient' && (
        <div className="space-y-3">
          <ControlField label="Angle">
            <div className="flex items-center gap-2">
              <ScrubbableNumberInput
                value={Math.round(background.angle)}
                onValueChange={(next) => onChange({ ...background, angle: next })}
                min={0}
                max={360}
                step={1}
              />
              <span className="text-[10px] text-muted-foreground">deg</span>
            </div>
          </ControlField>
          <div className="space-y-1">
            <Label className="text-xs">Colors</Label>
            <div className="flex gap-2">
              {background.stops.map((stop, i) => (
                <Input
                  key={i}
                  type="color"
                  value={stop.color}
                  onChange={(e) => {
                    const newStops = [...background.stops];
                    newStops[i] = { ...stop, color: e.target.value };
                    onChange({ ...background, stops: newStops });
                  }}
                  className="w-10 h-8 p-0"
                />
              ))}
            </div>
          </div>
        </div>
      )}

      {type === 'image' && (
        <div className="space-y-2">
          <Button variant="outline" className="w-full">
            <Image className="mr-2 h-4 w-4" />
            Choose Image
          </Button>
        </div>
      )}

      {type === 'video' && (
        <div className="space-y-2">
          <Button variant="outline" className="w-full">
            <Image className="mr-2 h-4 w-4" />
            Choose Video
          </Button>
        </div>
      )}
    </div>
  );
}

// Theme Save Button
interface ThemeSaveButtonProps {
  slideId: string;
  onSave: (name: string) => void;
}

function ThemeSaveButton({ onSave }: ThemeSaveButtonProps) {
  const [name, setName] = useState('');

  const handleSave = () => {
    if (name.trim()) {
      onSave(name.trim());
      setName('');
    }
  };

  return (
    <div className="flex gap-2">
      <Input
        value={name}
        onChange={(e) => setName(e.target.value)}
        placeholder="Theme name..."
        className="flex-1"
      />
      <Button onClick={handleSave} disabled={!name.trim()}>
        Save
      </Button>
    </div>
  );
}
