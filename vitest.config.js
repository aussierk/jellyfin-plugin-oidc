import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    environment: 'jsdom',
    include: ['Jellyfin.Plugin.OIDC/Configuration/**/*.test.js']
  }
});
