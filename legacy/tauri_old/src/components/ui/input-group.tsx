import * as React from "react"
import { cva, type VariantProps } from "class-variance-authority"

import { cn } from "@/lib/utils"
import { useCursor } from "@/components/cursor"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Textarea } from "@/components/ui/textarea"

function InputGroup({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="input-group"
      role="group"
      className={cn(
        "group/input-group border-input dark:bg-input/30 relative flex w-full items-center rounded-md border shadow-xs transition-[color,box-shadow] outline-none",
        "h-9 min-w-0 has-[>textarea]:h-auto",

        // Variants based on alignment.
        "has-[>[data-align=inline-start]]:[&>input]:pl-2",
        "has-[>[data-align=inline-end]]:[&>input]:pr-2",
        "has-[>[data-align=block-start]]:h-auto has-[>[data-align=block-start]]:flex-col has-[>[data-align=block-start]]:[&>input]:pb-3",
        "has-[>[data-align=block-end]]:h-auto has-[>[data-align=block-end]]:flex-col has-[>[data-align=block-end]]:[&>input]:pt-3",

        // Focus state.
        "has-[[data-slot=input-group-control]:focus-visible]:border-ring has-[[data-slot=input-group-control]:focus-visible]:ring-ring/50 has-[[data-slot=input-group-control]:focus-visible]:ring-[3px]",

        // Error state.
        "has-[[data-slot][aria-invalid=true]]:ring-destructive/20 has-[[data-slot][aria-invalid=true]]:border-destructive dark:has-[[data-slot][aria-invalid=true]]:ring-destructive/40",

        className
      )}
      {...props}
    />
  )
}

const inputGroupAddonVariants = cva(
  "text-muted-foreground flex h-auto cursor-text items-center justify-center gap-2 py-1.5 text-sm font-medium select-none [&>svg:not([class*='size-'])]:size-4 [&>kbd]:rounded-[calc(var(--radius)-5px)] group-data-[disabled=true]/input-group:opacity-50",
  {
    variants: {
      align: {
        "inline-start":
          "order-first pl-3 has-[>button]:ml-[-0.45rem] has-[>kbd]:ml-[-0.35rem]",
        "inline-end":
          "order-last pr-3 has-[>button]:mr-[-0.45rem] has-[>kbd]:mr-[-0.35rem]",
        "block-start":
          "order-first w-full justify-start px-3 pt-3 [.border-b]:pb-3 group-has-[>input]/input-group:pt-2.5",
        "block-end":
          "order-last w-full justify-start px-3 pb-3 [.border-t]:pt-3 group-has-[>input]/input-group:pb-2.5",
      },
    },
    defaultVariants: {
      align: "inline-start",
    },
  }
)

function InputGroupAddon({
  className,
  align = "inline-start",
  ...props
}: React.ComponentProps<"div"> & VariantProps<typeof inputGroupAddonVariants>) {
  return (
    <div
      role="group"
      data-slot="input-group-addon"
      data-align={align}
      className={cn(inputGroupAddonVariants({ align }), className)}
      onClick={(e) => {
        if ((e.target as HTMLElement).closest("button")) {
          return
        }
        e.currentTarget.parentElement?.querySelector("input")?.focus()
      }}
      {...props}
    />
  )
}

const inputGroupButtonVariants = cva(
  "text-sm shadow-none flex gap-2 items-center",
  {
    variants: {
      size: {
        xs: "h-6 gap-1 px-2 rounded-[calc(var(--radius)-5px)] [&>svg:not([class*='size-'])]:size-3.5 has-[>svg]:px-2",
        sm: "h-8 px-2.5 gap-1.5 rounded-md has-[>svg]:px-2.5",
        "icon-xs":
          "size-6 rounded-[calc(var(--radius)-5px)] p-0 has-[>svg]:p-0",
        "icon-sm": "size-8 p-0 has-[>svg]:p-0",
      },
    },
    defaultVariants: {
      size: "xs",
    },
  }
)

