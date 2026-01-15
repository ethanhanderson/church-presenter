import * as React from "react";

import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { cn } from "@/lib/utils";

export interface VerticalTabItem {
  value: string;
  label: string;
  icon?: React.ReactNode;
}

interface VerticalTabsProps {
  defaultValue: string;
  tabs: VerticalTabItem[];
  className?: string;
  listClassName?: string;
  triggerClassName?: string;
  children: React.ReactNode;
}

export function VerticalTabs({
  defaultValue,
  tabs,
  className,
  listClassName,
  triggerClassName,
  children,
}: VerticalTabsProps) {
  return (
    <Tabs
      className={cn("w-full flex-row items-start", className)}
      defaultValue={defaultValue}
      orientation="vertical"
    >
      <TabsList
        className={cn(
          "flex-col gap-1 self-start rounded-none bg-transparent px-1 py-0 text-foreground",
          listClassName
        )}
      >
        {tabs.map((tab) => (
          <TabsTrigger
            key={tab.value}
            className={cn(
              "after:-ms-1 relative w-full justify-start after:absolute after:inset-y-0 after:start-0 after:w-0.5 hover:bg-accent hover:text-foreground data-[state=active]:bg-transparent data-[state=active]:shadow-none data-[state=active]:hover:bg-accent data-[state=active]:after:bg-primary",
              triggerClassName
            )}
            value={tab.value}
          >
            {tab.icon}
            {tab.label}
          </TabsTrigger>
        ))}
      </TabsList>
      <div className="grow rounded-md border text-start">{children}</div>
    </Tabs>
  );
}
