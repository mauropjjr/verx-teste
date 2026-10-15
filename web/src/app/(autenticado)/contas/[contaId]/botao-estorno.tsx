"use client";

import { useActionState, useEffect, useId, useRef, useState } from "react";
import { formatarMoeda } from "@/lib/formatacao";
import type { EstadoEstorno } from "./acoes";

type Propriedades = {
  contaId: string;
  lancamento: { id: string; descricao: string; valor: number };
  /** `Idempotency-Key` gerada no servidor ao renderizar o extrato. */
  chave: string;
  acao: (estado: EstadoEstorno, dados: FormData) => Promise<EstadoEstorno>;
};

const ESTILO_BOTAO = "rounded px-3 py-1 focus:outline-2 focus:outline-offset-2 focus:outline-blue-700 disabled:opacity-60";

/**
 * Estorno com etapa de confirmação: "Estornar" abre a confirmação (foco no botão de confirmar, Esc cancela e
 * devolve o foco). O envio é a Server Action `estornar`; enquanto ele corre, "Estornar" fica desabilitado.
 */
export function BotaoEstorno({ contaId, lancamento, chave, acao }: Propriedades) {
  const [estado, enviar, enviando] = useActionState(acao, { chave });
  const [confirmando, setConfirmando] = useState(false);
  const botaoEstornar = useRef<HTMLButtonElement>(null);
  const botaoConfirmar = useRef<HTMLButtonElement>(null);
  const idPergunta = useId();
  const devolverFoco = useRef(false);

  useEffect(() => {
    if (confirmando) {
      botaoConfirmar.current?.focus();
    } else if (devolverFoco.current) {
      devolverFoco.current = false;
      botaoEstornar.current?.focus();
    }
  }, [confirmando]);

  function fecharConfirmacao() {
    devolverFoco.current = true;
    setConfirmando(false);
  }

  if (!confirmando) {
    return (
      <div className="flex flex-col items-end gap-1">
        <button
          ref={botaoEstornar}
          type="button"
          // aria-disabled (e não disabled) durante o envio: o botão continua focável e o foco não se perde.
          onClick={() => !enviando && setConfirmando(true)}
          aria-disabled={enviando || undefined}
          aria-label={enviando ? undefined : `Estornar lançamento ${lancamento.descricao}`}
          className={`${ESTILO_BOTAO} border border-gray-600 hover:bg-gray-100`}
        >
          {enviando ? "Estornando…" : "Estornar"}
        </button>
        {estado.erro ? (
          <p role="alert" className="text-sm text-red-800">
            {estado.erro}
          </p>
        ) : null}
        {estado.sucesso ? (
          <p role="status" className="text-sm text-green-900">
            {estado.sucesso}
          </p>
        ) : null}
      </div>
    );
  }

  return (
    <form
      action={enviar}
      // Fecha a confirmação já no envio: um segundo clique não encontra mais o botão (uma única chamada).
      onSubmit={fecharConfirmacao}
      onKeyDown={(evento) => {
        if (evento.key === "Escape") {
          evento.preventDefault();
          fecharConfirmacao();
        }
      }}
      className="flex flex-col items-end gap-2"
    >
      <input type="hidden" name="contaId" value={contaId} />
      <input type="hidden" name="lancamentoId" value={lancamento.id} />
      <input type="hidden" name="chave" value={estado.chave} />
      <p id={idPergunta} className="text-sm">
        Estornar &ldquo;{lancamento.descricao}&rdquo; ({formatarMoeda(lancamento.valor)})? Será criado um lançamento inverso.
      </p>
      <div className="flex gap-2">
        <button
          ref={botaoConfirmar}
          type="submit"
          aria-describedby={idPergunta}
          className={`${ESTILO_BOTAO} bg-red-700 font-semibold text-white hover:bg-red-800`}
        >
          Confirmar estorno
        </button>
        <button type="button" onClick={fecharConfirmacao} className={`${ESTILO_BOTAO} border border-gray-600 hover:bg-gray-100`}>
          Cancelar
        </button>
      </div>
    </form>
  );
}
