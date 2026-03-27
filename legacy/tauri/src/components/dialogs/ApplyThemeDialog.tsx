/**
 * ApplyThemeDialog - preview and apply theme slides by type
 */

import { useEffect, useMemo, useState } from 'react';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';
import { ScrollArea } from '@/components/ui/scroll-area';
import { Switch } from '@/components/ui/switch';
import { Label } from '@/components/ui/label';
import { Checkbox } from '@/components/ui/checkbox';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import type { Presentation, Slide, ThemeTemplate, ThemeTemplateSlide } from '@/lib/models';
import {
  buildThemedSlidePreview,
  getSlideGroupLabel,
  normalizeLayoutType,
} from '@/lib/models';
import { SlideThumbnail } from '@/components/preview';

type ThemeScaleMode = 'none' | 'fit';

interface SlideSelection {
  slide: Slide;
  selectedThemeSlideId: string | null;
  autoThemeSlideId: string | null;
}

interface ApplyThemeDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  presentation: Presentation | null;
  theme: ThemeTemplate | null;
  onApply: (
    selections: Record<string, string>,
    options: { scaleMode: ThemeScaleMode }
  ) => void;
}

const getBaseSlideSize = (
  aspectRatio?: '16:9' | '4:3' | '16:10',
  slideSize?: { width: number; height: number }
) => {
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
};

const getThemeSlideLabel = (slide: ThemeTemplateSlide, index: number) =>
  slide.name?.trim() || slide.layoutType?.trim() || `Slide ${index + 1}`;

