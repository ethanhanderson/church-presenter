import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import type { CursorVariant } from './types';

interface CursorContextValue {
  enabled: boolean;
  hoverVariant: CursorVariant | null;
  overrideVariant: CursorVariant | null;
  effectiveVariant: CursorVariant;
  setHoverVariant: (variant: CursorVariant | null) => void;
  clearHoverVariant: () => void;
  setOverrideVariant: (variant: CursorVariant | null) => void;
  clearOverrideVariant: () => void;
}

const CursorContext = createContext<CursorContextValue>({
  enabled: false,
  hoverVariant: null,
  overrideVariant: null,
  effectiveVariant: 'default',
  setHoverVariant: () => undefined,
  clearHoverVariant: () => undefined,
  setOverrideVariant: () => undefined,
  clearOverrideVariant: () => undefined,
});

const getInitialEnabled = () => {
  if (typeof window === 'undefined') return false;
  const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  const finePointer = window.matchMedia('(pointer: fine)').matches;
  return !reduceMotion && finePointer;
};

export function CursorProvider({ children }: { children: React.ReactNode }) {
  const [hoverVariant, setHoverVariantState] = useState<CursorVariant | null>(null);
  const [overrideVariant, setOverrideVariantState] = useState<CursorVariant | null>(null);
  const [enabled, setEnabled] = useState(getInitialEnabled);

  useEffect(() => {
    if (typeof document === 'undefined') return;
    if (enabled) {
      document.body.dataset.customCursor = 'on';
    } else {
      delete document.body.dataset.customCursor;
    }
  }, [enabled]);

  useEffect(() => {
    // #region agent log
    fetch('http://127.0.0.1:7247/ingest/b87235aa-ab93-4b87-9657-fe5221616d91',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({sessionId:'debug-session',runId:'pre-fix',hypothesisId:'B',location:'CursorProvider.tsx:enabled',message:'cursor_enabled_change',data:{enabled},timestamp:Date.now()})}).catch(()=>{});
    // #endregion
  }, [enabled]);

  useEffect(() => {
    if (typeof window === 'undefined') return;
    const reduceMotionQuery = window.matchMedia('(prefers-reduced-motion: reduce)');
    const finePointerQuery = window.matchMedia('(pointer: fine)');
    const updateEnabled = () => {
      setEnabled(!reduceMotionQuery.matches && finePointerQuery.matches);
    };
    updateEnabled();
    reduceMotionQuery.addEventListener('change', updateEnabled);
    finePointerQuery.addEventListener('change', updateEnabled);
    return () => {
      reduceMotionQuery.removeEventListener('change', updateEnabled);
      finePointerQuery.removeEventListener('change', updateEnabled);
    };
  }, []);

  const setHoverVariant = useCallback((variant: CursorVariant | null) => {
    setHoverVariantState((current) => (current === variant ? current : variant));
  }, []);

  const setOverrideVariant = useCallback((variant: CursorVariant | null) => {
    setOverrideVariantState((current) => (current === variant ? current : variant));
  }, []);

  const clearHoverVariant = useCallback(() => {
    setHoverVariantState((current) => (current === null ? current : null));
  }, []);

  const clearOverrideVariant = useCallback(() => {
    setOverrideVariantState((current) => (current === null ? current : null));
  }, []);

  const effectiveVariant = overrideVariant ?? hoverVariant ?? 'default';

  const value = useMemo<CursorContextValue>(
    () => ({
      enabled,
      hoverVariant,
      overrideVariant,
      effectiveVariant,
      setHoverVariant,
      clearHoverVariant,
      setOverrideVariant,
      clearOverrideVariant,
    }),
    [
      enabled,
      hoverVariant,
      overrideVariant,
      effectiveVariant,
      setHoverVariant,
      clearHoverVariant,
      setOverrideVariant,
      clearOverrideVariant,
    ]
  );

  return <CursorContext.Provider value={value}>{children}</CursorContext.Provider>;
}

export function useCursor() {
  return useContext(CursorContext);
}

export function useCursorHover(variant: CursorVariant | null) {
  const { enabled, setHoverVariant, clearHoverVariant } = useCursor();

  const onPointerEnter = useCallback(() => {
    if (!enabled || !variant) return;
    setHoverVariant(variant);
  }, [enabled, setHoverVariant, variant]);

  const onPointerLeave = useCallback(() => {
    if (!enabled) return;
    clearHoverVariant();
  }, [enabled, clearHoverVariant]);

  return { onPointerEnter, onPointerLeave };
}
