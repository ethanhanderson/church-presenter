import { useEffect, useMemo, useRef, useState } from 'react';
import type { Layer, MediaLayer, ShapeLayer, TextLayer, VectorLayer, WebLayer } from '@/lib/models';
import { ScaledWebLayer } from '@/components/preview/ScaledWebLayer';
import { toRgba } from '@/lib/color-utils';
import {
  getStrokeInset,
  getPrimaryStroke,
  getTextContainerStyle,
  getTextStyle,
  getTextStyleSignature,
  getTransformOrigin,
  mergeTextStyle,
  resolveLayerFills,
  resolveStrokeSides,
  resolveLayerStrokes,
} from '@/components/preview/layer-render-utils';

export { getLayerContentStyle } from '@/components/preview/layer-render-utils';

export interface LayerContentProps {
  layer: Layer;
  textScale: number;
  renderMode: 'live' | 'thumbnail' | 'canvas';
}

export function LayerContent({ layer, textScale, renderMode }: LayerContentProps) {
  switch (layer.type) {
    case 'text':
      return <TextLayerContent layer={layer} textScale={textScale} />;
    case 'shape':
      return <ShapeLayerContent layer={layer} textScale={textScale} />;
    case 'media':
      return <MediaLayerContent layer={layer} />;
    case 'web':
      return renderMode === 'thumbnail' ? (
        <WebLayerThumbnail layer={layer} />
      ) : (
        <WebLayerContent layer={layer} interactive={renderMode === 'live'} />
      );
    case 'vector':
      return <VectorLayerContent layer={layer} />;
    default:
      return null;
  }
}

export function getLayerContainerStyle(
  layer: Layer,
  baseSize: { width: number; height: number }
): React.CSSProperties {
  return {
    left: `${(layer.transform.x / baseSize.width) * 100}%`,
    top: `${(layer.transform.y / baseSize.height) * 100}%`,
    width: `${(layer.transform.width / baseSize.width) * 100}%`,
    height: `${(layer.transform.height / baseSize.height) * 100}%`,
  };
}

export function getLayerTransformStyle(layer: Layer): React.CSSProperties {
  const flipX = layer.transform.flipX ? -1 : 1;
  const flipY = layer.transform.flipY ? -1 : 1;
  return {
    transform: `rotate(${layer.transform.rotation}deg) scale(${flipX}, ${flipY})`,
    opacity: layer.transform.opacity,
  };
}

