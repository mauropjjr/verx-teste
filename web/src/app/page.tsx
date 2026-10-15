import { redirect } from "next/navigation";

// A página inicial do portal é a lista de contas.
export default function PaginaInicial() {
  redirect("/contas");
}
