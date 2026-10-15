// Contratos das APIs como o portal os consome (nomes de campo iguais aos JSON das APIs).

export type Conta = { id: string; titular: string; situacao: string };

export type TipoLancamento = "CREDITO" | "DEBITO";

export type Lancamento = {
  id: string;
  contaId: string;
  tipo: TipoLancamento;
  valor: number;
  descricao: string;
  ocorridoEm: string;
  estornoDe: string | null;
};

export type PaginaExtrato = { itens: Lancamento[]; proximoCursor: string | null };

export type Saldo = { contaId: string; saldo: number; em: string; atualizadoAte: string | null };

export type DiaConsolidado = { dia: string; creditos: number; debitos: number; saldoDia: number; fechamento: number };

export type ConsolidadoDiario = { contaId: string; dias: DiaConsolidado[]; atualizadoAte: string | null };

/** Problem Details (RFC 9457) devolvido pelas APIs; `errors` vem nas recusas de validação (400). */
export type ProblemaApi = { title?: string; detail?: string; status?: number; errors?: Record<string, string[]> };
