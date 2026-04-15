import { TranslatePane } from './components/TranslatePane';
import { LookupPane } from './components/LookupPane';

export default function App() {
  return (
    <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', height: '100vh' }}>
      <div style={{ borderRight: '1px solid #ddd', overflowY: 'auto' }}>
        <TranslatePane />
      </div>
      <div style={{ overflowY: 'auto' }}>
        <LookupPane />
      </div>
    </div>
  );
}