function TextLayerContent({ layer, textScale }: { layer: TextLayer; textScale: number }) {
  const containerRef = useRef<HTMLDivElement>(null);
  const textRef = useRef<HTMLSpanElement>(null);
  const [fitScale, setFitScale] = useState(1);

  const textFit = layer.textFit || 'auto';
  const padding = layer.padding ?? 2;
  const resolvedTextFills = useMemo(() => resolveLayerFills(layer), [layer]);
  const textStyle = useMemo(() => {
    const merged = mergeTextStyle(layer.style) ?? {};
    const primary = resolvedTextFills[0];
    if (primary) {
      return { ...merged, color: toRgba(primary.color, primary.opacity) };
    }
    if (layer.fills !== undefined) {
      return { ...merged, color: 'transparent' };
    }
    return merged;
  }, [layer.style, layer.fills, resolvedTextFills]);
  const renderFills = useMemo(() => {
    if (resolvedTextFills.length > 0) return resolvedTextFills;
    if (layer.fills !== undefined) {
      return [{ id: 'empty-fill', color: 'transparent', opacity: 1, enabled: true }];
    }
    return resolvedTextFills;
  }, [layer.fills, resolvedTextFills]);
  const primaryStroke = useMemo(
    () => getPrimaryStroke(layer.strokes ?? []),
    [layer.strokes]
  );
  const textCssBase = useMemo(
    () => getTextStyle({ ...textStyle, color: undefined }, textScale, null),
    [textStyle, textScale]
  );
  const textCssWithStroke = useMemo(
    () => getTextStyle({ ...textStyle, color: undefined }, textScale, primaryStroke),
    [textStyle, textScale, primaryStroke]
  );
  const styleSignature = useMemo(
    () => getTextStyleSignature(textStyle, primaryStroke),
    [textStyle, primaryStroke]
  );

  useEffect(() => {
    if (textFit === 'auto') {
      setFitScale((prev) => (prev === 1 ? prev : 1));
      return;
    }

    const calculateFitScale = () => {
      if (!containerRef.current || !textRef.current) return;

      const container = containerRef.current;
      const text = textRef.current;
      const paddingPercent = padding / 100;
      const availableWidth = container.clientWidth * (1 - paddingPercent * 2);
      const availableHeight = container.clientHeight * (1 - paddingPercent * 2);

      text.style.transform = 'scale(1)';
      const textWidth = text.scrollWidth;
      const textHeight = text.scrollHeight;

      if (textWidth === 0 || textHeight === 0) {
        setFitScale((prev) => (prev === 1 ? prev : 1));
        return;
      }

      const scaleX = availableWidth / textWidth;
      const scaleY = availableHeight / textHeight;
      let newScale = Math.min(scaleX, scaleY);

      if (textFit === 'shrink') {
        newScale = Math.min(1, newScale);
      }

      newScale = Math.max(0.1, Math.min(5, newScale));
      setFitScale((prev) => (Math.abs(prev - newScale) < 0.001 ? prev : newScale));
    };

    calculateFitScale();

    const observer = new ResizeObserver(calculateFitScale);
    if (containerRef.current) {
      observer.observe(containerRef.current);
    }

    return () => observer.disconnect();
  }, [textFit, padding, layer.content, styleSignature, textScale]);

  const containerStyle: React.CSSProperties = {
    ...getTextContainerStyle(textStyle),
    padding: `${padding}%`,
    overflow: layer.transform.clipContent ? 'hidden' : 'visible',
  };
  const textWrapperStyle: React.CSSProperties = {
    transform: textFit !== 'auto' ? `scale(${fitScale})` : undefined,
    transformOrigin: getTransformOrigin(textStyle),
    display: 'grid',
    width: textFit !== 'auto' ? `${100 / fitScale}%` : undefined,
  };

  return (
    <div ref={containerRef} className="w-full h-full flex" style={containerStyle}>
      <div style={textWrapperStyle}>
        {renderFills
          .slice()
          .reverse()
          .map((fill, index, reversed) => {
            const isTop = index === reversed.length - 1;
            const fillColor = toRgba(fill.color, fill.opacity);
            return (
              <span
                key={fill.id}
                ref={isTop ? textRef : undefined}
                className="whitespace-pre-wrap"
                style={{
                  ...(isTop ? textCssWithStroke : textCssBase),
                  color: fillColor,
                  gridArea: '1 / 1',
                }}
              >
                {layer.content}
              </span>
            );
          })}
      </div>
    </div>
  );
}

