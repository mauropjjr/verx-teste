import type { Metadata } from "next";
import Link from "next/link";
import { notFound, unstable_rethrow } from "next/navigation";
import { consultarExtrato, consultarSaldo, ErroApi } from "@/lib/api";
import { podeOperar } from "@/lib/papeis";
import { obterSessao } from "@/lib/sessao";
import { estornar, registrar } from "./acoes";
import { FormularioLancamento } from "./formulario-lancamento";
import { SecaoConsolidado, type ResultadoSaldo } from "./secao-consolidado";
import { TabelaExtrato } from "./tabela-extrato";

export const metadata: Metadata = { title: "Conta — Banco" };

const TAMANHO_PAGINA = 20;

type Propriedades = {
  params: Promise<{ contaId: string }>;
  searchParams: Promise<Record<string, string | string[] | undefined>>;
};

/** Falha do consolidado fica isolada na seção dele; o redirecionamento ao login (401) segue adiante. */
async function carregarSaldo(contaId: string): Promise<ResultadoSaldo> {
  try {
    return { disponivel: true, saldo: await consultarSaldo(contaId) };
  } catch (erro) {
    unstable_rethrow(erro);
    return { disponivel: false };
  }
}

async function carregarExtrato(contaId: string, cursor: string | undefined) {
  try {
    return await consultarExtrato(contaId, { cursor, limite: TAMANHO_PAGINA });
  } catch (erro) {
    if (erro instanceof ErroApi && erro.status === 404) {
      notFound();
    }
    throw erro;
  }
}

export default async function PaginaConta({ params, searchParams }: Propriedades) {
  const { contaId } = await params;
  const { cursor } = await searchParams;
  const cursorAtual = typeof cursor === "string" && cursor !== "" ? cursor : undefined;

  const [extrato, saldo, sessao] = await Promise.all([
    carregarExtrato(contaId, cursorAtual),
    carregarSaldo(contaId),
    obterSessao(),
  ]);
  // Ações de escrita só para operador/admin (as Server Actions também recusam sem o papel).
  const escrita = sessao && podeOperar(sessao.papeis);

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-col gap-1">
        <Link href="/contas" className="text-blue-800 underline">
          Voltar para contas
        </Link>
        <h1 className="text-2xl font-bold">Conta {contaId}</h1>
      </div>
      <SecaoConsolidado contaId={contaId} resultado={saldo} />
      {escrita ? (
        <section aria-labelledby="titulo-registrar" className="flex flex-col gap-3 rounded border border-gray-300 p-4">
          <h2 id="titulo-registrar" className="text-xl font-semibold">
            Registrar lançamento
          </h2>
          {/* Idempotency-Key gerada ao abrir o formulário; retentativas reenviam a mesma. */}
          <FormularioLancamento contaId={contaId} estadoInicial={{ chave: crypto.randomUUID() }} acao={registrar} />
        </section>
      ) : null}
      <TabelaExtrato
        contaId={contaId}
        extrato={extrato}
        paginaInicial={!cursorAtual}
        acaoEstorno={escrita ? estornar : undefined}
      />
    </div>
  );
}
