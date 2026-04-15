import { useState, useEffect, useCallback, useRef } from 'react';
import { sendToHost, onHostMessage } from '../bridge';
import type { LookupRow } from '../bridge';

export function LookupPane() {
  const [q, setQ] = useState('');
  const [lang, setLang] = useState<'auto' | 'ko' | 'en'>('auto');
  const [rows, setRows] = useState<LookupRow[]>([]);
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const lastQ = useRef('');

  useEffect(() => {
    onHostMessage((msg) => {
      if (msg.type === 'lookup.result') {
        setRows(msg.payload as LookupRow[]);
      }
    });
  }, []);

  const handleInput = useCallback((value: string) => {
    setQ(value);
    if (debounceRef.current) clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => {
      if (value === lastQ.current) return;
      lastQ.current = value;
      sendToHost('lookup', { q: value, lang });
    }, 100);
  }, [lang]);

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 8, padding: 16 }}>
      <h2>용어 검색</h2>
      <div style={{ display: 'flex', gap: 8 }}>
        <input
          value={q}
          onChange={e => handleInput(e.target.value)}
          placeholder="한글 또는 영문 용어 입력..."
          style={{ flex: 1, padding: 8, fontSize: 14 }}
        />
        <select value={lang} onChange={e => setLang(e.target.value as typeof lang)}>
          <option value="auto">자동</option>
          <option value="ko">한국어</option>
          <option value="en">영어</option>
        </select>
      </div>
      <div style={{ overflowY: 'auto', maxHeight: 400 }}>
        {rows.map(row => (
          <div key={row.id} style={{ borderBottom: '1px solid #eee', padding: '8px 0' }}>
            <div>
              <strong>{row.termKo}</strong>
              {' / '}
              <span style={{ color: '#666' }}>{row.termEn}</span>
              {row.categoryName && (
                <span style={{ marginLeft: 8, fontSize: 12, color: '#999' }}>
                  [{row.categoryName}]
                </span>
              )}
            </div>
            <div style={{ fontSize: 13, marginTop: 4, color: '#444' }}>
              {row.definitionKo}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}
