import { defineConfig } from 'vite';
import { resolve } from 'node:path';
import { createHash } from 'node:crypto';

export default defineConfig({
  base: '/player/',
  plugins: [{
    name: 'signage-offline-shell',
    generateBundle(_options, bundle) {
      const urls = ['/player/', ...Object.keys(bundle).filter(name => name.startsWith('assets/')).map(name => `/player/${name}`)];
      const version = createHash('sha256').update(JSON.stringify(urls)).digest('hex').slice(0, 16);
      const worker = bundle['sw.js'];
      if (worker?.type !== 'chunk') throw new Error('Missing service worker');
      worker.code = worker.code.replace('__PLAYER_SHELL_VERSION__', version)
        .replace(/(["'`])__PLAYER_SHELL_URLS__\1/, JSON.stringify(JSON.stringify(urls)));
      if (worker.code.includes('__PLAYER_SHELL_')) throw new Error('Unresolved service-worker shell metadata');
    }
  }],
  build: {
    outDir: resolve(import.meta.dirname, '../Signage.Web/wwwroot/player'),
    emptyOutDir: true,
    rollupOptions: {
      input: {
        index: resolve(import.meta.dirname, 'index.html'),
        sw: resolve(import.meta.dirname, 'src/sw.ts')
      },
      output: {
        entryFileNames: chunk => chunk.name === 'sw' ? 'sw.js' : 'assets/[name]-[hash].js',
        chunkFileNames: 'assets/[name]-[hash].js',
        assetFileNames: 'assets/[name]-[hash][extname]'
      }
    }
  }
});
