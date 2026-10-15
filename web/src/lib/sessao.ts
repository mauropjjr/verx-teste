import "server-only";

import { cookies } from "next/headers";
import { lerPayloadToken, NOME_COOKIE_SESSAO, tokenValido } from "./token";

/** O que as páginas podem saber do usuário logado. O token em si fica só no servidor. */
export type Sessao = {
  usuarioId: string;
  nome: string;
  papeis: string[];
};

/** Token de acesso do cookie, quando ainda válido — uso exclusivo do cliente HTTP servidor (lib/api). */
export async function obterTokenAcesso(): Promise<string | null> {
  const token = (await cookies()).get(NOME_COOKIE_SESSAO)?.value;
  return token && tokenValido(token) ? token : null;
}

/** Usuário e papéis lidos do token (decodificado no servidor, design D12); `null` sem sessão válida. */
export async function obterSessao(): Promise<Sessao | null> {
  const token = await obterTokenAcesso();
  const payload = token ? lerPayloadToken(token) : null;
  if (!payload?.sub) {
    return null;
  }
  const papeis = payload.role === undefined ? [] : Array.isArray(payload.role) ? payload.role : [payload.role];
  return { usuarioId: payload.sub, nome: payload.name ?? payload.sub, papeis };
}
