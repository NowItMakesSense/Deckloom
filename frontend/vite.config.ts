import react from "@vitejs/plugin-react";
import { fileURLToPath } from "node:url";
import { configDefaults, defineConfig } from "vitest/config";

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: { "@": fileURLToPath(new URL("./src", import.meta.url)) },
  },
  // Ambiente E2E (T-121): so em `npm run e2e`, repassa /api ao host de teste.
  server: process.env.E2E_API_URL
    ? { proxy: { "/api": { target: process.env.E2E_API_URL } } }
    : undefined,
  test: {
    exclude: [...configDefaults.exclude, "e2e/**"],
    environment: "jsdom",
    globals: false,
    // QA-11: o timeout sob carga vinha de CPU saturada por excesso de workers (suites paralelas x CPUs-1 cada), nao de
    // lentidao dos testes. Em CI limita a 2; localmente vale o padrao do vitest (ou VITEST_MAX_WORKERS).
    maxWorkers: process.env.CI ? 2 : undefined,
    css: { include: [/.+/] },
    setupFiles: ["./src/shared/testing/setupTests.ts"],
    coverage: {
      provider: "v8",
      include: ["src/**/*.{ts,tsx}"],
      exclude: ["src/**/*.test.{ts,tsx}", "src/shared/testing/**", "src/main.tsx"],
      thresholds: { lines: 80, statements: 80, functions: 80, branches: 80 },
    },
  },
});
