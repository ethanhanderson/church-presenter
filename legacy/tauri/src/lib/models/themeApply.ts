import { v4 as uuid } from 'uuid';
import type {
  Layer,
  LayerEffect,
  LayerFill,
  LayerStroke,
  LayerTransform,
  Slide,
  TextLayer,
  TextStyle,
  ThemeTemplateSlide,
} from './types';
import { createTextLayer } from './defaults';

export type ThemeScaleMode = 'none' | 'fit';

export interface ThemeApplyOptions {
  scaleMode?: ThemeScaleMode;
  sourceSize: { width: number; height: number };
  targetSize: { width: number; height: number };
}

const scaleTransform = (transform: LayerTransform, scaleX: number, scaleY: number): LayerTransform => {
  const radiusScale = Math.min(scaleX, scaleY);
  return {
    ...transform,
    x: transform.x * scaleX,
    y: transform.y * scaleY,
    width: transform.width * scaleX,
    height: transform.height * scaleY,
    cornerRadius: (transform.cornerRadius ?? 0) * radiusScale,
    cornerRadiusTopLeft: transform.cornerRadiusTopLeft
      ? transform.cornerRadiusTopLeft * radiusScale
      : undefined,
    cornerRadiusTopRight: transform.cornerRadiusTopRight
      ? transform.cornerRadiusTopRight * radiusScale
      : undefined,
    cornerRadiusBottomRight: transform.cornerRadiusBottomRight
      ? transform.cornerRadiusBottomRight * radiusScale
      : undefined,
    cornerRadiusBottomLeft: transform.cornerRadiusBottomLeft
      ? transform.cornerRadiusBottomLeft * radiusScale
      : undefined,
  };
};

const scaleTextStyle = (style: TextStyle, scale: number): TextStyle => ({
  ...style,
  font: {
    ...style.font,
    size: style.font.size * scale,
    letterSpacing: (style.font.letterSpacing ?? 0) * scale,
  },
  shadow: style.shadow
    ? {
      ...style.shadow,
      offsetX: style.shadow.offsetX * scale,
      offsetY: style.shadow.offsetY * scale,
      blur: style.shadow.blur * scale,
    }
    : style.shadow,
  outline: style.outline
    ? {
      ...style.outline,
      width: style.outline.width * scale,
    }
    : style.outline,
});

const cloneLayerFills = (fills?: LayerFill[]) =>
  fills?.map((fill) => ({
    ...fill,
    id: uuid(),
  }));

const cloneLayerStrokes = (strokes?: LayerStroke[], scale = 1) =>
  strokes?.map((stroke) => ({
    ...stroke,
    id: uuid(),
    width: (stroke.width ?? 0) * scale,
  }));

const cloneLayerEffects = (effects?: LayerEffect[], scale = 1) =>
  effects?.map((effect) => {
    if (effect.type === 'layer-blur') {
      return { ...effect, id: uuid(), radius: effect.radius * scale };
    }
    if (effect.type === 'drop-shadow') {
      return {
        ...effect,
        id: uuid(),
        offsetX: effect.offsetX * scale,
        offsetY: effect.offsetY * scale,
        blur: effect.blur * scale,
        spread: effect.spread !== undefined ? effect.spread * scale : effect.spread,
      };
    }
    return { ...effect, id: uuid() };
  });

export const getThemeSlideLayers = (themeSlide: ThemeTemplateSlide): Layer[] => {
  if (Array.isArray(themeSlide.layers) && themeSlide.layers.length > 0) {
    return themeSlide.layers;
  }
  const legacyLayers = themeSlide.textLayers ?? [];
  if (legacyLayers.length === 0) return [];
  return legacyLayers.map((layer, index) => {
    const nextLayer = createTextLayer(
      layer.content ?? layer.name?.trim() ?? `Text ${index + 1}`,
      {
        name: layer.name ?? `Text ${index + 1}`,
        transform: layer.transform,
        style: layer.style,
        fills: layer.fills,
        strokes: layer.strokes,
        textFit: layer.textFit,
        padding: layer.padding ?? 2,
      }
    );
    nextLayer.id = layer.id;
    return nextLayer;
  });
};

