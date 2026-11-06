import { defineConfig, devices } from "@playwright/test";

// T-121: ambiente E2E. Sobe o host de teste (ASPNETCORE_ENVIRONMENT=E2E: persistencia em memoria, StubGoogleOidcServer,
// relogio falso) e o Vite, que repassa /api para o host. Nunca usa banco nem credenciais reais.
const WEB_ORIGIN = "http://localhost:5173";
const API_ORIGIN = "http://127.0.0.1:5099";

export default defineConfig({
  testDir: ".",
  testMatch: "**/*.spec.ts",
  fullyParallel: false,
  workers: 1,
  reporter: [["list"]],
  use: {
    baseURL: WEB_ORIGIN,
    trace: "retain-on-failure",
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
  webServer: [
    {
      command: "dotnet run --no-launch-profile --project ../backend/tests/Deckloom.Api.E2E.Host",
      cwd: "..",
      url: `${API_ORIGIN}/api/v1/csrf`,
      reuseExistingServer: !process.env.CI,
      timeout: 180_000,
      env: {
        ASPNETCORE_ENVIRONMENT: "E2E",
        ASPNETCORE_URLS: API_ORIGIN,
        Auth__PublicOrigin: WEB_ORIGIN,
      },
    },
    {
      command: "npm run dev -- --port 5173 --strictPort",
      cwd: "..",
      url: WEB_ORIGIN,
      reuseExistingServer: !process.env.CI,
      timeout: 60_000,
      env: { E2E_API_URL: API_ORIGIN },
    },
  ],
});
