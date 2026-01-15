import React from 'react';
import ReactDOM from 'react-dom/client';
import { enableMapSet } from 'immer';
import App from './App';
import OutputApp from './output/OutputApp';
import './index.css';

// Zustand uses Immer, and our stores include Maps (e.g. pending media).
// Enable Map/Set support to avoid runtime errors when Immer drafts proxy Maps.
enableMapSet();

const isTauri = typeof window !== 'undefined' && '__TAURI__' in window;

if (isTauri) {
  void import('@tauri-apps/plugin-log').then(
    ({ warn, debug, trace, info, error }) => {
      const formatArgs = (args: unknown[]) =>
        args
          .map((arg) => {
            if (typeof arg === 'string') {
              return arg;
            }
            try {
              return JSON.stringify(arg);
            } catch {
              return String(arg);
            }
          })
          .join(' ');
      const formatError = (err: unknown) => {
        if (err instanceof Error) {
          return err.stack ?? err.message;
        }
        if (typeof err === 'string') {
          return err;
        }
        try {
          return JSON.stringify(err);
        } catch {
          return String(err);
        }
      };

      const forwardConsole = (
        fnName: 'log' | 'debug' | 'info' | 'warn' | 'error',
        logger: (message: string) => Promise<void>
      ) => {
        const original = console[fnName].bind(console);
        console[fnName] = (...args: unknown[]) => {
          original(...args);
          void logger(formatArgs(args));
        };
      };

      forwardConsole('log', trace);
      forwardConsole('debug', debug);
      forwardConsole('info', info);
      forwardConsole('warn', warn);
      forwardConsole('error', error);

      window.addEventListener('error', (event) => {
        void error(
          formatError(event.error ?? event.message ?? 'Unknown window error')
        );
      });

      window.addEventListener('unhandledrejection', (event) => {
        void error(`Unhandled rejection: ${formatError(event.reason)}`);
      });
    }
  );
}

// Determine which app to render based on URL path
const isOutputWindow = window.location.pathname === '/output';

ReactDOM.createRoot(document.getElementById('root') as HTMLElement).render(
  <React.StrictMode>
    {isOutputWindow ? <OutputApp /> : <App />}
  </React.StrictMode>
);
