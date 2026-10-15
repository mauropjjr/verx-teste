import type { Metadata } from "next";
import Link from "next/link";
import { listarContas } from "@/lib/api";
import { formatarSituacao } from "@/lib/formatacao";

export const metadata: Metadata = { title: "Contas — Banco" };

/** Lista de contas (titular já vem mascarado da API de lançamentos). */
export default async function PaginaContas() {
  const contas = await listarContas();

  return (
    <section className="flex flex-col gap-4">
      <h1 className="text-2xl font-bold">Contas</h1>
      <table className="w-full border-collapse text-left">
        <caption className="sr-only">Contas disponíveis</caption>
        <thead>
          <tr className="border-b border-gray-400">
            <th scope="col" className="py-2 pr-4">Conta</th>
            <th scope="col" className="py-2 pr-4">Titular</th>
            <th scope="col" className="py-2">Situação</th>
          </tr>
        </thead>
        <tbody>
          {contas.map((conta) => (
            <tr key={conta.id} className="border-b border-gray-200">
              <td className="py-2 pr-4">
                <Link href={`/contas/${encodeURIComponent(conta.id)}`} className="font-medium text-blue-800 underline">
                  {conta.id}
                </Link>
              </td>
              <td className="py-2 pr-4">{conta.titular}</td>
              <td className="py-2">{formatarSituacao(conta.situacao)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  );
}
