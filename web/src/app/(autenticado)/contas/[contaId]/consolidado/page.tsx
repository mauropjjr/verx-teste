import type { Metadata } from "next";
import Link from "next/link";
import { unstable_rethrow } from "next/navigation";
import { consultarConsolidadoDiario, consultarSaldo } from "@/lib/api";
import { formatarDataHora, formatarDia, formatarMoeda } from "@/lib/formatacao";
import { instanteLocalParaUtc, periodoPadrao, validarPeriodo, type ResultadoPeriodo } from "@/lib/periodo";
import type { ConsolidadoDiario, Saldo } from "@/lib/tipos";

export const metadata: Metadata = { title: "Consolidado — Banco" };

type Propriedades = {
  params: Promise<{ contaId: string }>;
  searchParams: Promise<Record<string, string | string[] | undefined>>;
};

type Carga<T> = { disponivel: true; dados: T } | { disponivel: false };

const ESTILO_CAMPO =
  "rounded border border-gray-600 px-3 py-2 focus:outline-2 focus:outline-offset-2 focus:outline-blue-700 aria-invalid:border-red-700";
const ESTILO_BOTAO =
  "self-end rounded bg-blue-700 px-4 py-2 font-semibold text-white hover:bg-blue-800 focus:outline-2 focus:outline-offset-2 focus:outline-blue-700";
const ESTILO_AVISO = "rounded border border-amber-700 bg-amber-50 px-3 py-2 text-amber-900";

function parametro(valor: string | string[] | undefined): string | undefined {
  return typeof valor === "string" && valor !== "" ? valor : undefined;
}

/** Falha da API de consolidado fica isolada no bloco dela; o redirecionamento ao login (401) segue adiante. */
async function carregar<T>(consulta: () => Promise<T>): Promise<Carga<T>> {
  try {
    return { disponivel: true, dados: await consulta() };
  } catch (erro) {
    unstable_rethrow(erro);
    return { disponivel: false };
  }
}

function textoAtualizadoAte(atualizadoAte: string | null): string {
  return atualizadoAte ? `Dados atualizados até ${formatarDataHora(atualizadoAte)}` : "Nenhum lançamento processado ainda.";
}

/**
 * Consolidado por período (spec web-portal "Consolidado do período"): filtro de/até (padrão: últimos 30 dias, até
 * 366 dias), tabela por dia e "Dados atualizados até"; e o saldo num instante escolhido. Formulários GET: a
 * consulta fica na URL e funciona sem JavaScript.
 */
