"use client";

/** Falha inesperada numa página autenticada (ex.: API de lançamentos fora do ar). */
export default function ErroPagina({ reset }: { error: Error & { digest?: string }; reset: () => void }) {
  return (
    <div role="alert" className="flex flex-col gap-3">
      <h1 className="text-2xl font-bold">Não foi possível carregar esta página</h1>
      <p>Tente novamente em instantes.</p>
      <button
        type="button"
        onClick={reset}
        className="self-start rounded bg-blue-700 px-4 py-2 font-semibold text-white hover:bg-blue-800 focus:outline-2 focus:outline-offset-2 focus:outline-blue-700"
      >
        Tentar de novo
      </button>
    </div>
  );
}
