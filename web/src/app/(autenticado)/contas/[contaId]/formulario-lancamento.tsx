"use client";

import { useActionState, useEffect, useRef } from "react";
import { TAMANHO_MAXIMO_DESCRICAO, type CampoLancamento } from "@/lib/validacao-lancamento";
import type { EstadoLancamento } from "./acoes";

type Propriedades = {
  contaId: string;
  /** Estado de abertura: traz a `Idempotency-Key` gerada no servidor ao renderizar o formulário. */
  estadoInicial: EstadoLancamento;
  acao: (estado: EstadoLancamento, dados: FormData) => Promise<EstadoLancamento>;
};

const ORDEM_CAMPOS: readonly CampoLancamento[] = ["tipo", "valor", "descricao"];
const ESTILO_CAMPO =
  "rounded border border-gray-600 px-3 py-2 focus:outline-2 focus:outline-offset-2 focus:outline-blue-700 aria-invalid:border-red-700";

/**
 * Formulário de lançamento (só UI): o envio é a Server Action `registrar`. A chave de idempotência vem do estado
 * (campo oculto) e só muda depois de um registro aceito; o botão fica desabilitado enquanto o envio está em curso.
 */
export function FormularioLancamento({ contaId, estadoInicial, acao }: Propriedades) {
  const [estado, enviar, enviando] = useActionState(acao, estadoInicial);
  const formulario = useRef<HTMLFormElement>(null);
  const erros = estado.erros ?? {};
  const temErrosCampo = ORDEM_CAMPOS.some((campo) => erros[campo]);

  // Leva o foco ao primeiro campo com erro (a mensagem é lida pela descrição do campo).
  useEffect(() => {
    const primeiro = ORDEM_CAMPOS.find((campo) => estado.erros?.[campo]);
    if (primeiro) {
      formulario.current?.querySelector<HTMLInputElement>(`[name="${primeiro}"]`)?.focus();
    }
  }, [estado]);

  const descricaoErro = (campo: CampoLancamento) => (erros[campo] ? `erro-lancamento-${campo}` : undefined);

  return (
    <form ref={formulario} action={enviar} noValidate className="flex flex-col gap-4">
      <input type="hidden" name="contaId" value={contaId} />
      <input type="hidden" name="chave" value={estado.chave} />

      <fieldset aria-describedby={descricaoErro("tipo")} className="flex flex-col gap-1">
        <legend className="font-medium">Tipo</legend>
        <div className="flex gap-6">
          {(
            [
              ["CREDITO", "Crédito"],
              ["DEBITO", "Débito"],
            ] as const
          ).map(([valor, rotulo]) => (
            <label key={valor} className="flex items-center gap-2">
              <input
                type="radio"
                name="tipo"
                value={valor}
                defaultChecked={estado.valores?.tipo === valor}
                className="h-4 w-4 focus:outline-2 focus:outline-offset-2 focus:outline-blue-700"
              />
              {rotulo}
            </label>
          ))}
        </div>
        {erros.tipo ? (
          <p id="erro-lancamento-tipo" className="text-red-800">
            {erros.tipo}
          </p>
        ) : null}
      </fieldset>

      <div className="flex flex-col gap-1">
        <label htmlFor="lancamento-valor" className="font-medium">
          Valor (R$)
        </label>
        <input
          id="lancamento-valor"
          name="valor"
          type="text"
          inputMode="decimal"
          autoComplete="off"
          defaultValue={estado.valores?.valor}
          aria-invalid={erros.valor ? true : undefined}
          aria-describedby={["dica-lancamento-valor", descricaoErro("valor")].filter(Boolean).join(" ")}
          className={ESTILO_CAMPO}
        />
        <p id="dica-lancamento-valor" className="text-sm text-gray-700">
          Maior que zero, com vírgula para os centavos (ex.: 1.234,56).
        </p>
        {erros.valor ? (
          <p id="erro-lancamento-valor" className="text-red-800">
            {erros.valor}
          </p>
        ) : null}
      </div>

      <div className="flex flex-col gap-1">
        <label htmlFor="lancamento-descricao" className="font-medium">
          Descrição
        </label>
        <input
          id="lancamento-descricao"
          name="descricao"
          type="text"
          autoComplete="off"
          maxLength={TAMANHO_MAXIMO_DESCRICAO}
          defaultValue={estado.valores?.descricao}
          aria-invalid={erros.descricao ? true : undefined}
          aria-describedby={["dica-lancamento-descricao", descricaoErro("descricao")].filter(Boolean).join(" ")}
          className={ESTILO_CAMPO}
        />
        <p id="dica-lancamento-descricao" className="text-sm text-gray-700">
          Até {TAMANHO_MAXIMO_DESCRICAO} caracteres.
        </p>
        {erros.descricao ? (
          <p id="erro-lancamento-descricao" className="text-red-800">
            {erros.descricao}
          </p>
        ) : null}
      </div>

      {/* Regiões vivas sempre presentes: o conteúdo novo é anunciado por leitores de tela. */}
      <div role="alert" className="empty:hidden rounded border border-red-700 bg-red-50 px-3 py-2 text-red-800">
        {temErrosCampo ? "Corrija os campos indicados. " : null}
        {estado.erro ?? null}
      </div>
      <div role="status" className="empty:hidden rounded border border-green-700 bg-green-50 px-3 py-2 text-green-900">
        {estado.sucesso ?? null}
      </div>

      <button
        type="submit"
        disabled={enviando}
        className="self-start rounded bg-blue-700 px-4 py-2 font-semibold text-white hover:bg-blue-800 focus:outline-2 focus:outline-offset-2 focus:outline-blue-700 disabled:opacity-60"
      >
        {enviando ? "Registrando…" : "Registrar"}
      </button>
    </form>
  );
}
