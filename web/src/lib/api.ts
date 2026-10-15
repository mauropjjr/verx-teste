import "server-only";

import { redirect } from "next/navigation";
import { urlConsolidado, urlLancamentos } from "./configuracao";
import { obterTokenAcesso } from "./sessao";
import type { ConsolidadoDiario, Conta, Lancamento, PaginaExtrato, ProblemaApi, Saldo, TipoLancamento } from "./tipos";

/** Falha de uma API de negócio (status HTTP diferente de 2xx e de 401), com o Problem Details quando houver. */
export class ErroApi extends Error {
  override readonly name = "ErroApi";

  constructor(
    readonly status: number,
    readonly url: string,
    readonly problema: ProblemaApi | null = null,
  ) {
    super(`A API respondeu ${status}.`);
  }
}

type Requisicao = { metodo?: "GET" | "POST"; corpo?: string; chaveIdempotencia?: string };

async function lerProblema(resposta: Response): Promise<ProblemaApi | null> {
  try {
    const corpo: unknown = await resposta.json();
    return typeof corpo === "object" && corpo !== null ? (corpo as ProblemaApi) : null;
  } catch {
    return null;
  }
}

/**
 * Chamada servidor→API com o token do cookie como Bearer (design D12): o token nunca vai para Client
 * Components. Sem sessão, ou com 401 da API (token recusado), o usuário volta ao login.
 */
async function requisitar<T>(url: string, { metodo = "GET", corpo, chaveIdempotencia }: Requisicao = {}): Promise<T> {
  const token = await obterTokenAcesso();
  if (!token) {
    redirect("/login");
  }

  const cabecalhos: Record<string, string> = { Authorization: `Bearer ${token}`, Accept: "application/json" };
  if (corpo !== undefined) {
    cabecalhos["Content-Type"] = "application/json";
  }
  if (chaveIdempotencia !== undefined) {
    cabecalhos["Idempotency-Key"] = chaveIdempotencia;
  }

  const resposta = await fetch(url, { method: metodo, headers: cabecalhos, body: corpo, cache: "no-store" });
  if (resposta.status === 401) {
    redirect("/login");
  }
  if (!resposta.ok) {
    throw new ErroApi(resposta.status, url, await lerProblema(resposta));
  }
  return (await resposta.json()) as T;
}

function comQuery(caminho: string, parametros: Record<string, string | number | undefined>): string {
  const query = new URLSearchParams();
  for (const [nome, valor] of Object.entries(parametros)) {
    if (valor !== undefined && valor !== "") {
      query.set(nome, String(valor));
    }
  }
  const texto = query.toString();
  return texto ? `${caminho}?${texto}` : caminho;
}

function caminhoConta(base: string, contaId: string): string {
  return `${base}/api/v1/contas/${encodeURIComponent(contaId)}`;
}

export function listarContas(): Promise<Conta[]> {
  return requisitar(`${urlLancamentos()}/api/v1/contas`);
}

export function consultarExtrato(contaId: string, opcoes: { cursor?: string; limite?: number } = {}): Promise<PaginaExtrato> {
  const caminho = `${caminhoConta(urlLancamentos(), contaId)}/lancamentos`;
  return requisitar(comQuery(caminho, { limite: opcoes.limite, cursor: opcoes.cursor }));
}

/** Saldo atual ou, com `em` (ISO-8601 UTC), o saldo naquele instante. */
export function consultarSaldo(contaId: string, em?: string): Promise<Saldo> {
  return requisitar(comQuery(`${caminhoConta(urlConsolidado(), contaId)}/saldo`, { em }));
}

/** Consolidado diário do período (`de`/`ate` em `yyyy-MM-dd`). */
export function consultarConsolidadoDiario(contaId: string, de: string, ate: string): Promise<ConsolidadoDiario> {
  return requisitar(comQuery(`${caminhoConta(urlConsolidado(), contaId)}/consolidado-diario`, { de, ate }));
}

/**
 * Novo lançamento. `valor` é o decimal já normalizado (`1234.56`) e vai como número JSON literal, sem passar
 * por `number` do JavaScript — valores grandes não perdem centavos.
 */
export function registrarLancamento(
  contaId: string,
  lancamento: { tipo: TipoLancamento; valor: string; descricao: string },
  chaveIdempotencia: string,
): Promise<Lancamento> {
  if (!/^\d+(\.\d{1,2})?$/.test(lancamento.valor)) {
    throw new Error("Valor de lançamento fora do formato decimal normalizado.");
  }
  const corpo = `{"tipo":${JSON.stringify(lancamento.tipo)},"valor":${lancamento.valor},"descricao":${JSON.stringify(lancamento.descricao)}}`;
  return requisitar(`${caminhoConta(urlLancamentos(), contaId)}/lancamentos`, { metodo: "POST", corpo, chaveIdempotencia });
}

export function estornarLancamento(contaId: string, lancamentoId: string, chaveIdempotencia: string): Promise<Lancamento> {
  const url = `${caminhoConta(urlLancamentos(), contaId)}/lancamentos/${encodeURIComponent(lancamentoId)}/estorno`;
  return requisitar(url, { metodo: "POST", chaveIdempotencia });
}
