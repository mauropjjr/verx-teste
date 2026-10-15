import Link from "next/link";
import { redirect } from "next/navigation";
import { sair } from "@/app/login/acoes";
import { obterSessao } from "@/lib/sessao";

/** Moldura das páginas autenticadas: identifica o usuário (sem expor o token) e oferece "Sair". */
export default async function LayoutAutenticado({ children }: { children: React.ReactNode }) {
  const sessao = await obterSessao();
  if (!sessao) {
    redirect("/login");
  }

  return (
    <>
      <a
        href="#conteudo"
        className="sr-only focus:not-sr-only focus:absolute focus:left-2 focus:top-2 focus:bg-white focus:px-3 focus:py-2"
      >
        Ir para o conteúdo
      </a>
      <header className="border-b border-gray-300 bg-gray-50">
        <div className="mx-auto flex w-full max-w-5xl items-center justify-between gap-4 px-4 py-3">
          <Link href="/contas" className="text-lg font-bold">
            Banco
          </Link>
          <div className="flex items-center gap-4">
            <span>
              {sessao.nome} <span className="text-sm text-gray-700">({sessao.papeis.join(", ")})</span>
            </span>
            <form action={sair}>
              <button
                type="submit"
                className="rounded border border-gray-500 px-3 py-1 hover:bg-gray-100 focus:outline-2 focus:outline-offset-2 focus:outline-blue-700"
              >
                Sair
              </button>
            </form>
          </div>
        </div>
      </header>
      <main id="conteudo" className="mx-auto w-full max-w-5xl flex-1 px-4 py-6">
        {children}
      </main>
    </>
  );
}
