import type { Metadata } from 'next';
import './globals.css';

export const metadata: Metadata = {
  title: 'Trino Platform',
  description: 'Esteira de compras do Grupo Trino',
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="pt-BR">
      <body>{children}</body>
    </html>
  );
}
