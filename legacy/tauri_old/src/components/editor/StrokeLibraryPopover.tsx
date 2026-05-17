import type { ReactNode } from 'react';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { useEditorStore } from '@/lib/stores';
import type { LayerStroke } from '@/lib/models';
import { cn } from '@/lib/utils';
import { toRgba } from '@/lib/color-utils';

interface StrokeLibraryPopoverProps {
  children: ReactNode;
  onSelect?: (stroke: LayerStroke) => void;
  side?: 'top' | 'right' | 'bottom' | 'left';
  align?: 'start' | 'center' | 'end';
  className?: string;
}

export function StrokeLibraryPopover({
  children,
  onSelect,
  side = 'bottom',
  align = 'start',
  className,
}: StrokeLibraryPopoverProps) {
  const strokes = useEditorStore((state) => state.strokeLibrary);

  return (
    <Popover>
      <PopoverTrigger asChild>{children}</PopoverTrigger>
      <PopoverContent side={side} align={align} className={cn('w-56 p-3', className)}>
        <div className="space-y-2">
          <div className="text-[10px] font-semibold uppercase tracking-wide text-muted-foreground">
            Stroke Library
          </div>
          <div className="grid grid-cols-4 gap-2">
            {strokes.slice(0, 8).map((stroke) => (
              <button
                key={stroke.id}
                type="button"
                className="relative h-8 w-8 rounded-full border border-border/70 bg-background"
                onClick={() => onSelect?.(stroke)}
                aria-label={`Use ${stroke.color} stroke`}
              >
                <span
                  className="absolute inset-2 rounded-full"
                  style={{
                    border: `${Math.max(1, Math.round(stroke.width))}px solid ${toRgba(
                      stroke.color,
                      stroke.opacity
                    )}`,
                  }}
                />
              </button>
            ))}
          </div>
        </div>
      </PopoverContent>
    </Popover>
  );
}
