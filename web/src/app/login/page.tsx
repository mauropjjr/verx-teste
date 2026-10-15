import type { Metadata } from "next";
import { caminhoRetornoSeguro } from "@/lib/retorno";
import { entrar } from "./acoes";
import { FormularioLogin } from "./formulario-login";

export const metadata: Metadata = { title: "Entrar — Banco" };

export default async function PaginaLogin({ searchParams }: PageProps<"/login">) {
  const { retorno } = await searchParams;
  const destino = caminhoRetornoSeguro(typeof retorno === "string" ? retorno : undefined);

  return (
    <main className="mx-auto flex w-full max-w-sm flex-1 flex-col justify-center gap-6 px-4 py-12">
      <h1 className="text-2xl font-bold">Entrar no portal</h1>
      <FormularioLogin acao={entrar} retorno={destino} />
    </main>
  );
}
