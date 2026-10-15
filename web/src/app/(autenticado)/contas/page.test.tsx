import { render, screen, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/api", () => ({
  listarContas: vi.fn(async () => [
    { id: "CC-0001", titular: "M*** S***", situacao: "ATIVA" },
    { id: "CC-0002", titular: "A*** P***", situacao: "INATIVA" },
  ]),
}));

import PaginaContas from "./page";

describe("página de contas", () => {
  it("lista as contas com titular mascarado e link para cada conta", async () => {
    render(await PaginaContas());

    expect(screen.getByRole("heading", { level: 1, name: "Contas" })).toBeInTheDocument();
    const linhas = screen.getAllByRole("row").slice(1);
    expect(linhas).toHaveLength(2);
    expect(within(linhas[0]).getByText("M*** S***")).toBeInTheDocument();
    expect(within(linhas[0]).getByRole("link", { name: "CC-0001" })).toHaveAttribute("href", "/contas/CC-0001");
    expect(within(linhas[1]).getByText("A*** P***")).toBeInTheDocument();
    expect(within(linhas[1]).getByText("Inativa")).toBeInTheDocument();
  });
});
