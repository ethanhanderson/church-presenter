import { useEffect, useRef, useState } from 'react';

interface ScaledWebLayerProps {
  url: string;
  zoom: number;
  baseWidth: number;
  baseHeight: number;
  interactive?: boolean;
  title?: string;
  className?: string;
}

export function ScaledWebLayer({
  url,
  zoom,
  baseWidth,
  baseHeight,
  interactive = false,
  title = 'Web content',
  className,
}: ScaledWebLayerProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const [scale, setScale] = useState(1);
  const safeWidth = Math.max(1, baseWidth);
  const safeHeight = Math.max(1, baseHeight);

  useEffect(() => {
    if (!containerRef.current) return;

    const updateScale = (width: number, height: number) => {
      if (!width || !height) return;
      const nextScale = Math.min(width / safeWidth, height / safeHeight);
      setScale((prev) => (Math.abs(prev - nextScale) < 0.001 ? prev : nextScale));
    };

    const observer = new ResizeObserver((entries) => {
      for (const entry of entries) {
        updateScale(entry.contentRect.width, entry.contentRect.height);
      }
    });

    observer.observe(containerRef.current);
    updateScale(containerRef.current.clientWidth, containerRef.current.clientHeight);

    return () => observer.disconnect();
  }, [safeWidth, safeHeight]);

  return (
    <div
      ref={containerRef}
      className={className ? `relative overflow-hidden ${className}` : 'relative overflow-hidden'}
      style={{ pointerEvents: interactive ? 'auto' : 'none' }}
    >
      <div
        className="absolute top-0 left-0 origin-top-left"
        style={{
          width: `${safeWidth}px`,
          height: `${safeHeight}px`,
          transform: `scale(${scale})`,
        }}
      >
        <iframe
          src={url}
          className="w-full h-full border-0 bg-white"
          style={{
            transform: `scale(${zoom})`,
            transformOrigin: 'top left',
            width: `${100 / zoom}%`,
            height: `${100 / zoom}%`,
            pointerEvents: interactive ? 'auto' : 'none',
          }}
          title={title}
          sandbox="allow-scripts allow-same-origin"
          tabIndex={interactive ? 0 : -1}
        />
      </div>
    </div>
  );
}