export function ApplyThemeDialog({
  open,
  onOpenChange,
  presentation,
  theme,
  onApply,
}: ApplyThemeDialogProps) {
  const [scaleMode, setScaleMode] = useState<ThemeScaleMode>('none');
  const [selections, setSelections] = useState<Record<string, string | null>>({});
  const [selectedSlideIds, setSelectedSlideIds] = useState<Set<string>>(new Set());

  const themeSlideMap = useMemo(() => {
    if (!theme) return new Map<string, ThemeTemplateSlide>();
    const map = new Map<string, ThemeTemplateSlide>();
    theme.slides.forEach((slide) => {
      const key = normalizeLayoutType(slide.layoutType);
      if (key) {
        map.set(key, slide);
      }
    });
    return map;
  }, [theme]);

  const slideSelections = useMemo<SlideSelection[]>(() => {
    if (!presentation) return [];
    return presentation.slides.map((slide) => {
      const layoutKey = normalizeLayoutType(slide.layoutType);
      const matched = layoutKey ? themeSlideMap.get(layoutKey) : undefined;
      const hasSelection = Object.prototype.hasOwnProperty.call(selections, slide.id);
      const selectedThemeSlideId = hasSelection
        ? selections[slide.id]
        : matched
          ? matched.id
          : null;
      return {
        slide,
        selectedThemeSlideId,
        autoThemeSlideId: matched?.id ?? null,
      };
    });
  }, [presentation, selections, themeSlideMap]);

  const themeSlides = theme?.slides ?? [];
  const changeCount = slideSelections.filter((entry) => entry.selectedThemeSlideId).length;
  const isAllSelected =
    presentation?.slides.length ? selectedSlideIds.size === presentation.slides.length : false;
  const isPartiallySelected =
    selectedSlideIds.size > 0 && presentation?.slides.length
      ? selectedSlideIds.size < presentation.slides.length
      : false;

  const aspectMismatch =
    theme &&
    presentation &&
    theme.aspectRatio &&
    presentation.manifest.aspectRatio &&
    theme.aspectRatio !== presentation.manifest.aspectRatio;

  useEffect(() => {
    if (!open || !presentation || !theme) return;
    const nextSelections: Record<string, string | null> = {};
    const nextSelected = new Set<string>();
    presentation.slides.forEach((slide) => {
      const layoutKey = normalizeLayoutType(slide.layoutType);
      const matched = layoutKey ? themeSlideMap.get(layoutKey) : undefined;
      nextSelections[slide.id] = matched ? matched.id : null;
      if (matched) {
        nextSelected.add(slide.id);
      }
    });
    setSelections(nextSelections);
    setSelectedSlideIds(nextSelected);
    setScaleMode(aspectMismatch ? 'fit' : 'none');
  }, [open, presentation, theme, themeSlideMap, aspectMismatch]);

  const handleSelectionChange = (slideId: string, value: string) => {
    const resolvedValue = value === '_none' ? null : value;
    setSelections((prev) => ({
      ...prev,
      [slideId]: resolvedValue,
    }));
    setSelectedSlideIds((prev) => {
      const next = new Set(prev);
      if (resolvedValue) {
        next.add(slideId);
      } else {
        next.delete(slideId);
      }
      return next;
    });
  };

  const toggleSlideSelection = (slideId: string, checked: boolean) => {
    setSelectedSlideIds((prev) => {
      const next = new Set(prev);
      if (checked) {
        next.add(slideId);
      } else {
        next.delete(slideId);
      }
      return next;
    });
  };

  const handleApply = () => {
    if (!presentation || !theme) return;
    const next: Record<string, string> = {};
    slideSelections.forEach((entry) => {
      if (entry.selectedThemeSlideId) {
        next[entry.slide.id] = entry.selectedThemeSlideId;
      }
    });
    if (Object.keys(next).length > 0) {
      try {
        onApply(next, { scaleMode });
      } catch (error) {
        console.error('Failed to apply theme from dialog', error);
      }
    }
    onOpenChange(false);
  };

  const targetSize = presentation
    ? getBaseSlideSize(presentation.manifest.aspectRatio, presentation.manifest.slideSize)
    : { width: 1920, height: 1080 };
  const sourceSize = theme
    ? theme.baseSize ?? getBaseSlideSize(theme.aspectRatio)
    : { width: 1920, height: 1080 };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-6xl sm:max-w-6xl h-[90vh] max-h-[90vh] min-h-0 flex flex-col overflow-hidden">
        <DialogHeader>
          <DialogTitle>Apply Theme to Presentation</DialogTitle>
          <DialogDescription>
            Review which slides will change and choose layouts for untyped slides.
          </DialogDescription>
        </DialogHeader>

        <div className="flex flex-wrap items-center justify-between gap-3 text-xs">
          <div className="text-muted-foreground">
            {selectedSlideIds.size} selected • {changeCount} will update
          </div>
          {aspectMismatch && (
            <div className="flex items-center gap-2">
              <Switch
                checked={scaleMode === 'fit'}
                onCheckedChange={(checked) => setScaleMode(checked ? 'fit' : 'none')}
                id="scale-theme"
              />
              <Label htmlFor="scale-theme" className="text-xs">
                Scale theme to fit presentation
              </Label>
            </div>
          )}
        </div>

        <div className="flex-1 min-h-0 rounded-md border flex flex-col overflow-hidden">
          <Table className="text-xs">
            <TableHeader>
              <TableRow>
                <TableHead className="w-[36px]">
                  <Checkbox
                    checked={isAllSelected ? true : isPartiallySelected ? 'indeterminate' : false}
                    onCheckedChange={(checked) => {
                      if (!presentation) return;
                      const nextChecked = Boolean(checked);
                      if (nextChecked) {
                        setSelectedSlideIds(new Set(presentation.slides.map((s) => s.id)));
                      } else {
                        setSelectedSlideIds(new Set());
                      }
                    }}
                    aria-label="Select all slides"
                  />
                </TableHead>
                <TableHead className="w-[140px]">Slide</TableHead>
                <TableHead className="w-[180px]">Current</TableHead>
                <TableHead className="w-[180px]">New</TableHead>
                <TableHead className="w-[200px]">Theme</TableHead>
              </TableRow>
            </TableHeader>
          </Table>
          <ScrollArea className="flex-1 min-h-0">
            <Table className="text-xs">
              <TableBody>
                {slideSelections.map((entry, index) => {
                  const themeSlide = themeSlides.find(
                    (slide) => slide.id === entry.selectedThemeSlideId
                  );
                  const previewSlide =
                    themeSlide && theme
                      ? buildThemedSlidePreview(entry.slide, themeSlide, {
                          scaleMode,
                          sourceSize,
                          targetSize,
                        })
                      : null;
                  const label =
                    getSlideGroupLabel(entry.slide) ||
                    entry.slide.layoutType?.trim() ||
                    entry.slide.type;
                  return (
                    <TableRow key={entry.slide.id}>
                      <TableCell className="align-top p-2 w-[36px]">
                        <div onClick={(event) => event.stopPropagation()}>
                          <Checkbox
                            checked={selectedSlideIds.has(entry.slide.id)}
                            onCheckedChange={(checked) =>
                              toggleSlideSelection(entry.slide.id, Boolean(checked))
                            }
                            aria-label={`Select slide ${index + 1}`}
                          />
                        </div>
                      </TableCell>
                      <TableCell className="align-top p-2 w-[140px]">
                        <div className="text-[11px] font-semibold">{`#${index + 1}`}</div>
                        <div className="text-[11px] text-muted-foreground">{label}</div>
                        {entry.slide.layoutType && (
                          <div className="text-[10px] text-muted-foreground">
                            Type: {entry.slide.layoutType}
                          </div>
                        )}
                      </TableCell>
                      <TableCell className="align-top p-2 w-[180px]">
                        <div className="w-32">
                          <SlideThumbnail
                            slide={entry.slide}
                            presentation={presentation ?? undefined}
                            showLabel={false}
                            useGroupColor={false}
                            showFooter={false}
                          />
                        </div>
                      </TableCell>
                      <TableCell className="align-top p-2 w-[180px]">
                        <div className="w-32">
                          {previewSlide ? (
                            <SlideThumbnail
                              slide={previewSlide}
                              presentation={presentation ?? undefined}
                              showLabel={false}
                              useGroupColor={false}
                              showFooter={false}
                            />
                          ) : (
                            <div className="h-20 rounded-md border border-dashed flex items-center justify-center text-[10px] text-muted-foreground">
                              No change
                            </div>
                          )}
                        </div>
                      </TableCell>
                      <TableCell className="align-top p-2 w-[200px]">
                        <div onClick={(event) => event.stopPropagation()}>
                          <Select
                            value={entry.selectedThemeSlideId ?? '_none'}
                            onValueChange={(value) => handleSelectionChange(entry.slide.id, value)}
                          >
                            <SelectTrigger className="h-7 text-[11px]">
                              <SelectValue placeholder="No change" />
                            </SelectTrigger>
                            <SelectContent>
                              <SelectItem value="_none">No change</SelectItem>
                              {themeSlides.map((slide, slideIndex) => (
                                <SelectItem key={slide.id} value={slide.id}>
                                  {getThemeSlideLabel(slide, slideIndex)}
                                </SelectItem>
                              ))}
                            </SelectContent>
                          </Select>
                        </div>
                        {themeSlides.length > 0 &&
                          entry.autoThemeSlideId &&
                          entry.autoThemeSlideId !== entry.selectedThemeSlideId && (
                            <div className="mt-1 text-[10px] text-muted-foreground">
                              Auto match:{' '}
                              {(() => {
                                const autoSlide = themeSlides.find(
                                  (slide) => slide.id === entry.autoThemeSlideId
                                );
                                if (!autoSlide) return 'Unknown';
                                return getThemeSlideLabel(
                                  autoSlide,
                                  themeSlides.findIndex((slide) => slide.id === autoSlide.id)
                                );
                              })()}
                            </div>
                          )}
                      </TableCell>
                    </TableRow>
                  );
                })}
              </TableBody>
            </Table>
          </ScrollArea>
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            Cancel
          </Button>
          <Button onClick={handleApply} disabled={changeCount === 0}>
            Apply Theme
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
