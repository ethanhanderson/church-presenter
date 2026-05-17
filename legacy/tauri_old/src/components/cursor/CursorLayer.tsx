import { useEffect } from 'react';
import { CustomCursor } from '@yhattav/react-component-cursor';
import { useCursor } from './CursorProvider';
import { getCursorComponent } from './CursorVariants';

export function CursorLayer() {
  const { enabled, effectiveVariant } = useCursor();

  useEffect(() => {
    // #region agent log
    fetch('http://127.0.0.1:7247/ingest/b87235aa-ab93-4b87-9657-fe5221616d91',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({sessionId:'debug-session',runId:'pre-fix',hypothesisId:'B',location:'CursorLayer.tsx:effect',message:'cursor_state',data:{enabled,variant:effectiveVariant},timestamp:Date.now()})}).catch(()=>{});
    // #endregion
  }, [enabled, effectiveVariant]);

  if (!enabled) return null;

  return (
    <CustomCursor
      className="app-cursor-layer"
      smoothFactor={1}
      showDevIndicator={false}
      zIndex={9999}
    >
      {getCursorComponent(effectiveVariant)}
    </CustomCursor>
  );
}
