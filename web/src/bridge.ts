export type MessageType = 'translate' | 'lookup';

export interface WebEnvelope<T = unknown> {
  type: MessageType;
  payload: T;
}

export interface LookupPayload { q: string; lang?: 'auto' | 'ko' | 'en'; }

export interface TranslatePayload {
  text: string;
  source_lang: 'en' | 'ko';
  target_lang: 'en' | 'ko';
  category_slug?: string;
}

export interface LookupRow {
  id: string;
  termKo: string;
  termEn: string;
  definitionKo: string;
  categoryName?: string;
}

export function sendToHost<T>(type: MessageType, payload: T) {
  window.chrome?.webview?.postMessage(JSON.stringify({ type, payload }));
}

export function onHostMessage(handler: (msg: { type: string; payload: unknown }) => void): () => void {
  const listener = (e: MessageEvent) => {
    try { handler(JSON.parse(e.data)); } catch { /* ignore */ }
  };
  window.chrome?.webview?.addEventListener('message', listener);
  // cleanup 함수 반환 — useEffect의 return에서 호출
  return () => window.chrome?.webview?.removeEventListener?.('message', listener);
}

declare global {
  interface Window {
    chrome?: {
      webview?: {
        postMessage(msg: string): void;
        addEventListener(type: 'message', handler: (e: MessageEvent) => void): void;
        removeEventListener?(type: 'message', handler: (e: MessageEvent) => void): void;
      };
    };
  }
}
