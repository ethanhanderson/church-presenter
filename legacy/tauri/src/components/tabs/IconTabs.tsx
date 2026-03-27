import * as React from "react";

import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui/tooltip";
import { cn } from "@/lib/utils";

export interface IconTabItem {
  value: string;
  label: string;
  icon?: React.ReactNode;
  tooltip?: string;
  srOnly?: string;
}

interface IconTabsProps {
  value: string;
  onValueChange: (value: string) => void;
  tabs: IconTabItem[];
  className?: string;
  listClassName?: string;
  triggerClassName?: string;
  showTooltips?: boolean;
}

export function IconTabs({
  value,
  onValueChange,
  tabs,
  className,
  listClassName,
  triggerClassName,
  showTooltips = false,
}: IconTabsProps) {
  return (
    <Tabs
      className={cn("flex justify-start", className)}
      value={value}
      onValueChange={onValueChange}
    >
      <TabsList className={cn("inline-flex justify-start p-1", listClassName)}>
        {tabs.map((tab) => {
          const trigger = (
            <TabsTrigger
              key={tab.value}
              className={cn("py-3", triggerClassName)}
              value={tab.value}
            >
              {tab.icon}
              {tab.srOnly && <span className="sr-only">{tab.srOnly}</span>}
            </TabsTrigger>
          );

          if (showTooltips && tab.tooltip) {
            return (
              <TooltipProvider key={tab.value} delayDuration={0}>
                <Tooltip>
                  <TooltipTrigger asChild>
                    <span>{trigger}</span>
                  </TooltipTrigger>
                  <TooltipContent className="px-2 py-1 text-xs">
                    {tab.tooltip}
                  </TooltipContent>
                </Tooltip>
              </TooltipProvider>
            );
          }

          return trigger;
        })}
      </TabsList>
    </Tabs>
  );
}
