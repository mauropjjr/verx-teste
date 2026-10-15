import "server-only";

// Endereços das APIs (no compose, os nomes internos dos serviços; fora dele, as portas publicadas).
// Lidos a cada chamada para que a mesma imagem sirva a qualquer ambiente.

export function urlIdentidade(): string {
  return process.env.IDENTIDADE_URL ?? "http://localhost:5000";
}

export function urlLancamentos(): string {
  return process.env.LANCAMENTOS_URL ?? "http://localhost:5001";
}

export function urlConsolidado(): string {
  return process.env.CONSOLIDADO_URL ?? "http://localhost:5002";
}

/**
 * Cookie de sessão com `Secure`: `COOKIE_SEGURO=true|false` explícito; sem a variável, `Secure` só em produção.
 * O compose local (http://localhost) usa `false`; atrás de HTTPS, `true`.
 */
export function cookieSeguro(): boolean {
  const valor = process.env.COOKIE_SEGURO;
  if (valor === "true" || valor === "false") {
    return valor === "true";
  }
  return process.env.NODE_ENV === "production";
}
