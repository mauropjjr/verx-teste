"use client";

import { useActionState } from "react";
import type { EstadoLogin } from "./acoes";

type Propriedades = {
  acao: (estado: EstadoLogin, dados: FormData) => Promise<EstadoLogin>;
  retorno: string;
};

/** Formulário de login: campos rotulados e erro anunciado a leitores de tela (role="alert"). */
export function FormularioLogin({ acao, retorno }: Propriedades) {
  const [estado, enviar, enviando] = useActionState(acao, undefined);

  return (
    <form action={enviar} className="flex flex-col gap-4">
      <input type="hidden" name="retorno" value={retorno} />

      <div className="flex flex-col gap-1">
        <label htmlFor="login" className="font-medium">
          Usuário
        </label>
        <input
          id="login"
          name="login"
          type="text"
          autoComplete="username"
          defaultValue={estado?.login}
          required
          maxLength={100}
          aria-describedby={estado?.erro ? "erro-login" : undefined}
          className="rounded border border-gray-500 px-3 py-2 focus:outline-2 focus:outline-offset-2 focus:outline-blue-700"
        />
      </div>

      <div className="flex flex-col gap-1">
        <label htmlFor="senha" className="font-medium">
          Senha
        </label>
        <input
          id="senha"
          name="senha"
          type="password"
          autoComplete="current-password"
          required
          maxLength={200}
          aria-describedby={estado?.erro ? "erro-login" : undefined}
          className="rounded border border-gray-500 px-3 py-2 focus:outline-2 focus:outline-offset-2 focus:outline-blue-700"
        />
      </div>

      {estado?.erro ? (
        <p id="erro-login" role="alert" className="rounded border border-red-700 bg-red-50 px-3 py-2 text-red-800">
          {estado.erro}
        </p>
      ) : null}

      <button
        type="submit"
        disabled={enviando}
        className="rounded bg-blue-700 px-4 py-2 font-semibold text-white hover:bg-blue-800 focus:outline-2 focus:outline-offset-2 focus:outline-blue-700 disabled:opacity-60"
      >
        {enviando ? "Entrando…" : "Entrar"}
      </button>
    </form>
  );
}
