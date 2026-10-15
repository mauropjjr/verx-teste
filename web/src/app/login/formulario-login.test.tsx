import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import type { EstadoLogin } from "./acoes";
import { FormularioLogin } from "./formulario-login";

describe("FormularioLogin", () => {
  it("envia login, senha e retorno, anuncia o erro genérico quando a ação recusa e mantém o usuário digitado", async () => {
    const acao = vi.fn<(estado: EstadoLogin, dados: FormData) => Promise<EstadoLogin>>(async () => ({
      erro: "Usuário ou senha inválidos",
      login: "operador",
    }));
    render(<FormularioLogin acao={acao} retorno="/contas/CC-0003" />);

    await userEvent.type(screen.getByLabelText("Usuário"), "operador");
    await userEvent.type(screen.getByLabelText("Senha"), "errada");
    await userEvent.click(screen.getByRole("button", { name: "Entrar" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Usuário ou senha inválidos");
    // Após a recusa o formulário é reiniciado pelo React, mas o usuário digitado continua lá (só a senha some).
    expect(screen.getByLabelText("Usuário")).toHaveValue("operador");
    expect(screen.getByLabelText("Senha")).toHaveValue("");
    const dados = acao.mock.calls[0][1];
    expect(dados.get("login")).toBe("operador");
    expect(dados.get("senha")).toBe("errada");
    expect(dados.get("retorno")).toBe("/contas/CC-0003");
  });
});
