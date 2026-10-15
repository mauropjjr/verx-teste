import { render, screen, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { consultarConsolidadoDiario, consultarSaldo } from "@/lib/api";

vi.mock("@/lib/api", async () => {
  class ErroApi extends Error {
    constructor(readonly status: number) {
      super(`A API respondeu ${status}.`);
    }
  }
  return { ErroApi, consultarConsolidadoDiario: vi.fn(), consultarSaldo: vi.fn() };
});

import PaginaConsolidado from "./page";

function abrir(busca: Record<string, string> = {}) {
  return PaginaConsolidado({ params: Promise.resolve({ contaId: "CC-0001" }), searchParams: Promise.resolve(busca) });
}

function normalizar(texto: string | null | undefined): string {
  return (texto ?? "").replace(/\s/g, " ");
}

describe("página de consolidado por período", () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ["Date"] });
    vi.setSystemTime(new Date("2026-10-13T15:00:00Z"));
    vi.mocked(consultarConsolidadoDiario).mockResolvedValue({
      contaId: "CC-0001",
      dias: [
        { dia: "2026-10-12", creditos: 1000, debitos: 250.5, saldoDia: 749.5, fechamento: 10749.5 },
        { dia: "2026-10-13", creditos: 0, debitos: 49.5, saldoDia: -49.5, fechamento: 10700 },
      ],
      atualizadoAte: "2026-10-13T14:59:00Z",
    });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("sem filtro consulta os últimos 30 dias e mostra a tabela por dia com \"Dados atualizados até\"", async () => {
    render(await abrir());

    expect(consultarConsolidadoDiario).toHaveBeenCalledWith("CC-0001", "2026-09-14", "2026-10-13");
    expect(screen.getByLabelText("De")).toHaveValue("2026-09-14");
    expect(screen.getByLabelText("Até")).toHaveValue("2026-10-13");

    const tabela = screen.getByRole("table", { name: "Consolidado diário de 14/09/2026 a 13/10/2026" });
    const cabecalhos = within(tabela).getAllByRole("columnheader").map((th) => th.textContent);
    expect(cabecalhos).toEqual(["Dia", "Créditos", "Débitos", "Saldo do dia", "Fechamento"]);
    const linhas = within(tabela).getAllByRole("row").slice(1);
    expect(linhas).toHaveLength(2);
    expect(within(linhas[0]).getByRole("rowheader")).toHaveTextContent("12/10/2026");
    expect(normalizar(linhas[0].textContent)).toContain("R$ 1.000,00");
    expect(normalizar(linhas[0].textContent)).toContain("R$ 250,50");
    expect(normalizar(linhas[0].textContent)).toContain("R$ 10.749,50");
    expect(normalizar(linhas[1].textContent)).toContain("-R$ 49,50");
    expect(normalizar(screen.getByText(/Dados atualizados até/).textContent)).toBe("Dados atualizados até 13/10/2026, 11:59");
  });

  it("período inválido mostra o erro no campo sem consultar a API", async () => {
    render(await abrir({ de: "2025-01-01", ate: "2026-10-13" }));

    expect(consultarConsolidadoDiario).not.toHaveBeenCalled();
    expect(screen.getByRole("alert")).toHaveTextContent("O período pode ter no máximo 366 dias.");
    expect(screen.getByLabelText("Até")).toHaveAttribute("aria-invalid", "true");
    expect(screen.getByLabelText("Até")).toHaveAccessibleDescription("O período pode ter no máximo 366 dias.");
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });

  it("consolidado fora do ar mostra aviso de indisponibilidade; o saldo no instante escolhido é convertido para UTC", async () => {
    vi.mocked(consultarConsolidadoDiario).mockRejectedValue(new Error("consolidado fora do ar"));
    vi.mocked(consultarSaldo).mockResolvedValue({
      contaId: "CC-0001",
      saldo: 4321.09,
      em: "2026-10-01T15:00:00Z",
      atualizadoAte: "2026-10-13T14:59:00Z",
    });

    render(await abrir({ de: "2026-10-01", ate: "2026-10-13", em: "2026-10-01T12:00" }));

    const periodo = screen.getByRole("region", { name: "Consolidado diário" });
    expect(within(periodo).getByRole("status")).toHaveTextContent("Consolidado temporariamente indisponível");
    expect(consultarSaldo).toHaveBeenCalledWith("CC-0001", "2026-10-01T15:00:00.000Z");
    const saldo = screen.getByRole("region", { name: "Saldo em um instante" });
    expect(screen.getByLabelText("Instante (horário de Brasília)")).toHaveValue("2026-10-01T12:00");
    expect(normalizar(saldo.textContent)).toContain("R$ 4.321,09");
    expect(normalizar(saldo.textContent)).toContain("01/10/2026, 12:00");
    expect(normalizar(saldo.textContent)).toContain("Dados atualizados até 13/10/2026, 11:59");
  });
});
