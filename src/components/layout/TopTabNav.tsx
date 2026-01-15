/**
 * TopTabNav - Top navigation tabs for main pages
 */

import { MonitorPlay, Pencil, Rows3 } from "lucide-react";
import { AppPageTabs } from '@/components/tabs';
import type { AppPage } from '@/lib/stores';

interface TopTabNavProps {
  value: AppPage;
  onChange: (value: AppPage) => void;
}

const appPageTabs = [
  {
    value: "show",
    label: "Show",
    icon: (
      <MonitorPlay
        aria-hidden="true"
        className="-ms-0.5 me-1.5 opacity-60"
        size={16}
      />
    ),
  },
  {
    value: "edit",
    label: "Edit",
    icon: (
      <Pencil
        aria-hidden="true"
        className="-ms-0.5 me-1.5 opacity-60"
        size={16}
      />
    ),
  },
  {
    value: "reflow",
    label: "Reflow",
    icon: (
      <Rows3
        aria-hidden="true"
        className="-ms-0.5 me-1.5 opacity-60"
        size={16}
      />
    ),
  },
] as const;

export function TopTabNav({ value, onChange }: TopTabNavProps) {
  return (
    <div className="border-b bg-background py-2 pl-2">
      <AppPageTabs
        value={value}
        onValueChange={(next) => onChange(next as AppPage)}
        tabs={appPageTabs}
        listClassName="mb-0"
        showScrollBar={false}
      />
    </div>
  );
}
