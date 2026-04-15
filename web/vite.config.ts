import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  build: {
    outDir: '../src/TechTrans.Wpf/Web/dist',
    emptyOutDir: true,
  },
  base: '/',
});
