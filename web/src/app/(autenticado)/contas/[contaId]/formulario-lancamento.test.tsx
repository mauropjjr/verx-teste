import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import type { EstadoLancamento } from "./acoes";
import { FormularioLancamento } from "./formulario-lancamento";

type Acao = (estado: EstadoLancamento, dados: FormData) => Promise<EstadoLancamento>;

async function preencher(valor: string) {
  await userEvent.click(screen.getByRole("radio", { name: "Crédito" }));
  await userEvent.type(screen.getByLabelText("Valor (R$)"), valor);
  await userEvent.type(screen.getByLabelText("Descrição"), "Depósito");
}

describe("FormularioLancamento", () => {
  it("anuncia os erros por campo devolvidos pela ação e reenvia a mesma Idempotency-Key na retentativa", async () => {
    const acao = vi.fn<Acao>(async (_estado, dados) => ({
      chave: String(dados.get("chave")),
      erros: { valor: "O valor deve ser maior que zero." },
      valores: { tipo: "CREDITO", valor: "0", descricao: "Depósito" },
    }));
    render(<FormularioLancamento contaId="CC-0001" estadoInicial={{ chave: "chave-aberta" }} acao={acao} />);

    await preencher("0");
    await userEvent.click(screen.getByRole("button", { name: "Registrar" }));

    const valor = await screen.findByLabelText("Valor (R$)");
    await waitFor(() => expect(valor).toHaveAttribute("aria-invalid", "true"));
    expect(valor).toHaveAccessibleDescription(expect.stringContaining("O valor deve ser maior que zero."));
    expect(valor).toHaveFocus();
    await waitFor(() => expect(screen.getByRole("alert")).toHaveTextContent("Corrija os campos indicados"));
    // Os valores digitados continuam no formulário para a correção.
    expect(screen.getByLabelText("Descrição")).toHaveValue("Depósito");
    expect(screen.getByRole("radio", { name: "Crédito" })).toBeChecked();

    await userEvent.click(screen.getByRole("button", { name: "Registrar" }));

    await waitFor(() => expect(acao).toHaveBeenCalledTimes(2));
    const primeiro = acao.mock.calls[0][1];
    expect(primeiro.get("contaId")).toBe("CC-0001");
    expect(primeiro.get("chave")).toBe("chave-aberta");
    expect(primeiro.get("tipo")).toBe("CREDITO");
    expect(primeiro.get("valor")).toBe("0");
    expect(acao.mock.calls[1][1].get("chave")).toBe("chave-aberta");
  });

  it("duplo clique em Registrar faz uma única chamada; botão desabilitado no envio; após sucesso, confirmação e chave nova", async () => {
    let concluir: (estado: EstadoLancamento) => void = () => {};
    const acao = vi.fn<Acao>(() => new Promise((resolver) => (concluir = resolver)));
    render(<FormularioLancamento contaId="CC-0001" estadoInicial={{ chave: "chave-aberta" }} acao={acao} />);

    await preencher("10,50");
    await userEvent.dblClick(screen.getByRole("button", { name: "Registrar" }));

    expect(await screen.findByRole("button", { name: "Registrando…" })).toBeDisabled();
    expect(acao).toHaveBeenCalledTimes(1);

    concluir({ chave: "chave-nova", sucesso: "Lançamento registrado: crédito de R$ 10,50." });

    await waitFor(() => expect(screen.getByRole("status")).toHaveTextContent("Lançamento registrado: crédito de R$ 10,50."));
    expect(await screen.findByRole("button", { name: "Registrar" })).toBeEnabled();
    expect(screen.getByLabelText("Valor (R$)")).toHaveValue("");

    acao.mockResolvedValue({ chave: "chave-nova" });
    await preencher("1");
    await userEvent.click(screen.getByRole("button", { name: "Registrar" }));
    await waitFor(() => expect(acao).toHaveBeenCalledTimes(2));
    expect(acao.mock.calls[1][1].get("chave")).toBe("chave-nova");
  });
});
