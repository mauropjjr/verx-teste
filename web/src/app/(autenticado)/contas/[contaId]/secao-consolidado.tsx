import Link from "next/link";
import { formatarDataHora, formatarMoeda } from "@/lib/formatacao";
import type { Saldo } from "@/lib/tipos";

export type ResultadoSaldo = { disponivel: true; saldo: Saldo } | { disponivel: false };

/**
 * Seção do consolidado com degradação graciosa (spec web-portal "Consolidado indisponível"): se a API de
 * consolidado falhar, só esta seção mostra o aviso; extrato e registro continuam funcionando.
 */
export function SecaoConsolidado({ contaId, resultado }: { contaId: string; resultado: ResultadoSaldo }) {
  return (
    <section aria-labelledby="titulo-consolidado" className="flex flex-col gap-2 rounded border border-gray-300 p-4">
      <h2 id="titulo-consolidado" className="text-xl font-semibold">
        Consolidado
      </h2>
      {resultado.disponivel ? (
        <dl className="flex flex-col gap-1">
          <div className="flex gap-2">
            <dt className="font-medium">Saldo atual:</dt>
            <dd>{formatarMoeda(resultado.saldo.saldo)}</dd>
          </div>
          <div className="flex gap-2">
            {/* Todo <dd> precisa de um <dt> (axe definition-list). */}
            <dt className="sr-only">Atualização</dt>
            <dd className="text-sm text-gray-700">
              {resultado.saldo.atualizadoAte
                ? `dados atualizados até ${formatarDataHora(resultado.saldo.atualizadoAte)}`
                : "nenhum lançamento processado ainda"}
            </dd>
          </div>
        </dl>
      ) : (
        <p role="status" className="rounded border border-amber-700 bg-amber-50 px-3 py-2 text-amber-900">
          Consolidado temporariamente indisponível. O extrato e o registro de lançamentos continuam funcionando.
        </p>
      )}
      <Link href={`/contas/${encodeURIComponent(contaId)}/consolidado`} className="self-start text-blue-800 underline">
        Consolidado por período
      </Link>
    </section>
  );
}
