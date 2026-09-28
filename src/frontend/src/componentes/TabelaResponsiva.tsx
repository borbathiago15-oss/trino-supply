import type { ReactNode } from 'react';

export interface ColunaResponsiva<T> {
  titulo: string;
  render: (linha: T) => ReactNode;
  /** A coluna que dá nome ao card no celular (o fornecedor, o comprador, o processo). */
  principal?: boolean;
  /** Classe da célula na tabela (alinhamento, `whitespace-nowrap`). */
  classe?: string;
}

/**
 * Uma tabela no computador e no tablet, e no celular um card por linha: a tabela de seis
 * colunas espremida em 375px vira rolagem lateral, e a rolagem lateral é onde a leitura para.
 *
 * As duas formas nascem das <b>mesmas</b> colunas — não há uma lista de campos para o card e
 * outra para a tabela, que é como uma coluna nova aparece numa e some da outra. O corte é por
 * CSS (`md:`), então o DOM tem as duas: quem procura pelo `testid` acha a tabela; os cards
 * ficam em `${testid}-cards`.
 */
export function TabelaResponsiva<T>({ linhas, colunas, chave, testid, minLargura = 640, colunasNoCard = 2 }: {
  linhas: T[];
  colunas: ColunaResponsiva<T>[];
  chave: (linha: T) => string;
  testid: string;
  /** Largura mínima da tabela, para as colunas não se esmagarem no tablet. */
  minLargura?: number;
  /**
   * Quantos campos por linha no card do celular. Dois cabem quando o valor é curto (um número,
   * um nome); o campo que empilha meta, real, mediana e um selo precisa da largura inteira —
   * em meia tela ele transbordava por cima do campo vizinho.
   */
  colunasNoCard?: 1 | 2;
}) {
  const principal = colunas.find((c) => c.principal) ?? colunas[0];
  const demais = colunas.filter((c) => c !== principal);
  return (
    <>
      <div className="hidden overflow-x-auto md:block">
        <table data-testid={testid} style={{ minWidth: minLargura }}>
          <thead><tr>{colunas.map((c) => <th key={c.titulo}>{c.titulo}</th>)}</tr></thead>
          <tbody>
            {linhas.map((l) => (
              <tr key={chave(l)}>
                {colunas.map((c) => <td key={c.titulo} className={c.classe}>{c.render(l)}</td>)}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <ul className="flex flex-col gap-2 md:hidden" data-testid={`${testid}-cards`}>
        {linhas.map((l) => (
          <li key={chave(l)} className="rounded-lg border border-borda bg-superficie px-3 py-2.5">
            <div className="text-[14px] font-semibold">{principal.render(l)}</div>
            {/* `min-w-0` nos dois níveis: sem ele o campo mais largo que a célula empurra o
                conteúdo por cima do vizinho em vez de quebrar a linha */}
            <dl className={`mt-1.5 grid gap-x-3 gap-y-1 text-[12.5px] ${colunasNoCard === 1 ? 'grid-cols-1' : 'grid-cols-2'}`}>
              {demais.map((c) => (
                <div key={c.titulo} className="flex min-w-0 items-baseline justify-between gap-2">
                  <dt className="shrink-0 text-texto-suave">{c.titulo}</dt>
                  <dd className="min-w-0 text-right [overflow-wrap:anywhere]">{c.render(l)}</dd>
                </div>
              ))}
            </dl>
          </li>
        ))}
      </ul>
    </>
  );
}
