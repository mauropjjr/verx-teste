import { fileURLToPath } from "node:url";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";

export default defineConfig({
  plugins: [react()],
  resolve: {
    tsconfigPaths: true,
    alias: {
      // `server-only` lança erro fora do bundle de servidor do Next; nos testes vira um módulo vazio.
      "server-only": fileURLToPath(new URL("./testes/vazio.ts", import.meta.url)),
    },
  },
  test: {
    environment: "jsdom",
    include: ["src/**/*.test.{ts,tsx}"],
    setupFiles: ["./testes/configuracao.ts"],
    restoreMocks: true,
    unstubGlobals: true,
    unstubEnvs: true,
    // Testes de componente com user-event digitando campo a campo passam de 5 s com a máquina carregada.
    testTimeout: 15_000,
  },
});