export default async function PaginaConsolidado({ params, searchParams }: Propriedades) {
  const { contaId } = await params;
  const busca = await searchParams;
  const padrao = periodoPadrao();
  const de = parametro(busca.de) ?? padrao.de;
  const ate = parametro(busca.ate) ?? padrao.ate;
  const em = parametro(busca.em);

  const periodo: ResultadoPeriodo = validarPeriodo(de, ate);
  const instanteUtc = em ? instanteLocalParaUtc(em) : null;

  const [consolidado, saldo] = await Promise.all([
    periodo.valido ? carregar(() => consultarConsolidadoDiario(contaId, periodo.de, periodo.ate)) : null,
    instanteUtc ? carregar(() => consultarSaldo(contaId, instanteUtc)) : null,
  ]);

  const erroDe = !periodo.valido && periodo.campo === "de" ? periodo.mensagem : undefined;
  const erroAte = !periodo.valido && periodo.campo === "ate" ? periodo.mensagem : undefined;
  const erroInstante = em && !instanteUtc ? "Informe data e hora válidas." : undefined;

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-col gap-1">
        <Link href={`/contas/${encodeURIComponent(contaId)}`} className="text-blue-800 underline">
          Voltar para a conta
        </Link>
        <h1 className="text-2xl font-bold">Consolidado da conta {contaId}</h1>
      </div>

      <section aria-labelledby="titulo-periodo" className="flex flex-col gap-3">
        <h2 id="titulo-periodo" className="text-xl font-semibold">
          Consolidado diário
        </h2>
        <form method="get" className="flex flex-wrap gap-4">
          {em ? <input type="hidden" name="em" value={em} /> : null}
          <div className="flex flex-col gap-1">
            <label htmlFor="periodo-de" className="font-medium">
              De
            </label>
            <input
              id="periodo-de"
              name="de"
              type="date"
              defaultValue={de}
              required
              aria-invalid={erroDe ? true : undefined}
              aria-describedby={erroDe ? "erro-periodo" : undefined}
              className={ESTILO_CAMPO}
            />
          </div>
          <div className="flex flex-col gap-1">
            <label htmlFor="periodo-ate" className="font-medium">
              Até
            </label>
            <input
              id="periodo-ate"
              name="ate"
              type="date"
              defaultValue={ate}
              required
              aria-invalid={erroAte ? true : undefined}
              aria-describedby={erroAte ? "erro-periodo" : undefined}
              className={ESTILO_CAMPO}
            />
          </div>
          <button type="submit" className={ESTILO_BOTAO}>
            Consultar
          </button>
        </form>
        <p className="text-sm text-gray-700">Período de até 366 dias, no horário de Brasília.</p>

        {!periodo.valido ? (
          <p id="erro-periodo" role="alert" className="rounded border border-red-700 bg-red-50 px-3 py-2 text-red-800">
            {periodo.mensagem}
          </p>
        ) : consolidado && !consolidado.disponivel ? (
          <p role="status" className={ESTILO_AVISO}>
            Consolidado temporariamente indisponível. O extrato e o registro de lançamentos continuam funcionando.
          </p>
        ) : consolidado ? (
          <TabelaConsolidado consolidado={consolidado.dados} de={periodo.de} ate={periodo.ate} />
        ) : null}
      </section>

      <section aria-labelledby="titulo-saldo-instante" className="flex flex-col gap-3">
        <h2 id="titulo-saldo-instante" className="text-xl font-semibold">
          Saldo em um instante
        </h2>
        <form method="get" className="flex flex-wrap gap-4">
          <input type="hidden" name="de" value={de} />
          <input type="hidden" name="ate" value={ate} />
          <div className="flex flex-col gap-1">
            <label htmlFor="saldo-em" className="font-medium">
              Instante (horário de Brasília)
            </label>
            <input
              id="saldo-em"
              name="em"
              type="datetime-local"
              defaultValue={em}
              required
              aria-invalid={erroInstante ? true : undefined}
              aria-describedby={erroInstante ? "erro-instante" : undefined}
              className={ESTILO_CAMPO}
            />
          </div>
          <button type="submit" className={ESTILO_BOTAO}>
            Consultar saldo
          </button>
        </form>

        {erroInstante ? (
          <p id="erro-instante" role="alert" className="rounded border border-red-700 bg-red-50 px-3 py-2 text-red-800">
            {erroInstante}
          </p>
        ) : saldo && !saldo.disponivel ? (
          <p role="status" className={ESTILO_AVISO}>
            Saldo temporariamente indisponível. Tente novamente em instantes.
          </p>
        ) : saldo ? (
          <ResultadoSaldo saldo={saldo.dados} />
        ) : null}
      </section>
    </div>
  );
}

function TabelaConsolidado({ consolidado, de, ate }: { consolidado: ConsolidadoDiario; de: string; ate: string }) {
  return (
    <div className="flex flex-col gap-2">
      <p className="text-sm text-gray-700">{textoAtualizadoAte(consolidado.atualizadoAte)}</p>
      {consolidado.dias.length === 0 ? (
        <p>Nenhum movimento no período.</p>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full border-collapse text-left">
            <caption className="sr-only">
              Consolidado diário de {formatarDia(de)} a {formatarDia(ate)}
            </caption>
            <thead>
              <tr className="border-b border-gray-400">
                <th scope="col" className="py-2 pr-4">Dia</th>
                <th scope="col" className="py-2 pr-4 text-right">Créditos</th>
                <th scope="col" className="py-2 pr-4 text-right">Débitos</th>
                <th scope="col" className="py-2 pr-4 text-right">Saldo do dia</th>
                <th scope="col" className="py-2 text-right">Fechamento</th>
              </tr>
            </thead>
            <tbody>
              {consolidado.dias.map((dia) => (
                <tr key={dia.dia} className="border-b border-gray-200">
                  <th scope="row" className="py-2 pr-4 font-normal whitespace-nowrap">
                    {formatarDia(dia.dia)}
                  </th>
                  <td className="py-2 pr-4 text-right whitespace-nowrap">{formatarMoeda(dia.creditos)}</td>
                  <td className="py-2 pr-4 text-right whitespace-nowrap">{formatarMoeda(dia.debitos)}</td>
                  <td className="py-2 pr-4 text-right whitespace-nowrap">{formatarMoeda(dia.saldoDia)}</td>
                  <td className="py-2 text-right whitespace-nowrap">{formatarMoeda(dia.fechamento)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

function ResultadoSaldo({ saldo }: { saldo: Saldo }) {
  return (
    <dl className="flex flex-col gap-1">
      <div className="flex gap-2">
        <dt className="font-medium">Saldo em {formatarDataHora(saldo.em)}:</dt>
        <dd>{formatarMoeda(saldo.saldo)}</dd>
      </div>
      <div className="flex gap-2">
        <dt className="sr-only">Atualização</dt>
        <dd className="text-sm text-gray-700">{textoAtualizadoAte(saldo.atualizadoAte)}</dd>
      </div>
    </dl>
  );
}
