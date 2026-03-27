import { cn } from '@/lib/utils';

interface ThumbnailCheckerboardProps {
  className?: string;
}

export function ThumbnailCheckerboard({ className }: ThumbnailCheckerboardProps) {
  return (
    <div
      className={cn(
        'absolute inset-0',
        'bg-[rgba(0,0,0,0.04)]',
        'bg-[linear-gradient(45deg,rgba(0,0,0,0.08)_25%,transparent_25%),linear-gradient(-45deg,rgba(0,0,0,0.08)_25%,transparent_25%),linear-gradient(45deg,transparent_75%,rgba(0,0,0,0.08)_75%),linear-gradient(-45deg,transparent_75%,rgba(0,0,0,0.08)_75%)]',
        'bg-size-[16px_16px]',
        'bg-position-[0_0,0_8px,8px_-8px,-8px_0]',
        'dark:bg-[rgba(255,255,255,0.04)]',
        'dark:bg-[linear-gradient(45deg,rgba(255,255,255,0.12)_25%,transparent_25%),linear-gradient(-45deg,rgba(255,255,255,0.12)_25%,transparent_25%),linear-gradient(45deg,transparent_75%,rgba(255,255,255,0.12)_75%),linear-gradient(-45deg,transparent_75%,rgba(255,255,255,0.12)_75%)]',
        className
      )}
    />
  );
}
