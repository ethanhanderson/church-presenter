"use client"

import * as React from "react"
import * as LabelPrimitive from "@radix-ui/react-label"

import { cn } from "@/lib/utils"
import { useCursorHover } from "@/components/cursor"

function Label({
  className,
  ...props
}: React.ComponentProps<typeof LabelPrimitive.Root>) {
  const { onPointerEnter, onPointerLeave } = useCursorHover("pointer")

  return (
    <LabelPrimitive.Root
      data-slot="label"
      className={cn(
        "flex items-center gap-2 text-sm leading-none font-medium select-none group-data-[disabled=true]:pointer-events-none group-data-[disabled=true]:opacity-50 peer-disabled:cursor-not-allowed peer-disabled:opacity-50",
        className
      )}
      onPointerEnter={(event) => {
        onPointerEnter()
        props.onPointerEnter?.(event)
      }}
      onPointerLeave={(event) => {
        onPointerLeave()
        props.onPointerLeave?.(event)
      }}
      {...props}
    />
  )
}

export { Label }
