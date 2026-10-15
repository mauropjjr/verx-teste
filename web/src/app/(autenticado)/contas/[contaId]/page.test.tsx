import { render, screen, within } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { consultarExtrato, consultarSaldo, ErroApi } from "@/lib/api";
import { obterSessao } from "@/lib/sessao";

vi.mock("@/lib/api", async () => {
  class ErroApi extends Error {
    constructor(readonly status: number) {
      super(`A API respondeu ${status}.`);
    }
  }
  return { ErroApi, consultarExtrato: vi.fn(), consultarSaldo: vi.fn() };
});

vi.mock("@/lib/sessao", () => ({ obterSessao: vi.fn() }));

import PaginaConta from "./page";

const extrato = {
  itens: [
    {
      id: "a1",
      contaId: "CC-0001",
      tipo: "CREDITO" as const,
      valor: 1500.5,
      descricao: "Depósito em dinheiro",
      ocorridoEm: "2026-10-10T13:30:00Z",
      estornoDe: null,
    },
    {
      id: "a2",
      contaId: "CC-0001",
      tipo: "DEBITO" as const,
      valor: 89.9,
      descricao: "Pagamento fornecedor",
      ocorridoEm: "2026-10-09T18:05:00Z",
      estornoDe: null,
    },
  ],
  proximoCursor: "cur+2/=",
};

function abrir(contaId: string, busca: Record<string, string> = {}) {
  return PaginaConta({ params: Promise.resolve({ contaId }), searchParams: Promise.resolve(busca) });
}

function normalizar(texto: string | null | undefined): string {
  return (texto ?? "").replace(/\s/g, " ");
}

describe("página da conta", () => {
  beforeEach(() => {
    vi.mocked(obterSessao).mockResolvedValue({ usuarioId: "u-1", nome: "Consulta", papeis: ["consulta"] });
    vi.mocked(consultarExtrato).mockResolvedValue(extrato);
    vi.mocked(consultarSaldo).mockResolvedValue({
      contaId: "CC-0001",
      saldo: 1234.56,
      em: "2026-10-12T15:00:00Z",
      atualizadoAte: "2026-10-12T14:59:00Z",
    });
  });

  it("mostra a página do extrato pedida pelo cursor e o link de próxima página com o próximo cursor", async () => {
    render(await abrir("CC-0001", { cursor: "cur-1" }));

    expect(consultarExtrato).toHaveBeenCalledWith("CC-0001", expect.objectContaining({ cursor: "cur-1" }));
    expect(screen.getByRole("heading", { level: 1, name: "Conta CC-0001" })).toBeInTheDocument();

    const tabela = screen.getByRole("table", { name: "Extrato da conta CC-0001" });
    const linhas = within(tabela).getAllByRole("row").slice(1);
    expect(linhas).toHaveLength(2);
    expect(normalizar(linhas[0].textContent)).toContain("10/10/2026, 10:30");
    expect(normalizar(linhas[0].textContent)).toContain("Depósito em dinheiro");
    expect(normalizar(linhas[0].textContent)).toContain("Crédito");
    expect(normalizar(linhas[0].textContent)).toContain("R$ 1.500,50");
    expect(normalizar(linhas[1].textContent)).toContain("Débito");

    expect(screen.getByRole("link", { name: "Próxima página" })).toHaveAttribute(
      "href",
      "/contas/CC-0001?cursor=cur%2B2%2F%3D",
    );
    expect(screen.getByRole("link", { name: "Primeira página" })).toHaveAttribute("href", "/contas/CC-0001");

    const consolidado = screen.getByRole("region", { name: "Consolidado" });
    expect(normalizar(consolidado.textContent)).toContain("R$ 1.234,56");
    expect(normalizar(consolidado.textContent)).toContain("dados atualizados até 12/10/2026, 11:59");
  });

  it("com o consolidado fora do ar mostra o aviso e o extrato continua funcionando", async () => {
    vi.mocked(consultarSaldo).mockRejectedValue(new ErroApi(503, "http://consolidado"));
    vi.mocked(consultarExtrato).mockResolvedValue({ ...extrato, proximoCursor: null });

    render(await abrir("CC-0001"));

    const consolidado = screen.getByRole("region", { name: "Consolidado" });
    expect(within(consolidado).getByRole("status")).toHaveTextContent("Consolidado temporariamente indisponível");
    expect(within(screen.getByRole("table", { name: "Extrato da conta CC-0001" })).getAllByRole("row")).toHaveLength(3);
    expect(screen.queryByRole("link", { name: "Próxima página" })).not.toBeInTheDocument();
  });

  it("operador vê o formulário de lançamento e o estorno por lançamento; consulta não vê ações de escrita", async () => {
    const original = { ...extrato.itens[0], id: "6f1c2b9e-0000-4000-8000-000000000001" };
    const estornoDoOriginal = {
      ...extrato.itens[0],
      id: "6f1c2b9e-0000-4000-8000-000000000002",
      tipo: "DEBITO" as const,
      descricao: "Estorno: Depósito em dinheiro",
      estornoDe: original.id,
    };
    const outro = { ...extrato.itens[1], id: "6f1c2b9e-0000-4000-8000-000000000003" };
    vi.mocked(consultarExtrato).mockResolvedValue({ itens: [estornoDoOriginal, original, outro], proximoCursor: null });
    vi.mocked(obterSessao).mockResolvedValue({ usuarioId: "u-2", nome: "Operador", papeis: ["operador"] });

    const { container, unmount } = render(await abrir("CC-0001"));

    const secao = screen.getByRole("region", { name: "Registrar lançamento" });
    expect(within(secao).getByRole("button", { name: "Registrar" })).toBeInTheDocument();
    const chave = container.querySelector<HTMLInputElement>('input[type="hidden"][name="chave"]');
    expect(chave?.value).toMatch(/^[0-9a-f-]{36}$/);
    // Só o lançamento ainda estornável tem "Estornar": nem o estorno nem o já estornado.
    expect(screen.getAllByRole("button", { name: /^Estornar lançamento/ })).toHaveLength(1);
    expect(screen.getByRole("button", { name: "Estornar lançamento Pagamento fornecedor" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Consolidado por período" })).toHaveAttribute("href", "/contas/CC-0001/consolidado");
    unmount();

    vi.mocked(obterSessao).mockResolvedValue({ usuarioId: "u-1", nome: "Consulta", papeis: ["consulta"] });
    render(await abrir("CC-0001"));

    expect(screen.queryByRole("region", { name: "Registrar lançamento" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Registrar" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^Estornar/ })).not.toBeInTheDocument();
  });
});