export const applyThemeSlideToSlideInPlace = (
  slide: Slide,
  themeSlide: ThemeTemplateSlide,
  options: ThemeApplyOptions
) => {
  const scaleMode = options.scaleMode ?? 'none';
  const scaleX = options.targetSize.width / options.sourceSize.width;
  const scaleY = options.targetSize.height / options.sourceSize.height;
  const uniformScale = Math.min(scaleX, scaleY);
  const offsetX =
    scaleMode === 'fit'
      ? (options.targetSize.width - options.sourceSize.width * uniformScale) / 2
      : 0;
  const offsetY =
    scaleMode === 'fit'
      ? (options.targetSize.height - options.sourceSize.height * uniformScale) / 2
      : 0;

  const mapTransform = (transform: LayerTransform) => {
    if (scaleMode !== 'fit') return transform;
    const scaled = scaleTransform(transform, uniformScale, uniformScale);
    return {
      ...scaled,
      x: scaled.x + offsetX,
      y: scaled.y + offsetY,
    };
  };

  const mapStyle = (style: TextStyle) => {
    if (scaleMode !== 'fit') return style;
    return scaleTextStyle(style, uniformScale);
  };

  slide.background = themeSlide.background;

  const themeLayers = getThemeSlideLayers(themeSlide);
  const targetTextLayers = slide.layers.filter(
    (layer): layer is TextLayer => layer.type === 'text'
  );
  const nextLayers: Layer[] = [];
  let textIndex = 0;
  const scaleValue = scaleMode === 'fit' ? uniformScale : 1;

  themeLayers.forEach((layer) => {
    if (layer.type === 'text') {
      const targetLayer = targetTextLayers[textIndex];
      const mappedTransform = mapTransform(layer.transform);
      const mappedStyle = layer.style ? mapStyle(layer.style as TextStyle) : layer.style;
      const mappedFills = cloneLayerFills(layer.fills);
      const mappedStrokes = cloneLayerStrokes(layer.strokes, scaleValue);
      const mappedEffects = cloneLayerEffects(layer.effects, scaleValue);
      const nextLayer: TextLayer = {
        ...layer,
        id: targetLayer?.id ?? uuid(),
        content: targetLayer?.content ?? layer.content ?? '',
        name: layer.name ?? targetLayer?.name ?? `Text ${textIndex + 1}`,
        transform: mappedTransform,
        style: mappedStyle,
        fills: mappedFills,
        strokes: mappedStrokes,
        effects: mappedEffects,
      };
      nextLayers.push(nextLayer);
      textIndex += 1;
      return;
    }

    const mappedTransform = mapTransform(layer.transform);
    const mappedEffects = cloneLayerEffects(layer.effects, scaleValue);
    const mappedStrokes = cloneLayerStrokes(layer.strokes, scaleValue);
    const mappedFills = cloneLayerFills(layer.fills);
    const nextLayer: Layer = {
      ...layer,
      id: uuid(),
      transform: mappedTransform,
      fills: mappedFills,
      strokes: mappedStrokes,
      effects: mappedEffects,
    };

    if (layer.type === 'shape') {
      nextLayer.style = scaleMode === 'fit'
        ? {
          ...layer.style,
          strokeWidth: layer.style.strokeWidth * uniformScale,
          cornerRadius: layer.style.cornerRadius * uniformScale,
        }
        : layer.style;
    }
    nextLayers.push(nextLayer);
  });

  slide.layers = nextLayers;
  slide.mediaCues = themeSlide.mediaCues ? themeSlide.mediaCues.map((cue) => ({ ...cue })) : [];
  slide.updatedAt = new Date().toISOString();
};

export const buildThemedSlidePreview = (
  slide: Slide,
  themeSlide: ThemeTemplateSlide,
  options: ThemeApplyOptions
) => {
  const cloned =
    typeof structuredClone === 'function'
      ? (structuredClone(slide) as Slide)
      : (JSON.parse(JSON.stringify(slide)) as Slide);
  applyThemeSlideToSlideInPlace(cloned, themeSlide, options);
  return cloned;
};
