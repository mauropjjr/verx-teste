"use server";

import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { cookieSeguro, urlIdentidade } from "@/lib/configuracao";
import { caminhoRetornoSeguro } from "@/lib/retorno";
import { NOME_COOKIE_SESSAO } from "@/lib/token";

/** Recusa do login: mensagem e o usuário digitado (para não apagá-lo quando o React reinicia o formulário). */
export type EstadoLogin = { erro: string; login: string } | undefined;

const MENSAGEM_CREDENCIAIS_INVALIDAS = "Usuário ou senha inválidos";
const MENSAGEM_INDISPONIVEL = "Não foi possível entrar agora. Tente novamente em instantes.";

type RespostaLogin = { tokenAcesso: string; tipo: string; expiraEm: string };

/**
 * Login pelo servidor (design D12): chama a Identidade, grava o JWT em cookie httpOnly/SameSite=Lax com
 * `maxAge` igual à validade do token e volta à página pedida. O token nunca chega ao navegador como dado.
 */
export async function entrar(_estadoAnterior: EstadoLogin, dados: FormData): Promise<EstadoLogin> {
  const login = String(dados.get("login") ?? "").trim();
  const senha = String(dados.get("senha") ?? "");
  if (!login || !senha) {
    return { erro: MENSAGEM_CREDENCIAIS_INVALIDAS, login };
  }

  let resposta: Response;
  try {
    resposta = await fetch(`${urlIdentidade()}/api/v1/autenticacao/login`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ login, senha }),
      cache: "no-store",
    });
  } catch {
    return { erro: MENSAGEM_INDISPONIVEL, login };
  }

  // 400 (campos fora do formato) e 401 (usuário inexistente, senha errada, bloqueio) dão a mesma mensagem.
  if (resposta.status === 400 || resposta.status === 401) {
    return { erro: MENSAGEM_CREDENCIAIS_INVALIDAS, login };
  }
  if (!resposta.ok) {
    return { erro: MENSAGEM_INDISPONIVEL, login };
  }

  const corpo = (await resposta.json()) as RespostaLogin;
  const segundosRestantes = Math.floor((new Date(corpo.expiraEm).getTime() - Date.now()) / 1000);
  const armazenamento = await cookies();
  armazenamento.set(NOME_COOKIE_SESSAO, corpo.tokenAcesso, {
    httpOnly: true,
    sameSite: "lax",
    secure: cookieSeguro(),
    path: "/",
    maxAge: Math.max(segundosRestantes, 0),
  });

  redirect(caminhoRetornoSeguro(String(dados.get("retorno") ?? "")));
}

/** "Sair": remove o cookie de sessão e volta ao login (spec web-portal "Logout"). */
export async function sair(): Promise<never> {
  const armazenamento = await cookies();
  armazenamento.delete(NOME_COOKIE_SESSAO);
  redirect("/login");
}
