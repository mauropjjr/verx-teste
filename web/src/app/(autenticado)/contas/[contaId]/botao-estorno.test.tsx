import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import type { EstadoEstorno } from "./acoes";
import { BotaoEstorno } from "./botao-estorno";

type Acao = (estado: EstadoEstorno, dados: FormData) => Promise<EstadoEstorno>;

const lancamento = { id: "6f1c2b9e-0000-4000-8000-000000000001", descricao: "Depósito em dinheiro", valor: 1500.5 };

describe("BotaoEstorno", () => {
  it("pede confirmação antes de estornar; cancelar não chama a ação e devolve o foco", async () => {
    const acao = vi.fn<Acao>();
    render(<BotaoEstorno contaId="CC-0001" lancamento={lancamento} chave="chave-e1" acao={acao} />);

    const estornar = screen.getByRole("button", { name: "Estornar lançamento Depósito em dinheiro" });
    await userEvent.click(estornar);

    const confirmar = screen.getByRole("button", { name: "Confirmar estorno" });
    expect(confirmar).toHaveFocus();
    expect(confirmar).toHaveAccessibleDescription(expect.stringContaining("Depósito em dinheiro"));
    expect(acao).not.toHaveBeenCalled();

    await userEvent.keyboard("{Escape}");

    expect(screen.queryByRole("button", { name: "Confirmar estorno" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Estornar lançamento Depósito em dinheiro" })).toHaveFocus();
    expect(acao).not.toHaveBeenCalled();
  });

  it("confirmado, envia conta, lançamento e chave uma única vez e anuncia \"Lançamento já estornado\"", async () => {
    const acao = vi.fn<Acao>(async (_estado, dados) => ({ chave: String(dados.get("chave")), erro: "Lançamento já estornado" }));
    render(<BotaoEstorno contaId="CC-0001" lancamento={lancamento} chave="chave-e1" acao={acao} />);

    await userEvent.click(screen.getByRole("button", { name: "Estornar lançamento Depósito em dinheiro" }));
    await userEvent.dblClick(screen.getByRole("button", { name: "Confirmar estorno" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Lançamento já estornado");
    await waitFor(() => expect(acao).toHaveBeenCalledTimes(1));
    const dados = acao.mock.calls[0][1];
    expect(dados.get("contaId")).toBe("CC-0001");
    expect(dados.get("lancamentoId")).toBe(lancamento.id);
    expect(dados.get("chave")).toBe("chave-e1");
  });
});
