import { useEffect, useState, type FormEvent } from 'react';
import { Link, useParams } from 'react-router-dom';
import { abrirBlob } from '@/api/cliente';
import { baixarDocumento } from '@/api/documentos';
import { listarLocais, type LocalEstoque } from '@/api/estoque';
import {
  anexarNota, anexarOc, lancarNota, obterPedido, pdfPedido, pedidoEncerrado, registrarEntrega, registrarOc,
  ROTULO_SITUACAO, type PedidoCompra,
} from '@/api/pedidos';
import { Badge, Carregando, Dado, Erro, Painel, Vazio } from '@/componentes/basicos';
import { useToast } from '@/componentes/Toast';
import { podeConfirmarEntrega, podeGerirPedidos } from '@/dominio/papeis';
import { useUsuario } from '@/sessao/SessaoProvider';
import { data, dataHora, moeda, quantidade } from '@/util/formato';
import { useCarregar } from '@/util/useCarregar';

const mensagem = (e: unknown, padrao: string) => (e instanceof Error ? e.message : padrao);

export function PedidoDetalhe() {
  const { id = '' } = useParams();
  const usuario = useUsuario();
  const { dados: pedido, erro, recarregar } = useCarregar((signal) => obterPedido(id, signal), [id]);
  const { avisar } = useToast();

  async function abrirPdf() {
    try { avisar('Gerando o PDF da OC…'); abrirBlob(await pdfPedido(id)); }
    catch (e) { avisar(mensagem(e, 'Falha ao gerar o PDF.'), 'erro'); }
  }
  async function abrirDocumento(docId: string) {
    try { abrirBlob(await baixarDocumento(docId)); }
    catch (e) { avisar(mensagem(e, 'Falha ao baixar.'), 'erro'); }
  }

  if (erro && !pedido) return <><Erro>{erro}</Erro><p className="mt-3"><Link className="text-marca underline" to="/pedidos">Voltar aos pedidos</Link></p></>;
  if (!pedido) return <Carregando />;

  const { rotulo, classe } = ROTULO_SITUACAO[pedido.status] ?? { rotulo: pedido.status, classe: '' };
  const gere = podeGerirPedidos(usuario);
  const entrega = podeConfirmarEntrega(usuario);
  const encerrado = pedidoEncerrado(pedido);

  return (
    <div data-testid="pedido-detalhe" data-situacao={pedido.status}>
      <p className="mb-3"><Link className="text-marca hover:underline" to="/pedidos">← Pedidos de compra</Link></p>

      <Painel
        titulo={<>Pedido {pedido.number} — {pedido.supplierName} <Badge classe={classe + ' ml-2 align-middle'}>{rotulo}</Badge></>}
        acoes={<button type="button" className="botao-secundario" onClick={abrirPdf}>PDF da OC</button>}>
        <div className="grid grid-cols-2 gap-4 md:grid-cols-4">
          <Dado rotulo="Total">{moeda(pedido.totalValue)}</Dado>
          <Dado rotulo="Origem">{pedido.sourcePrNumber ?? '—'}{pedido.quotationNumber && <div className="sub">{pedido.quotationNumber}</div>}</Dado>
          <Dado rotulo="Famílias">{pedido.families.length ? pedido.families.join(', ') : '—'}</Dado>
          <Dado rotulo="Emitido">{dataHora(pedido.createdAt)}{pedido.issuedByLabel && <div className="sub">por {pedido.issuedByLabel}</div>}</Dado>
          <Dado rotulo="Prazo de entrega">{pedido.deliveryDays != null ? `${pedido.deliveryDays} dia(s)` : '—'}{pedido.promisedDate && <div className="sub">até {data(pedido.promisedDate)}</div>}</Dado>
          <Dado rotulo="Pagamento">{pedido.paymentTerms ?? '—'}</Dado>
          <Dado rotulo="Frete">{pedido.freightValue != null ? moeda(pedido.freightValue) : '—'}</Dado>
          <Dado rotulo="OTIF">{pedido.otif == null ? '—' : pedido.otif ? 'No prazo e completo' : `${pedido.onTime ? 'no prazo' : 'atrasado'}, ${pedido.inFull ? 'completo' : 'incompleto'}`}</Dado>
        </div>
        {pedido.cancelReason && <p className="mt-3 rounded-lg bg-perigo-fundo px-3 py-2 text-perigo">Cancelado: {pedido.cancelReason}</p>}
        {pedido.notes && <p className="sub mt-3">{pedido.notes}</p>}
      </Painel>

      <Painel titulo="Itens do pedido">
        <div className="overflow-x-auto">
          <table>
            <thead><tr><th>Item</th><th>Família</th><th>Qtd.</th><th>Unitário</th><th>Subtotal</th><th>Recebido</th><th>Falta</th></tr></thead>
            <tbody>
              {pedido.items.map((i) => (
                <tr key={i.itemId}>
                  <td>{i.catalogCode && <span className="sub">[{i.catalogCode}] </span>}{i.description}{i.sourcePrNumber && i.sourcePrNumber !== pedido.sourcePrNumber && <div className="sub">de {i.sourcePrNumber}</div>}</td>
                  <td>{i.family ?? '—'}</td>
                  <td className="whitespace-nowrap">{quantidade(i.quantity)} {i.unitOfMeasure}</td>
                  <td className="whitespace-nowrap">{moeda(i.unitPrice)}</td>
                  <td className="whitespace-nowrap">{moeda(i.unitPrice * i.quantity)}</td>
                  <td>{quantidade(i.receivedQuantity)}</td>
                  <td>{quantidade(i.pendingQuantity)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Painel>

      <FormularioOc pedido={pedido} podeEditar={gere && !encerrado} aoSalvar={recarregar} abrirDocumento={abrirDocumento} />
      <NotasFiscais pedido={pedido} podeLancar={gere && !encerrado} aoSalvar={recarregar} abrirDocumento={abrirDocumento} />
      <Entrega pedido={pedido} podeConfirmar={entrega && !encerrado} aoSalvar={recarregar} />
    </div>
  );
}

// ---------- OC do ERP ----------
function FormularioOc({ pedido, podeEditar, aoSalvar, abrirDocumento }:
  { pedido: PedidoCompra; podeEditar: boolean; aoSalvar: () => void; abrirDocumento: (id: string) => void }) {
  const { avisar } = useToast();
  const [numero, setNumero] = useState(pedido.erpNumber ?? '');
  const [dataOc, setDataOc] = useState(pedido.erpIssuedOn ?? '');
  const [arquivo, setArquivo] = useState<File | null>(null);
  const [salvando, setSalvando] = useState(false);
  useEffect(() => { setNumero(pedido.erpNumber ?? ''); setDataOc(pedido.erpIssuedOn ?? ''); }, [pedido.erpNumber, pedido.erpIssuedOn]);

  async function salvar(ev: FormEvent) {
    ev.preventDefault();
    setSalvando(true);
    try {
      await registrarOc(pedido.id, { erpNumber: numero, issuedOn: dataOc || null });
      if (arquivo) await anexarOc(pedido.id, arquivo);
      setArquivo(null);
      avisar('OC registrada.');
      aoSalvar();
    } catch (e) { avisar(mensagem(e, 'Falha ao registrar a OC.'), 'erro'); }
    finally { setSalvando(false); }
  }

  return (
    <Painel titulo="OC do ERP" id="oc-erp">
      <p className="mb-3">
        {pedido.erpNumber ? (
          <>OC <strong>{pedido.erpNumber}</strong> de {data(pedido.erpIssuedOn)}
            {pedido.erpFileName && pedido.erpDocumentId && <> · <button type="button" className="text-marca underline" onClick={() => abrirDocumento(pedido.erpDocumentId!)}>{pedido.erpFileName}</button></>}
          </>
        ) : 'Nenhuma OC registrada ainda — informe o número gerado no ERP.'}
      </p>
      {podeEditar && (
        <form onSubmit={salvar} className="grid grid-cols-1 items-end gap-3 md:grid-cols-[1fr_170px_1fr_auto]">
          <div><label htmlFor="oc-numero">Número da OC no ERP</label><input id="oc-numero" required value={numero} onChange={(e) => setNumero(e.target.value)} /></div>
          <div><label htmlFor="oc-data">Data</label><input id="oc-data" type="date" value={dataOc} onChange={(e) => setDataOc(e.target.value)} /></div>
          <div><label htmlFor="oc-arquivo">Anexo (PDF da OC)</label><input id="oc-arquivo" type="file" onChange={(e) => setArquivo(e.target.files?.[0] ?? null)} /></div>
          <button type="submit" className="botao" disabled={salvando}>{salvando ? 'Salvando…' : 'Registrar OC'}</button>
        </form>
      )}
    </Painel>
  );
}

// ---------- notas fiscais ----------
function NotasFiscais({ pedido, podeLancar, aoSalvar, abrirDocumento }:
  { pedido: PedidoCompra; podeLancar: boolean; aoSalvar: () => void; abrirDocumento: (id: string) => void }) {
  const { avisar } = useToast();
  const [numero, setNumero] = useState('');
  const [dataNf, setDataNf] = useState('');
  const [valor, setValor] = useState('');
  const [arquivo, setArquivo] = useState<File | null>(null);
  const [salvando, setSalvando] = useState(false);

  async function lancar(ev: FormEvent) {
    ev.preventDefault();
    setSalvando(true);
    try {
      const nf = await lancarNota(pedido.id, { number: numero, issuedOn: dataNf || null, value: valor ? parseFloat(valor) : null });
      if (arquivo) await anexarNota(pedido.id, nf.id, arquivo);
      setNumero(''); setValor(''); setArquivo(null);
      avisar('Nota fiscal lançada.');
      aoSalvar();
    } catch (e) { avisar(mensagem(e, 'Falha ao lançar a nota.'), 'erro'); }
    finally { setSalvando(false); }
  }

  return (
    <Painel titulo="Faturamento (notas fiscais)" id="notas-fiscais">
      {pedido.invoices.length ? (
        <div className="overflow-x-auto">
          <table data-testid="tabela-notas">
            <thead><tr><th>NF</th><th>Data</th><th>Valor</th><th>Anexo</th><th>Lançada por</th></tr></thead>
            <tbody>
              {pedido.invoices.map((i) => (
                <tr key={i.id}>
                  <td>{i.number}</td><td>{data(i.issuedOn)}</td>
                  <td className="whitespace-nowrap">{i.value != null ? moeda(i.value) : '—'}</td>
                  <td>{i.fileName && i.documentId ? <button type="button" className="text-marca underline" onClick={() => abrirDocumento(i.documentId!)}>{i.fileName}</button> : '—'}</td>
                  <td className="sub">{i.createdByLabel ?? ''}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : <Vazio>Nenhuma nota fiscal lançada. Uma OC pode ter mais de uma.</Vazio>}
      {podeLancar && (
        <form onSubmit={lancar} className="mt-4 grid grid-cols-1 items-end gap-3 md:grid-cols-[1fr_170px_170px_1fr_auto]">
          <div><label htmlFor="nf-numero">Número da NF</label><input id="nf-numero" required value={numero} onChange={(e) => setNumero(e.target.value)} /></div>
          <div><label htmlFor="nf-data">Emissão</label><input id="nf-data" type="date" value={dataNf} onChange={(e) => setDataNf(e.target.value)} /></div>
          <div><label htmlFor="nf-valor">Valor (R$)</label><input id="nf-valor" type="number" min="0" step="0.01" value={valor} onChange={(e) => setValor(e.target.value)} /></div>
          <div><label htmlFor="nf-arquivo">Anexo (XML/PDF)</label><input id="nf-arquivo" type="file" onChange={(e) => setArquivo(e.target.files?.[0] ?? null)} /></div>
          <button type="submit" className="botao" disabled={salvando}>{salvando ? 'Lançando…' : 'Lançar NF'}</button>
        </form>
      )}
    </Painel>
  );
}

// ---------- entrega ----------
interface LinhaForm { quantidade: string; devolvido: string }

function Entrega({ pedido, podeConfirmar, aoSalvar }: { pedido: PedidoCompra; podeConfirmar: boolean; aoSalvar: () => void }) {
  const { avisar } = useToast();
  const [locais, setLocais] = useState<LocalEstoque[]>([]);
  const [local, setLocal] = useState('');
  const [linhas, setLinhas] = useState<Record<string, LinhaForm>>({});
  const [motivoDevolucao, setMotivoDevolucao] = useState('');
  const [motivoEncerramento, setMotivoEncerramento] = useState('');
  const [enviando, setEnviando] = useState(false);
  const encerrado = pedidoEncerrado(pedido);

  useEffect(() => {
    if (!podeConfirmar) return;
    let vivo = true;
    listarLocais().then((l) => { if (vivo) { setLocais(l); setLocal((atual) => atual || l[0]?.id || ''); } }).catch(() => {});
    return () => { vivo = false; };
  }, [podeConfirmar]);

  const linha = (itemId: string): LinhaForm => linhas[itemId] ?? { quantidade: '', devolvido: '' };
  const editar = (itemId: string, campo: keyof LinhaForm, v: string) =>
    setLinhas((l) => ({ ...l, [itemId]: { ...linha(itemId), [campo]: v } }));

  async function enviar(encerrarSaldo: boolean) {
    const items = pedido.items
      .map((i) => ({ itemId: i.itemId, quantity: parseFloat(linha(i.itemId).quantidade) || 0, rejected: parseFloat(linha(i.itemId).devolvido) || 0 }))
      .filter((i) => i.quantity > 0 || i.rejected > 0);
    if (!items.length && !encerrarSaldo) { avisar('Informe o que chegou.', 'erro'); return; }
    if (encerrarSaldo && !motivoEncerramento.trim()) { avisar('Informe o motivo do cancelamento do saldo.', 'erro'); return; }
    if (items.some((i) => i.rejected > 0) && !motivoDevolucao.trim()) { avisar('Informe o motivo da devolução.', 'erro'); return; }
    setEnviando(true);
    try {
      await registrarEntrega(pedido.id, {
        locationId: local || null, items, closeRemaining: encerrarSaldo,
        closeReason: motivoEncerramento || null, rejectReason: motivoDevolucao || null,
      });
      setLinhas({}); setMotivoEncerramento(''); setMotivoDevolucao('');
      avisar(encerrarSaldo ? 'Saldo encerrado.' : 'Entrega registrada.');
      aoSalvar();
    } catch (e) { avisar(mensagem(e, 'Falha ao registrar a entrega.'), 'erro'); }
    finally { setEnviando(false); }
  }

  return (
    <Painel titulo="Confirmação de entrega" id="entrega">
      {encerrado && (
        <p className="sub mb-3">
          {pedido.status === 'CANCELADO' ? 'Pedido cancelado.' : `Entrega concluída em ${dataHora(pedido.deliveryCompletedAt ?? pedido.receivedAt)}`}
          {pedido.receivedByLabel && ` por ${pedido.receivedByLabel}`}.
        </p>
      )}
      <div className="overflow-x-auto">
        <table data-testid="tabela-entrega">
          <thead><tr><th>Item</th><th>Pedido</th><th>Recebido</th><th>Devolvido</th><th>Falta</th>{podeConfirmar && <><th>Chegou agora</th><th>Devolvido agora</th></>}</tr></thead>
          <tbody>
            {pedido.items.map((i) => {
              const aberto = i.pendingQuantity > 0 && podeConfirmar;
              return (
                <tr key={i.itemId} data-item={i.itemId}>
                  <td>{i.catalogCode && <span className="sub">[{i.catalogCode}] </span>}{i.description}</td>
                  <td className="whitespace-nowrap">{quantidade(i.quantity)} {i.unitOfMeasure}</td>
                  <td>{quantidade(i.receivedQuantity)}</td>
                  <td>{i.rejectedQuantity > 0 ? <Badge classe="bg-aviso-fundo text-aviso" title={i.rejectionReason ?? ''}>{quantidade(i.rejectedQuantity)}</Badge> : '0'}</td>
                  <td>{quantidade(i.pendingQuantity)}</td>
                  {podeConfirmar && (
                    <>
                      <td><input type="number" aria-label={`Chegou agora: ${i.description}`} className="!w-[100px]" min="0" step="0.01" max={i.pendingQuantity} placeholder="0" disabled={!aberto}
                        value={linha(i.itemId).quantidade} onChange={(e) => editar(i.itemId, 'quantidade', e.target.value)} /></td>
                      <td><input type="number" aria-label={`Devolvido agora: ${i.description}`} className="!w-[100px]" min="0" step="0.01" max={i.pendingQuantity} placeholder="0" disabled={!aberto}
                        title="Chegou mas foi recusado/devolvido ao fornecedor"
                        value={linha(i.itemId).devolvido} onChange={(e) => editar(i.itemId, 'devolvido', e.target.value)} /></td>
                    </>
                  )}
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
      {podeConfirmar && (
        <div className="mt-4 grid grid-cols-1 gap-3 md:grid-cols-2">
          <div>
            <label htmlFor="entrega-local">Local de estoque</label>
            <select id="entrega-local" value={local} onChange={(e) => setLocal(e.target.value)}>
              {locais.length ? locais.map((l) => <option key={l.id} value={l.id}>{l.code} — {l.name}</option>) : <option value="">(nenhum local cadastrado)</option>}
            </select>
          </div>
          <div>
            <label htmlFor="entrega-devolucao">Motivo da devolução (obrigatório quando algo for devolvido)</label>
            <input id="entrega-devolucao" placeholder="ex.: qualidade fora do padrão, avaria no transporte, item divergente"
              value={motivoDevolucao} onChange={(e) => setMotivoDevolucao(e.target.value)} />
          </div>
          <div className="md:col-span-2 flex flex-wrap items-end gap-3">
            <button type="button" className="botao" disabled={enviando} onClick={() => enviar(false)}>Registrar entrega</button>
            <div className="flex-1 min-w-[260px]">
              <label htmlFor="entrega-encerrar">Motivo para encerrar o saldo que não vai chegar</label>
              <input id="entrega-encerrar" value={motivoEncerramento} onChange={(e) => setMotivoEncerramento(e.target.value)} />
            </div>
            <button type="button" className="botao-secundario" disabled={enviando} onClick={() => enviar(true)}>Encerrar saldo</button>
          </div>
        </div>
      )}
    </Painel>
  );
}
