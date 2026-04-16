import { useState, useEffect, useRef } from 'react';
import { sendToHost, onHostMessage } from '../bridge';

export function TranslatePane() {
  const [direction, setDirection] = useState<'en-ko' | 'ko-en'>('en-ko');
  const [sourceText, setSourceText] = useState('');
  const [result, setResult] = useState('');
  const [isStreaming, setIsStreaming] = useState(false);
  const resultRef = useRef('');

  useEffect(() => {
    const cleanup = onHostMessage((msg) => {
      if (msg.type === 'translation.chunk') {
        resultRef.current += (msg.payload as string);
        setResult(resultRef.current);
      }
      if (msg.type === 'translation.done') {
        setIsStreaming(false);
      }
      if (msg.type === 'translation.error') {
        setIsStreaming(false);
        setResult(`오류: ${msg.payload}`);
      }
    });
    return cleanup;  // StrictMode 이중 등록 방지
  }, []);

  const handleTranslate = () => {
    if (!sourceText.trim() || isStreaming) return;
    resultRef.current = '';
    setResult('');
    setIsStreaming(true);
    sendToHost('translate', {
      text: sourceText,
      source_lang: direction === 'en-ko' ? 'en' : 'ko',
      target_lang: direction === 'en-ko' ? 'ko' : 'en',
    });
  };

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 12, padding: 16 }}>
      <h2>번역</h2>
      <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
        <span>{direction === 'en-ko' ? '영어 → 한국어' : '한국어 → 영어'}</span>
        <button onClick={() => setDirection(d => d === 'en-ko' ? 'ko-en' : 'en-ko')}>
          ⇌ 방향 전환
        </button>
      </div>
      <textarea
        value={sourceText}
        onChange={e => setSourceText(e.target.value)}
        placeholder={direction === 'en-ko' ? 'Enter English text...' : '한국어 텍스트 입력...'}
        rows={8}
        style={{ width: '100%', padding: 8, fontSize: 14, resize: 'vertical' }}
      />
      <button
        onClick={handleTranslate}
        disabled={isStreaming || !sourceText.trim()}
        style={{ padding: '8px 24px', fontSize: 14, cursor: 'pointer' }}
      >
        {isStreaming ? '번역 중...' : '번역'}
      </button>
      <div style={{
        minHeight: 200, padding: 12, background: '#f8f8f8',
        borderRadius: 4, whiteSpace: 'pre-wrap', fontSize: 14
      }}>
        {result || <span style={{ color: '#bbb' }}>번역 결과가 여기 표시됩니다.</span>}
      </div>
    </div>
  );
}
