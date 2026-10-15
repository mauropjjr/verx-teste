import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "Banco — Portal de lançamentos",
  description: "Registro de lançamentos e consulta de extrato, consolidado diário e saldo.",
};

export default function LayoutRaiz({ children }: LayoutProps<"/">) {
  return (
    <html lang="pt-BR" className="h-full antialiased">
      <body className="min-h-full flex flex-col">{children}</body>
    </html>
  );
}
