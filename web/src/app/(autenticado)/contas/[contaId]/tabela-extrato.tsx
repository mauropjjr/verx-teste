import Link from "next/link";
import { formatarDataHora, formatarMoeda, formatarTipo } from "@/lib/formatacao";
import type { Lancamento, PaginaExtrato } from "@/lib/tipos";
import type { EstadoEstorno } from "./acoes";
import { BotaoEstorno } from "./botao-estorno";

type Propriedades = {
  contaId: string;
  extrato: PaginaExtrato;
  paginaInicial: boolean;
  /** Server Action de estorno — só para quem pode operar; sem ela, a coluna de ações não aparece. */
  acaoEstorno?: (estado: EstadoEstorno, dados: FormData) => Promise<EstadoEstorno>;
};

/** Estornos não se estornam e um lançamento só se estorna uma vez (os estornos visíveis nesta página contam). */
function estornaveis(itens: Lancamento[]): Set<string> {
  const jaEstornados = new Set(itens.map((item) => item.estornoDe).filter((id): id is string => id !== null));
  return new Set(itens.filter((item) => item.estornoDe === null && !jaEstornados.has(item.id)).map((item) => item.id));
}

/** Extrato paginado por cursor (keyset): "Próxima página" leva o `proximoCursor` na query string. */
export function TabelaExtrato({ contaId, extrato, paginaInicial, acaoEstorno }: Propriedades) {
  const caminhoConta = `/contas/${encodeURIComponent(contaId)}`;
  const podeEstornar = acaoEstorno ? estornaveis(extrato.itens) : new Set<string>();

  return (
    <section aria-labelledby="titulo-extrato" className="flex flex-col gap-3">
      <h2 id="titulo-extrato" className="text-xl font-semibold">
        Extrato
      </h2>
      {extrato.itens.length === 0 ? (
        <p>Nenhum lançamento nesta página.</p>
      ) : (
        <table className="w-full border-collapse text-left">
          <caption className="sr-only">Extrato da conta {contaId}</caption>
          <thead>
            <tr className="border-b border-gray-400">
              <th scope="col" className="py-2 pr-4">Data</th>
              <th scope="col" className="py-2 pr-4">Descrição</th>
              <th scope="col" className="py-2 pr-4">Tipo</th>
              <th scope="col" className="py-2 text-right">Valor</th>
              {acaoEstorno ? (
                <th scope="col" className="py-2 pl-4 text-right">
                  Ações
                </th>
              ) : null}
            </tr>
          </thead>
          <tbody>
            {extrato.itens.map((lancamento) => (
              <tr key={lancamento.id} className="border-b border-gray-200">
                <td className="py-2 pr-4 whitespace-nowrap">{formatarDataHora(lancamento.ocorridoEm)}</td>
                <td className="py-2 pr-4">
                  {lancamento.descricao}
                  {lancamento.estornoDe ? <span className="ml-2 text-sm text-gray-700">(estorno)</span> : null}
                </td>
                <td className="py-2 pr-4">{formatarTipo(lancamento.tipo)}</td>
                <td className="py-2 text-right whitespace-nowrap">{formatarMoeda(lancamento.valor)}</td>
                {acaoEstorno ? (
                  <td className="py-2 pl-4 text-right">
                    {podeEstornar.has(lancamento.id) ? (
                      // Idempotency-Key do estorno gerada ao renderizar a linha.
                      <BotaoEstorno contaId={contaId} lancamento={lancamento} chave={crypto.randomUUID()} acao={acaoEstorno} />
                    ) : null}
                  </td>
                ) : null}
              </tr>
            ))}
          </tbody>
        </table>
      )}
      <nav aria-label="Paginação do extrato" className="flex gap-4">
        {paginaInicial ? null : (
          <Link href={caminhoConta} className="text-blue-800 underline">
            Primeira página
          </Link>
        )}
        {extrato.proximoCursor ? (
          <Link
            href={`${caminhoConta}?cursor=${encodeURIComponent(extrato.proximoCursor)}`}
            className="text-blue-800 underline"
          >
            Próxima página
          </Link>
        ) : null}
      </nav>
    </section>
  );
}