function InputGroupButton({
  className,
  type = "button",
  variant = "ghost",
  size = "xs",
  ...props
}: Omit<React.ComponentProps<typeof Button>, "size"> &
  VariantProps<typeof inputGroupButtonVariants>) {
  return (
    <Button
      type={type}
      data-size={size}
      variant={variant}
      className={cn(inputGroupButtonVariants({ size }), className)}
      {...props}
    />
  )
}

function InputGroupText({ className, ...props }: React.ComponentProps<"span">) {
  return (
    <span
      className={cn(
        "text-muted-foreground flex items-center gap-2 text-sm [&_svg]:pointer-events-none [&_svg:not([class*='size-'])]:size-4",
        className
      )}
      {...props}
    />
  )
}

type InputGroupInputProps = React.ComponentProps<"input"> & {
  onValueChange?: (value: number) => void
  scrubStep?: number
}

function InputGroupInput({
  className,
  onChange,
  onPointerDown,
  onPointerMove,
  onPointerUp,
  onPointerCancel,
  onPointerLeave,
  onKeyDown,
  onValueChange,
  scrubStep,
  type,
  min,
  max,
  step,
  ...props
}: InputGroupInputProps) {
  const pointerState = React.useRef({
    isScrubbing: false,
    pointerId: -1,
    startX: 0,
    startValue: 0,
  })
  const rafRef = React.useRef<number | null>(null)
  const pendingValue = React.useRef<number | null>(null)
  const { setOverrideVariant, clearOverrideVariant } = useCursor()

  const isScrubbableNumber = type === "number" && typeof onValueChange === "function"
  const stepValue =
    typeof step === "number"
      ? step
      : typeof step === "string" && step.length > 0
        ? Number(step)
        : 1
  const precision = String(stepValue).includes(".")
    ? String(stepValue).split(".")[1]?.length ?? 0
    : 0
  const scrubUnit = scrubStep ?? stepValue ?? 1
  const minValue =
    typeof min === "number"
      ? min
      : typeof min === "string" && min.length > 0
        ? Number(min)
        : undefined
  const maxValue =
    typeof max === "number"
      ? max
      : typeof max === "string" && max.length > 0
        ? Number(max)
        : undefined

  const clampValue = (nextValue: number) => {
    let clamped = nextValue
    if (Number.isFinite(minValue as number)) {
      clamped = Math.max(minValue as number, clamped)
    }
    if (Number.isFinite(maxValue as number)) {
      clamped = Math.min(maxValue as number, clamped)
    }
    if (precision > 0) {
      clamped = Number(clamped.toFixed(precision))
    }
    return clamped
  }

  const beginScrub = (event: React.PointerEvent<HTMLInputElement>) => {
    onPointerDown?.(event)
    if (!isScrubbableNumber) return
    if (event.button !== 0 || props.disabled || props.readOnly) return
    const startValue = Number.parseFloat(event.currentTarget.value)
    pointerState.current = {
      isScrubbing: false,
      pointerId: event.pointerId,
      startX: event.clientX,
      startValue: Number.isFinite(startValue) ? startValue : 0,
    }
  }

  const updateScrub = (event: React.PointerEvent<HTMLInputElement>) => {
    onPointerMove?.(event)
    if (!isScrubbableNumber) return
    if (event.pointerId !== pointerState.current.pointerId) {
      return
    }
    const delta = event.clientX - pointerState.current.startX
    if (!pointerState.current.isScrubbing && Math.abs(delta) < 3) {
      return
    }
    if (!pointerState.current.isScrubbing) {
      pointerState.current.isScrubbing = true
      event.currentTarget.setPointerCapture(event.pointerId)
      setOverrideVariant("ew-resize")
      document.body.style.cursor = "ew-resize"
      document.body.style.userSelect = "none"
      event.currentTarget.blur()
    }
    event.preventDefault()
    const multiplier = event.shiftKey ? 0.2 : event.altKey ? 5 : 1
    const nextValue =
      pointerState.current.startValue + (delta / 5) * scrubUnit * multiplier
    pendingValue.current = nextValue
    if (rafRef.current === null) {
      rafRef.current = window.requestAnimationFrame(() => {
        rafRef.current = null
        if (pendingValue.current === null) return
        onValueChange?.(clampValue(pendingValue.current))
        pendingValue.current = null
      })
    }
  }

  const endScrub = (event: React.PointerEvent<HTMLInputElement>) => {
    const wasScrubbing = pointerState.current.isScrubbing
    onPointerUp?.(event)
    if (wasScrubbing && event.pointerId === pointerState.current.pointerId) {
      event.currentTarget.releasePointerCapture(event.pointerId)
    }
    pointerState.current = {
      isScrubbing: false,
      pointerId: -1,
      startX: 0,
      startValue: 0,
    }
    if (wasScrubbing) {
      document.body.style.cursor = ""
      document.body.style.userSelect = ""
      clearOverrideVariant()
    }
    if (rafRef.current !== null) {
      window.cancelAnimationFrame(rafRef.current)
      rafRef.current = null
    }
    if (pendingValue.current !== null) {
      onValueChange?.(clampValue(pendingValue.current))
      pendingValue.current = null
    }
  }

  const handleKeyDown = (event: React.KeyboardEvent<HTMLInputElement>) => {
    onKeyDown?.(event)
    if (event.defaultPrevented) return
    if (!event.shiftKey) return
    if (event.key !== "ArrowUp" && event.key !== "ArrowDown") return
    if (props.disabled || props.readOnly) return
    if (type !== "number") return
    event.preventDefault()
    const direction = event.key === "ArrowUp" ? 1 : -1
    if (isScrubbableNumber) {
      const nextValue = Number.parseFloat(event.currentTarget.value)
      const baseValue = Number.isFinite(nextValue) ? nextValue : 0
      onValueChange?.(clampValue(baseValue + direction * stepValue * 10))
      return
    }
    if (direction > 0) {
      event.currentTarget.stepUp(10)
    } else {
      event.currentTarget.stepDown(10)
    }
    event.currentTarget.dispatchEvent(new Event("input", { bubbles: true }))
  }

  return (
    <Input
      data-slot="input-group-control"
      className={cn(
        "flex-1 rounded-none border-0 bg-transparent shadow-none focus-visible:ring-0 dark:bg-transparent",
        isScrubbableNumber && "cursor-ew-resize",
        className
      )}
      type={type}
      min={min}
      max={max}
      step={step}
      onChange={(event) => {
        if (isScrubbableNumber) {
          const next = Number.parseFloat(event.target.value)
          onValueChange?.(clampValue(Number.isFinite(next) ? next : 0))
        }
        onChange?.(event)
      }}
      onPointerDown={beginScrub}
      onPointerMove={updateScrub}
      onPointerUp={endScrub}
      onPointerCancel={(event) => {
        onPointerCancel?.(event)
        endScrub(event)
      }}
      onPointerLeave={(event) => {
        onPointerLeave?.(event)
        if (pointerState.current.isScrubbing) {
          endScrub(event)
        }
      }}
      onKeyDown={handleKeyDown}
      {...props}
    />
  )
}

function InputGroupTextarea({
  className,
  ...props
}: React.ComponentProps<"textarea">) {
  return (
    <Textarea
      data-slot="input-group-control"
      className={cn(
        "flex-1 resize-none rounded-none border-0 bg-transparent py-3 shadow-none focus-visible:ring-0 dark:bg-transparent",
        className
      )}
      {...props}
    />
  )
}

export {
  InputGroup,
  InputGroupAddon,
  InputGroupButton,
  InputGroupText,
  InputGroupInput,
  InputGroupTextarea,
}
