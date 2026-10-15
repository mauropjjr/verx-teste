import type { NextConfig } from "next";

// Portal BFF (design D12): toda página lê o cookie de sessão e dados financeiros do usuário, então nada é
// pré-renderizado nem cacheado no servidor — sem Cache Components; cada requisição é dinâmica.
const nextConfig: NextConfig = {
  // Imagem Docker enxuta (design D14): servidor Node autocontido em .next/standalone.
  output: "standalone",
  poweredByHeader: false,
  turbopack: {
    rules: {
      "*.css": {
        loaders: ["@tailwindcss/turbopack"],
        as: "*.css",
      },
    },
  },
};

export default nextConfig;
