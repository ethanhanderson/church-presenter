/**
 * TopTabNav - Top navigation tabs for main pages
 */

import { AppPageTabs } from '@/components/AppPageTabs';
import type { AppPage } from '@/lib/stores';

interface TopTabNavProps {
  value: AppPage;
  onChange: (value: AppPage) => void;
}

export function TopTabNav({ value, onChange }: TopTabNavProps) {
  return (
    <div className="border-b bg-background py-2 pl-2">
      <AppPageTabs value={value} onChange={onChange} />
    </div>
  );
}
