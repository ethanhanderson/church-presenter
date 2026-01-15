/**
 * AppPageTabs - top navigation tabs for app pages
 */

import { MonitorPlay, Pencil, Rows3 } from "lucide-react";

import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui/tooltip";
import type { AppPage } from "@/lib/stores";

interface AppPageTabsProps {
  value: AppPage;
  onChange: (value: AppPage) => void;
}

export function AppPageTabs({ value, onChange }: AppPageTabsProps) {
  return (
    <Tabs
      className="flex justify-start"
      value={value}
      onValueChange={(next) => onChange(next as AppPage)}
    >
      <TabsList className="inline-flex justify-start p-1">
        <TooltipProvider delayDuration={0}>
          <Tooltip>
            <TooltipTrigger asChild>
              <span>
                <TabsTrigger className="py-3" value="show">
                  <MonitorPlay aria-hidden="true" size={16} />
                  <span className="sr-only">Show</span>
                </TabsTrigger>
              </span>
            </TooltipTrigger>
            <TooltipContent className="px-2 py-1 text-xs">
              Show
            </TooltipContent>
          </Tooltip>
        </TooltipProvider>
        <TooltipProvider delayDuration={0}>
          <Tooltip>
            <TooltipTrigger asChild>
              <span>
                <TabsTrigger className="py-3" value="edit">
                  <Pencil aria-hidden="true" size={16} />
                  <span className="sr-only">Edit</span>
                </TabsTrigger>
              </span>
            </TooltipTrigger>
            <TooltipContent className="px-2 py-1 text-xs">
              Edit
            </TooltipContent>
          </Tooltip>
        </TooltipProvider>
        <TooltipProvider delayDuration={0}>
          <Tooltip>
            <TooltipTrigger asChild>
              <span>
                <TabsTrigger className="py-3" value="reflow">
                  <Rows3 aria-hidden="true" size={16} />
                  <span className="sr-only">Reflow</span>
                </TabsTrigger>
              </span>
            </TooltipTrigger>
            <TooltipContent className="px-2 py-1 text-xs">
              Reflow
            </TooltipContent>
          </Tooltip>
        </TooltipProvider>
      </TabsList>
    </Tabs>
  );
}
