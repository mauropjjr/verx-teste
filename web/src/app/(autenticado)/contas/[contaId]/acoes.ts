"use server";

import { revalidatePath } from "next/cache";
import { redirect, unstable_rethrow } from "next/navigation";
import { ErroApi, estornarLancamento, registrarLancamento } from "@/lib/api";
import { formatarMoeda } from "@/lib/formatacao";
import { podeOperar } from "@/lib/papeis";
import { obterSessao } from "@/lib/sessao";
import type { ProblemaApi } from "@/lib/tipos";
import {
  validarLancamento,
  type CampoLancamento,
  type CamposLancamento,
  type ErrosLancamento,
} from "@/lib/validacao-lancamento";

/**
 * Estado do formulário de lançamento. `chave` é a `Idempotency-Key` do PRÓXIMO envio: a mesma enquanto o
 * lançamento não foi aceito (retentativas não duplicam), uma nova depois do sucesso.
 */
export type EstadoLancamento = {
  chave: string;
  sucesso?: string;
  erro?: string;
  erros?: ErrosLancamento;
  valores?: CamposLancamento;
};

/** Estado do estorno de um lançamento; `chave` segue a mesma regra do formulário de lançamento. */
export type EstadoEstorno = { chave: string; sucesso?: string; erro?: string };

const CAMPOS: readonly CampoLancamento[] = ["tipo", "valor", "descricao"];
const CONTA_ID = /^[A-Za-z0-9-]{1,20}$/;
const LANCAMENTO_ID = /^[0-9a-fA-F-]{36}$/;

function texto(dados: FormData, nome: string): string {
  const valor = dados.get(nome);
  return typeof valor === "string" ? valor : "";
}

/** Só operador/admin escrevem — verificado no servidor, pois a Server Action é alcançável por POST direto. */
async function usuarioPodeOperar(): Promise<boolean> {
  const sessao = await obterSessao();
  if (!sessao) {
    redirect("/login");
  }
  return podeOperar(sessao.papeis);
}

/** Mensagem de uma recusa de negócio (4xx com Problem Details); falhas de servidor ficam com a genérica. */
function mensagemRecusa(erro: unknown): string | undefined {
  if (!(erro instanceof ErroApi) || erro.status >= 500 || !erro.problema?.title) {
    return undefined;
  }
  return erro.problema.detail ? `${erro.problema.title}: ${erro.problema.detail}` : erro.problema.title;
}

/** Erros de validação (400) da API: os de campo vão para o campo, o resto vira erro geral. */
function errosDoProblema(problema: ProblemaApi | null): { erros: ErrosLancamento; outros: string[] } {
  const erros: ErrosLancamento = {};
  const outros: string[] = [];
  for (const [chave, mensagens] of Object.entries(problema?.errors ?? {})) {
    const campo = CAMPOS.find((c) => c === chave.toLowerCase());
    if (campo) {
      erros[campo] = mensagens.join(" ");
    } else {
      outros.push(...mensagens);
    }
  }
  return { erros, outros };
}

/** Registra crédito/débito (spec web-portal "Registrar lançamento pelo portal"). */
export async function registrar(estado: EstadoLancamento, dados: FormData): Promise<EstadoLancamento> {
  const chave = texto(dados, "chave") || estado.chave;
  const contaId = texto(dados, "contaId");
  const valores: CamposLancamento = { tipo: texto(dados, "tipo"), valor: texto(dados, "valor"), descricao: texto(dados, "descricao") };

  if (!(await usuarioPodeOperar())) {
    return { chave, erro: "Você não tem permissão para registrar lançamentos.", valores };
  }
  if (!CONTA_ID.test(contaId)) {
    return { chave, erro: "Conta inválida.", valores };
  }

  const validacao = validarLancamento(valores);
  if (!validacao.valido) {
    return { chave, erros: validacao.erros, valores };
  }

  try {
    await registrarLancamento(contaId, validacao.lancamento, chave);
  } catch (erro) {
    unstable_rethrow(erro);
    if (erro instanceof ErroApi && erro.status === 400) {
      const { erros, outros } = errosDoProblema(erro.problema);
      return { chave, erros, erro: outros.length > 0 ? outros.join(" ") : undefined, valores };
    }
    if (erro instanceof ErroApi && erro.status === 403) {
      return { chave, erro: "Você não tem permissão para registrar lançamentos.", valores };
    }
    return { chave, erro: mensagemRecusa(erro) ?? "Não foi possível registrar o lançamento agora. Tente novamente.", valores };
  }

  revalidatePath(`/contas/${contaId}`);
  const tipo = validacao.lancamento.tipo === "CREDITO" ? "crédito" : "débito";
  return {
    chave: crypto.randomUUID(),
    sucesso: `Lançamento registrado: ${tipo} de ${formatarMoeda(Number(validacao.lancamento.valor))}.`,
  };
}

/** Estorna um lançamento (409 da API ⇒ "Lançamento já estornado"). */
export async function estornar(estado: EstadoEstorno, dados: FormData): Promise<EstadoEstorno> {
  const chave = texto(dados, "chave") || estado.chave;
  const contaId = texto(dados, "contaId");
  const lancamentoId = texto(dados, "lancamentoId");

  if (!(await usuarioPodeOperar())) {
    return { chave, erro: "Você não tem permissão para estornar lançamentos." };
  }
  if (!CONTA_ID.test(contaId) || !LANCAMENTO_ID.test(lancamentoId)) {
    return { chave, erro: "Lançamento inválido." };
  }

  try {
    await estornarLancamento(contaId, lancamentoId, chave);
  } catch (erro) {
    unstable_rethrow(erro);
    if (erro instanceof ErroApi && erro.status === 409) {
      return { chave, erro: "Lançamento já estornado" };
    }
    if (erro instanceof ErroApi && erro.status === 403) {
      return { chave, erro: "Você não tem permissão para estornar lançamentos." };
    }
    return { chave, erro: mensagemRecusa(erro) ?? "Não foi possível estornar o lançamento agora. Tente novamente." };
  }

  revalidatePath(`/contas/${contaId}`);
  return { chave: crypto.randomUUID(), sucesso: "Lançamento estornado." };
}
