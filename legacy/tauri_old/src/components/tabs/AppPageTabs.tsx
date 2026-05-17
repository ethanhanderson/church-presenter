import * as React from "react";

import * as ScrollAreaPrimitive from "@radix-ui/react-scroll-area";

import { Badge } from "@/components/ui/badge";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { cn } from "@/lib/utils";

export interface AppPageTabItem {
  value: string;
  label: string;
  icon?: React.ReactNode;
  badge?: React.ReactNode;
  badgeVariant?: React.ComponentProps<typeof Badge>["variant"];
  badgeClassName?: string;
  srOnly?: string;
  content?: React.ReactNode;
}

interface AppPageTabsProps {
  value?: string;
  defaultValue?: string;
  onValueChange?: (value: string) => void;
  tabs: AppPageTabItem[];
  className?: string;
  listClassName?: string;
  triggerClassName?: string;
  contentClassName?: string;
  badgeClassName?: string;
  showScrollBar?: boolean;
}

export function AppPageTabs({
  value,
  defaultValue,
  onValueChange,
  tabs,
  className,
  listClassName,
  triggerClassName,
  contentClassName = "p-4 pt-1 text-center text-muted-foreground text-xs",
  badgeClassName,
  showScrollBar = true,
}: AppPageTabsProps) {
  const tabsProps: React.ComponentProps<typeof Tabs> = {
    className: cn("w-full", className),
    ...(value !== undefined ? { value } : {}),
    ...(defaultValue !== undefined ? { defaultValue } : {}),
    ...(onValueChange ? { onValueChange } : {}),
  };

  return (
    <Tabs {...tabsProps}>
      <ScrollAreaPrimitive.Root className="relative w-full">
        <ScrollAreaPrimitive.Viewport className="w-full">
          <TabsList className={cn("mb-3", listClassName)}>
            {tabs.map((tab) => (
              <TabsTrigger
                key={tab.value}
                className={cn("group", triggerClassName)}
                value={tab.value}
              >
                {tab.icon}
                {tab.srOnly ? (
                  <span className="sr-only">{tab.srOnly}</span>
                ) : (
                  tab.label
                )}
                {tab.badge ? (
                  <Badge
                    className={cn(
                      "ms-1.5 transition-opacity group-data-[state=inactive]:opacity-50",
                      tab.badgeVariant === "secondary" &&
                        "min-w-5 bg-primary/15 px-1",
                      badgeClassName,
                      tab.badgeClassName
                    )}
                    variant={tab.badgeVariant}
                  >
                    {tab.badge}
                  </Badge>
                ) : null}
              </TabsTrigger>
            ))}
          </TabsList>
        </ScrollAreaPrimitive.Viewport>
        {showScrollBar ? (
          <ScrollAreaPrimitive.Scrollbar
            className="flex touch-none p-px transition-colors select-none h-2.5 flex-col border-t border-t-transparent"
            orientation="horizontal"
          >
            <ScrollAreaPrimitive.Thumb className="bg-border relative flex-1 rounded-full" />
          </ScrollAreaPrimitive.Scrollbar>
        ) : null}
        <ScrollAreaPrimitive.Corner />
      </ScrollAreaPrimitive.Root>
      {tabs
        .filter((tab) => tab.content !== undefined)
        .map((tab) => (
          <TabsContent
            key={tab.value}
            className={contentClassName}
            value={tab.value}
          >
            {tab.content}
          </TabsContent>
        ))}
    </Tabs>
  );
}
