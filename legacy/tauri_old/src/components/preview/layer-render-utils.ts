import type { Layer, LayerEffect, LayerStroke, TextStyle } from '@/lib/models';
import { resolveBlendMode } from '@/lib/models';
import { toRgba } from '@/lib/color-utils';

export const mergeTextStyle = (style?: Partial<TextStyle>): Partial<TextStyle> | undefined => {
  if (!style) return undefined;
  return {
    ...style,
    font: { ...style.font },
    shadow: { ...style.shadow },
    outline: { ...style.outline },
  };
};

export function getLayerEffectStyle(effects?: LayerEffect[]): React.CSSProperties | undefined {
  if (!effects || effects.length === 0) return undefined;
  const filterParts: string[] = [];
  for (const effect of effects) {
    if (effect.enabled === false) continue;
    if (effect.type === 'layer-blur') {
      filterParts.push(`blur(${effect.radius}px)`);
    }
    if (effect.type === 'drop-shadow') {
      const color = toRgba(effect.color, effect.opacity);
      filterParts.push(
        `drop-shadow(${effect.offsetX}px ${effect.offsetY}px ${effect.blur}px ${color})`
      );
    }
  }
  if (filterParts.length === 0) return undefined;
  return { filter: filterParts.join(' ') };
}

export function getLayerContentStyle(layer: Layer): React.CSSProperties {
  const baseCornerRadius = layer.transform.cornerRadius ?? 0;
  const cornerRadii = {
    topLeft: layer.transform.cornerRadiusTopLeft ?? baseCornerRadius,
    topRight: layer.transform.cornerRadiusTopRight ?? baseCornerRadius,
    bottomRight: layer.transform.cornerRadiusBottomRight ?? baseCornerRadius,
    bottomLeft: layer.transform.cornerRadiusBottomLeft ?? baseCornerRadius,
  };
  const borderRadius = `${cornerRadii.topLeft}px ${cornerRadii.topRight}px ${cornerRadii.bottomRight}px ${cornerRadii.bottomLeft}px`;
  const resolvedBlendMode = resolveBlendMode(layer.blendMode);
  const blendStyle =
    resolvedBlendMode === 'normal' ? undefined : { mixBlendMode: resolvedBlendMode };
  const effectStyle = getLayerEffectStyle(layer.effects);
  return {
    borderRadius,
    overflow: layer.transform.clipContent ? 'hidden' : 'visible',
    ...(blendStyle ?? {}),
    ...(effectStyle ?? {}),
  };
}

export const resolveLayerFills = (layer: Layer) => {
  const hasExplicitFills = layer.fills !== undefined;
  const fills = layer.fills?.filter((fill) => fill.enabled !== false) ?? [];
  if (fills.length > 0) return fills;
  if (hasExplicitFills) return [];
  if (layer.type === 'shape') {
    return [
      {
        id: 'legacy-fill',
        color: layer.style?.fill ?? '#3b82f6',
        opacity: layer.style?.fillOpacity ?? 1,
      },
    ];
  }
  if (layer.type === 'text') {
    return [
      {
        id: 'legacy-fill',
        color: layer.style?.color ?? '#ffffff',
        opacity: 1,
      },
    ];
  }
  return [];
};

export const resolveLayerStrokes = (layer: Layer) => {
  const enabledStrokes = layer.strokes?.filter((stroke) => stroke.enabled !== false) ?? [];
  if (enabledStrokes.length > 0) return enabledStrokes;
  if (layer.type === 'shape') {
    const usesLegacyStroke = layer.strokes === undefined;
    if (usesLegacyStroke) {
      return [
        {
          id: 'legacy-stroke',
          color: layer.style?.stroke ?? '#1d4ed8',
          opacity: layer.style?.strokeOpacity ?? 1,
          width: layer.style?.strokeWidth ?? 2,
          position: 'inside',
          sides: 'all',
        },
      ] as LayerStroke[];
    }
  }
  return [];
};

export const getPrimaryStroke = (strokes: LayerStroke[]) => {
  const enabled = strokes.filter((stroke) => stroke.enabled !== false);
  return enabled[enabled.length - 1] ?? enabled[0] ?? strokes[0] ?? null;
};

export const resolveStrokeSides = (stroke: LayerStroke) => {
  const sides = stroke.sides ?? 'all';
  if (sides === 'custom') {
    return stroke.customSides ?? {
      top: true,
      right: true,
      bottom: true,
      left: true,
    };
  }
  return {
    top: sides === 'all' || sides === 'top',
    right: sides === 'all' || sides === 'right',
    bottom: sides === 'all' || sides === 'bottom',
    left: sides === 'all' || sides === 'left',
  };
};

export const getStrokeInset = (stroke: LayerStroke) => {
  const width = Math.max(0, stroke.width ?? 0);
  switch (stroke.position ?? 'inside') {
    case 'outside':
      return -width;
    case 'center':
      return -width / 2;
    default:
      return 0;
  }
};

export function getTransformOrigin(style?: Partial<TextStyle>): string {
  const alignment = style?.alignment || 'center';
  const verticalAlignment = style?.verticalAlignment || 'middle';

  const horizontal = {
    left: 'left',
    center: 'center',
    right: 'right',
  }[alignment];

  const vertical = {
    top: 'top',
    middle: 'center',
    bottom: 'bottom',
  }[verticalAlignment];

  return `${horizontal} ${vertical}`;
}

export function getTextStyleSignature(style?: Partial<TextStyle>, stroke?: LayerStroke | null): string {
  if (!style) return '';
  const font = style.font;
  return [
    style.alignment ?? '',
    style.verticalAlignment ?? '',
    style.color ?? '',
    font?.family ?? '',
    font?.size ?? '',
    font?.weight ?? '',
    font?.italic ?? '',
    font?.lineHeight ?? '',
    font?.letterSpacing ?? '',
    stroke?.color ?? '',
    stroke?.opacity ?? '',
    stroke?.width ?? '',
  ].join('|');
}

export function getTextContainerStyle(style?: Partial<TextStyle>): React.CSSProperties {
  if (!style) return {};

  const alignItems = {
    top: 'flex-start',
    middle: 'center',
    bottom: 'flex-end',
  }[style.verticalAlignment || 'middle'];

  const justifyContent = {
    left: 'flex-start',
    center: 'center',
    right: 'flex-end',
  }[style.alignment || 'center'];

  return { alignItems, justifyContent };
}

export function getTextStyle(
  style?: Partial<TextStyle>,
  scale = 1,
  stroke?: LayerStroke | null
): React.CSSProperties {
  if (!style) return {};

  const css: React.CSSProperties = {
    color: style.color,
    textAlign: style.alignment,
  };

  if (style.font) {
    css.fontFamily = style.font.family;
    css.fontSize = style.font.size * scale;
    css.fontWeight = style.font.weight;
    css.fontStyle = style.font.italic ? 'italic' : 'normal';
    css.lineHeight = style.font.lineHeight;
    const letterSpacing = style.font.letterSpacing ?? 0;
    css.letterSpacing = Number.isFinite(letterSpacing) ? letterSpacing * scale : 0;
  }

  if (stroke) {
    css.WebkitTextStroke = `${stroke.width * scale}px ${toRgba(stroke.color, stroke.opacity)}`;
  }

  return css;
}
