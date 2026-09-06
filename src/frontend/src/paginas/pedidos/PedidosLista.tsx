import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { abrirBlob } from '@/api/cliente';
import { baixarDocumento } from '@/api/documentos';
import { listarPedidos, pdfPedido, ROTULO_SITUACAO, totalPedido, totalRecebido, type SituacaoPedido } from '@/api/pedidos';
import { Badge, Carregando, Erro, Painel, Vazio } from '@/componentes/basicos';
import { useToast } from '@/componentes/Toast';
import { data, moeda, quantidade } from '@/util/formato';
import { useCarregar } from '@/util/useCarregar';
import { useDebounce } from '@/util/useDebounce';

const SITUACOES = Object.keys(ROTULO_SITUACAO) as SituacaoPedido[];

const POR_PAGINA = 50;

export function PedidosLista() {
  const [busca, setBusca] = useState('');
  const [situacao, setSituacao] = useState<SituacaoPedido | ''>('');
  const [tamanho, setTamanho] = useState(POR_PAGINA);
  const navegar = useNavigate();
  const { avisar } = useToast();

  // a busca é do servidor: filtrar no navegador esconderia o que não coube na
  // lista, e a tela responderia "nada encontrado" para pedido que existe
  const termo = useDebounce(busca);
  const { dados, erro, carregando } = useCarregar(
    (signal) => listarPedidos({ busca: termo, situacao, tamanho }, signal),
    [termo, situacao, tamanho],
  );

  const lista = dados?.itens ?? [];
  const total = dados?.total ?? 0;
  const filtrando = termo.trim().length > 0 || situacao !== '';

  const mudarFiltro = (aplicar: () => void) => { setTamanho(POR_PAGINA); aplicar(); };

  async function abrirPdf(id: string) {
    try {
      avisar('Gerando o PDF da OC…');
      abrirBlob(await pdfPedido(id));
    } catch (e) { avisar(e instanceof Error ? e.message : 'Falha ao gerar o PDF.', 'erro'); }
  }
  async function abrirDocumento(id: string) {
    try { abrirBlob(await baixarDocumento(id)); }
    catch (e) { avisar(e instanceof Error ? e.message : 'Falha ao baixar.', 'erro'); }
  }

  return (
    <Painel
      titulo="Pedidos de compra"
      acoes={
        <>
          <input aria-label="Buscar" placeholder="Buscar por pedido, OC, fornecedor, SC ou NF"
            className="!w-[300px]" value={busca} onChange={(e) => mudarFiltro(() => setBusca(e.target.value))} />
          <select aria-label="Situação" className="!w-[190px]" value={situacao} onChange={(e) => mudarFiltro(() => setSituacao(e.target.value as SituacaoPedido | ''))}>
            <option value="">Todas as situações</option>
            {SITUACOES.map((s) => <option key={s} value={s}>{ROTULO_SITUACAO[s].rotulo}</option>)}
          </select>
        </>
      }>
      {erro && <Erro>{erro}</Erro>}
      {carregando && !dados && <Carregando />}
      {dados && !lista.length && (
        <Vazio>{filtrando
          ? 'Nenhum pedido corresponde ao filtro.'
          : 'Nenhum pedido de compra ainda. Eles nascem do processo de cotação aprovado.'}</Vazio>
      )}
      {lista.length > 0 && (
        <div className="overflow-x-auto">
          <table data-testid="tabela-pedidos">
            <thead>
              <tr><th>Pedido</th><th>OC (ERP)</th><th>Fornecedor</th><th>Itens</th><th>Total</th><th>Situação</th><th>Ações</th></tr>
            </thead>
            <tbody>
              {lista.map((o) => {
                const { rotulo, classe } = ROTULO_SITUACAO[o.status] ?? { rotulo: o.status, classe: '' };
                return (
                  <tr key={o.id} data-pedido={o.number}>
                    <td>
                      <Link to={`/pedidos/${o.id}`} className="font-semibold text-marca hover:underline">{o.number}</Link>
                      {o.sourcePrNumber && <div className="sub">de {o.sourcePrNumber}</div>}
                      {o.families.length > 0 && <div className="sub">{o.families.join(', ')}</div>}
                    </td>
                    <td>
                      {o.erpNumber ? (
                        <>
                          <strong>{o.erpNumber}</strong>
                          <div className="sub">
                            {data(o.erpIssuedOn)}
                            {o.erpFileName && o.erpDocumentId && (
                              <> · <button type="button" className="text-marca" title={o.erpFileName} onClick={() => abrirDocumento(o.erpDocumentId!)}>📎</button></>
                            )}
                          </div>
                        </>
                      ) : <span className="sub">a registrar</span>}
                    </td>
                    <td>
                      {o.supplierName}
                      {o.invoices.length > 0 && <div className="sub">NF: {o.invoices.map((i) => i.number).join(', ')}</div>}
                    </td>
                    <td>
                      {o.items.length} item(ns)
                      <div className="sub">recebido {quantidade(totalRecebido(o.items))} de {quantidade(totalPedido(o.items))}</div>
                    </td>
                    <td className="whitespace-nowrap">{moeda(o.totalValue)}</td>
                    <td>
                      <Badge classe={classe}>{rotulo}</Badge>
                      {o.cancelReason && <div className="sub">{o.cancelReason}</div>}
                    </td>
                    <td className="whitespace-nowrap">
                      <div className="flex gap-1.5">
                        <button type="button" className="botao !py-1.5" onClick={() => navegar(`/pedidos/${o.id}`)}>Abrir</button>
                        <button type="button" className="botao-secundario !py-1.5" onClick={() => abrirPdf(o.id)}>PDF</button>
                      </div>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
      {lista.length > 0 && (
        <div className="mt-3 flex items-center justify-between gap-3">
          <span className="sub" data-testid="contagem-pedidos">
            Mostrando {lista.length} de {total} pedido(s).
          </span>
          {lista.length < total && (
            <button type="button" className="botao-secundario" disabled={carregando}
              onClick={() => setTamanho((t) => t + POR_PAGINA)}>
              {carregando ? 'Carregando…' : 'Carregar mais'}
            </button>
          )}
        </div>
      )}
    </Painel>
  );
}
