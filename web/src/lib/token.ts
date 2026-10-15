// Leitura do payload do JWT de acesso SEM validar a assinatura: serve só para decidir navegação (proxy) e
// exibição (nome, papéis). Quem valida o token de verdade (assinatura, emissor, audiência) são as APIs.

/** Nome do cookie httpOnly que guarda o token de acesso (design D12). */
export const NOME_COOKIE_SESSAO = "sessao";

export type PayloadToken = {
  sub?: string;
  name?: string;
  role?: string | string[];
  exp?: number;
};

export function lerPayloadToken(token: string): PayloadToken | null {
  const partes = token.split(".");
  if (partes.length !== 3) {
    return null;
  }
  try {
    const base64 = partes[1].replace(/-/g, "+").replace(/_/g, "/");
    const json = new TextDecoder().decode(Uint8Array.from(atob(base64), (c) => c.charCodeAt(0)));
    const payload: unknown = JSON.parse(json);
    return typeof payload === "object" && payload !== null ? (payload as PayloadToken) : null;
  } catch {
    return null;
  }
}

/** Token ilegível, sem `exp` ou com `exp` no passado conta como sessão inexistente. */
export function tokenValido(token: string | undefined, agora: Date = new Date()): boolean {
  if (!token) {
    return false;
  }
  const exp = lerPayloadToken(token)?.exp;
  return typeof exp === "number" && exp * 1000 > agora.getTime();
}
