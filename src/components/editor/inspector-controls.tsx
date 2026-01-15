import type { ComponentProps, ReactNode } from 'react';
import { useRef } from 'react';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { cn } from '@/lib/utils';

interface InspectorSectionProps {
  title: string;
  description?: string;
  actions?: ReactNode;
  children: ReactNode;
}

export function InspectorSection({
  title,
  description,
  actions,
  children,
}: InspectorSectionProps) {
  return (
    <section className="space-y-3 rounded-md border bg-card/40 p-3">
      <div className="flex items-start justify-between gap-3">
        <div className="space-y-1">
          <div className="text-[10px] font-semibold uppercase tracking-wide text-muted-foreground">
            {title}
          </div>
          {description && (
            <p className="text-[11px] text-muted-foreground">{description}</p>
          )}
        </div>
        {actions}
      </div>
      <div className="space-y-3">{children}</div>
    </section>
  );
}

interface ControlFieldProps {
  label: string;
  hint?: string;
  children: ReactNode;
  inline?: boolean;
}

export function ControlField({ label, hint, children, inline }: ControlFieldProps) {
  return (
    <div
      className={cn(
        inline ? 'flex items-center justify-between gap-3' : 'space-y-1'
      )}
    >
      <div className={cn('space-y-1', inline && 'min-w-[90px]')}>
        <Label className="text-[10px] text-muted-foreground">{label}</Label>
        {hint && <p className="text-[10px] text-muted-foreground">{hint}</p>}
      </div>
      <div className={cn(!inline && 'pt-0.5', inline && 'flex-1')}>{children}</div>
    </div>
  );
}

interface ScrubbableNumberInputProps
  extends Omit<ComponentProps<typeof Input>, 'value' | 'onChange'> {
  value: number;
  onValueChange: (value: number) => void;
  min?: number;
  max?: number;
  step?: number;
  scrubStep?: number;
}

export function ScrubbableNumberInput({
  value,
  onValueChange,
  min,
  max,
  step = 1,
  scrubStep,
  className,
  ...props
}: ScrubbableNumberInputProps) {
  const inputRef = useRef<HTMLInputElement | null>(null);
  const pointerState = useRef({
    isScrubbing: false,
    pointerId: -1,
    startX: 0,
    startValue: 0,
  });

  const precision = String(step).includes('.')
    ? String(step).split('.')[1]?.length ?? 0
    : 0;
  const scrubUnit = scrubStep ?? step;

  const clampValue = (nextValue: number) => {
    let clamped = nextValue;
    if (typeof min === 'number') clamped = Math.max(min, clamped);
    if (typeof max === 'number') clamped = Math.min(max, clamped);
    if (precision > 0) {
      clamped = Number(clamped.toFixed(precision));
    }
    return clamped;
  };

  const beginScrub = (event: React.PointerEvent<HTMLInputElement>) => {
    if (event.button !== 0 || props.disabled || props.readOnly) return;
    pointerState.current = {
      isScrubbing: false,
      pointerId: event.pointerId,
      startX: event.clientX,
      startValue: Number.isFinite(value) ? value : 0,
    };
  };

  const updateScrub = (event: React.PointerEvent<HTMLInputElement>) => {
    if (event.pointerId !== pointerState.current.pointerId) {
      return;
    }
    const delta = event.clientX - pointerState.current.startX;
    if (!pointerState.current.isScrubbing && Math.abs(delta) < 3) {
      return;
    }
    if (!pointerState.current.isScrubbing) {
      pointerState.current.isScrubbing = true;
      inputRef.current?.setPointerCapture(event.pointerId);
      document.body.style.cursor = 'ew-resize';
      document.body.style.userSelect = 'none';
      inputRef.current?.blur();
    }
    event.preventDefault();
    const multiplier = event.shiftKey ? 0.2 : event.altKey ? 5 : 1;
    const nextValue = pointerState.current.startValue + (delta / 5) * scrubUnit * multiplier;
    onValueChange(clampValue(nextValue));
  };

  const endScrub = (event?: React.PointerEvent<HTMLInputElement>) => {
    const wasScrubbing = pointerState.current.isScrubbing;
    if (wasScrubbing && event && event.pointerId === pointerState.current.pointerId) {
      inputRef.current?.releasePointerCapture(event.pointerId);
    }
    pointerState.current = {
      isScrubbing: false,
      pointerId: -1,
      startX: 0,
      startValue: 0,
    };
    if (wasScrubbing) {
      document.body.style.cursor = '';
      document.body.style.userSelect = '';
    }
  };

  return (
    <Input
      ref={inputRef}
      type="number"
      inputMode="decimal"
      value={Number.isFinite(value) ? value : 0}
      onChange={(event) => {
        const next = parseFloat(event.target.value);
        onValueChange(clampValue(Number.isFinite(next) ? next : 0));
      }}
      onPointerDown={beginScrub}
      onPointerMove={updateScrub}
      onPointerUp={endScrub}
      onPointerCancel={endScrub}
      onPointerLeave={endScrub}
      className={cn('h-7 text-xs', className)}
      {...props}
    />
  );
}