function ShapeLayerContent({ layer, textScale }: { layer: ShapeLayer; textScale: number }) {
  const { shapeType, style } = layer;
  const fills = resolveLayerFills(layer);
  const strokes = resolveLayerStrokes(layer);
  const scale = Number.isFinite(textScale) ? textScale : 1;

  if (shapeType === 'line') {
    return (
      <svg className="w-full h-full" preserveAspectRatio="none">
        {strokes.map((stroke, index) => (
          <line
            key={`${stroke.id}-${index}`}
            x1="0"
            y1="50%"
            x2="100%"
            y2="50%"
            stroke={stroke.color}
            strokeWidth={(stroke.width ?? 0) * scale}
            strokeOpacity={stroke.opacity}
          />
        ))}
      </svg>
    );
  }

  if (shapeType === 'triangle') {
    return (
      <svg className="w-full h-full" viewBox="0 0 100 100" preserveAspectRatio="none">
        {fills.slice().reverse().map((fill, index) => (
          <polygon
            key={`${fill.id}-${index}`}
            points="50,0 100,100 0,100"
            fill={fill.color}
            fillOpacity={fill.opacity}
          />
        ))}
        {strokes.map((stroke, index) => (
          <polygon
            key={`${stroke.id}-${index}`}
            points="50,0 100,100 0,100"
            fill="none"
            stroke={stroke.color}
            strokeWidth={(stroke.width ?? 0) * scale}
            strokeOpacity={stroke.opacity}
          />
        ))}
      </svg>
    );
  }

  const borderRadius = shapeType === 'ellipse' ? '50%' : `${style.cornerRadius}px`;

  return (
    <div className="relative w-full h-full" style={{ borderRadius }}>
      {fills.slice().reverse().map((fill, index) => (
        <div
          key={`${fill.id}-${index}`}
          className="absolute inset-0"
          style={{
            backgroundColor: toRgba(fill.color, fill.opacity),
            borderRadius,
          }}
        />
      ))}
      {strokes.map((stroke, index) => {
        const strokeWidth = (stroke.width ?? 0) * scale;
        const inset = getStrokeInset({ ...stroke, width: strokeWidth });
        const sides = resolveStrokeSides(stroke);
        const radiusOffset = Math.abs(inset);
        const resolvedRadius =
          shapeType === 'ellipse' ? '50%' : `${Math.max(0, style.cornerRadius + radiusOffset)}px`;
        const strokeColor = toRgba(stroke.color, stroke.opacity);
        return (
          <div
            key={`${stroke.id}-${index}`}
            className="absolute pointer-events-none"
            style={{
              top: inset,
              left: inset,
              right: inset,
              bottom: inset,
              borderStyle: 'solid',
              borderColor: strokeColor,
              borderTopWidth: sides.top ? strokeWidth : 0,
              borderRightWidth: sides.right ? strokeWidth : 0,
              borderBottomWidth: sides.bottom ? strokeWidth : 0,
              borderLeftWidth: sides.left ? strokeWidth : 0,
              borderRadius: resolvedRadius,
            }}
          />
        );
      })}
    </div>
  );
}

function MediaLayerContent({ layer }: { layer: MediaLayer }) {
  const style: React.CSSProperties = {
    width: '100%',
    height: '100%',
    objectFit: layer.fit,
  };

  if (layer.mediaType === 'video') {
    return (
      <video
        src={layer.mediaId}
        style={style}
        loop={layer.loop}
        muted={layer.muted}
        autoPlay={layer.autoplay}
        playsInline
      />
    );
  }

  return <img src={layer.mediaId} alt="" style={style} draggable={false} />;
}

function WebLayerContent({ layer, interactive }: { layer: WebLayer; interactive: boolean }) {
  return (
    <div className="w-full h-full overflow-hidden bg-white">
      <iframe
        src={layer.url}
        className="w-full h-full"
        style={{
          transform: `scale(${layer.zoom})`,
          transformOrigin: '0 0',
          width: `${100 / layer.zoom}%`,
          height: `${100 / layer.zoom}%`,
          pointerEvents: interactive ? 'auto' : 'none',
        }}
        title="Web content"
        sandbox="allow-scripts allow-same-origin"
      />
    </div>
  );
}

function WebLayerThumbnail({ layer }: { layer: WebLayer }) {
  return (
    <ScaledWebLayer
      url={layer.url}
      zoom={layer.zoom}
      baseWidth={layer.transform.width}
      baseHeight={layer.transform.height}
      interactive={false}
      title="Web content thumbnail"
      className="w-full h-full bg-white"
    />
  );
}

function VectorLayerContent({ layer }: { layer: VectorLayer }) {
  const fills = resolveLayerFills(layer);
  const strokes = resolveLayerStrokes(layer);
  const viewBox = layer.viewBox ?? '0 0 100 100';
  const fillRule = layer.fillRule ?? 'nonzero';

  return (
    <svg className="w-full h-full" viewBox={viewBox} preserveAspectRatio="none">
      {fills.slice().reverse().map((fill, index) => (
        <path
          key={`${fill.id}-${index}`}
          d={layer.path}
          fill={fill.color}
          fillOpacity={fill.opacity}
          fillRule={fillRule}
        />
      ))}
      {strokes.map((stroke, index) => (
        <path
          key={`${stroke.id}-${index}`}
          d={layer.path}
          fill="none"
          stroke={stroke.color}
          strokeWidth={stroke.width}
          strokeOpacity={stroke.opacity}
          fillRule={fillRule}
        />
      ))}
    </svg>
  );
}

