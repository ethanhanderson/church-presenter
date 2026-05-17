import type { ReactNode } from 'react';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { useEditorStore } from '@/lib/stores';
import { cn } from '@/lib/utils';

interface ColorLibraryPopoverProps {
  children: ReactNode;
  onSelect?: (color: string) => void;
  side?: 'top' | 'right' | 'bottom' | 'left';
  align?: 'start' | 'center' | 'end';
  className?: string;
}

export function ColorLibraryPopover({
  children,
  onSelect,
  side = 'bottom',
  align = 'start',
  className,
}: ColorLibraryPopoverProps) {
  const colors = useEditorStore((state) => state.colorLibrary);

  return (
    <Popover>
      <PopoverTrigger asChild>{children}</PopoverTrigger>
      <PopoverContent side={side} align={align} className={cn('w-44 p-3', className)}>
        <div className="space-y-2">
          <div className="text-[10px] font-semibold uppercase tracking-wide text-muted-foreground">
            Color Library
          </div>
          <div className="flex items-center gap-2">
            {colors.slice(0, 4).map((color) => (
              <button
                key={color}
                type="button"
                className="h-6 w-6 rounded-full border border-border/70 shadow-inner"
                style={{ backgroundColor: color }}
                onClick={() => onSelect?.(color)}
                aria-label={`Use ${color}`}
              />
            ))}
          </div>
        </div>
      </PopoverContent>
    </Popover>
  );
}
