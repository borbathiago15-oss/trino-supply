import { useState, type ChangeEvent } from 'react';
import { Link, useParams } from 'react-router-dom';
import { abrirBlob } from '@/api/cliente';
import {
  fichaDoContrato, motivoDaCompra, ROTULO_ACONTECIMENTO,
  type AcontecimentoDoContrato, type FichaDoContrato as Ficha,
} from '@/api/contratos';
import { baixarDocumento } from '@/api/documentos';
import {
  anexarDocumento, DOCUMENTOS_DO_CONTRATO, ehDocumentoDoContrato, ROTULO_DOCUMENTO, ROTULO_HOMOLOGACAO,
  type DocumentoFornecedor, type TipoDocumento,
} from '@/api/fornecedores';
import { Badge, Carregando, Dado, Erro, Painel, Vazio } from '@/componentes/basicos';
import { Campo, Grade2 } from '@/componentes/formulario';
import { useToast } from '@/componentes/Toast';
import { podeComprar } from '@/dominio/papeis';
import { situacaoDocumento } from '@/paginas/fornecedores/PainelHomologacao';
import { useUsuario } from '@/sessao/SessaoProvider';
import { data, dataHora, moeda } from '@/util/formato';
import { useCarregar } from '@/util/useCarregar';

const mensagem = (e: unknown, padrao: string) => (e instanceof Error ? e.message : padrao);

/**
 * Os papéis do contrato primeiro — é a pergunta de quem abre a ficha —, depois as certidões,
 * cada grupo do mais novo para o mais antigo.
 */
export function documentosEmOrdem(docs: DocumentoFornecedor[]): DocumentoFornecedor[] {
  const peso = (d: DocumentoFornecedor) => (ehDocumentoDoContrato(d.type) ? 0 : 1);
  return [...docs].sort((a, b) => peso(a) - peso(b)
    || (b.validUntil ?? '').localeCompare(a.validUntil ?? '')
    || a.fileName.localeCompare(b.fileName, 'pt-BR'));
}

const COR_DO_ACONTECIMENTO: Record<AcontecimentoDoContrato['kind'], string> = {
  CRIADO: 'bg-ok',
  ALTERADO: 'bg-marca',
  ENCERRADO: 'bg-slate-400',
  DOCUMENTO_ANEXADO: 'bg-teal-600',
  DOCUMENTO_REMOVIDO: 'bg-aviso',
  REAJUSTE: 'bg-perigo',
};

/**
 * A ficha do contrato de um fornecedor: tudo o que o comprador precisa saber antes de renovar,
 * reajustar ou comprar de novo — os documentos, o que foi comprado (e se abateu o saldo) e a
 * história do contrato, contada pelo servidor.
 */
export function FichaDoContrato() {
  const { id = '' } = useParams();
  const usuario = useUsuario();
  const { avisar } = useToast();
  const { dados: ficha, erro, recarregar } = useCarregar((signal) => fichaDoContrato(id, signal), [id]);

  async function abrir(documentId: string) {
    try { abrirBlob(await baixarDocumento(documentId)); }
    catch (e) { avisar(mensagem(e, 'Falha ao baixar o documento.'), 'erro'); }
  }

  if (erro && !ficha) return <><Erro>{erro}</Erro><p className="mt-3"><Link className="text-marca underline" to="/contratos">Voltar aos contratos</Link></p></>;
  if (!ficha) return <Carregando />;

  const f = ficha.supplier;
  const c = f.contract;
  const temContrato = c.items.length > 0;
  const homologacao = ROTULO_HOMOLOGACAO[f.effectiveHomologation] ?? { rotulo: f.effectiveHomologation, classe: '' };
  const percentual = c.valueLimit ? Math.min(100, Math.round(((c.consumed ?? 0) * 100) / c.valueLimit)) : null;

  return (
    <div data-testid="ficha-contrato">
      <p className="mb-3"><Link className="text-marca hover:underline" to="/contratos">← Contratos</Link></p>

      <Painel titulo={<>
        {f.legalName}
        {temContrato && (
          <Badge classe={(c.current ? 'bg-ok-fundo text-ok' : 'bg-slate-100 text-slate-500') + ' ml-2 align-middle'}>
            {c.current ? 'VIGENTE' : 'FORA DA VIGÊNCIA'}
          </Badge>
        )}
      </>}>
        <div className="grid grid-cols-2 gap-4 md:grid-cols-4">
          <Dado rotulo="Contrato">{temContrato ? (c.number ?? 'sem número') : 'sem contrato'}</Dado>
          <Dado rotulo="Vigência">{temContrato ? `${c.validFrom ? data(c.validFrom) : 'sem início'} → ${c.validUntil ? data(c.validUntil) : 'sem fim'}` : '—'}</Dado>
          <Dado rotulo="CNPJ">{f.taxId ?? '—'}</Dado>
          <Dado rotulo="Homologação"><Badge classe={homologacao.classe}>{homologacao.rotulo}</Badge></Dado>
          <Dado rotulo="Teto">{c.valueLimit != null ? moeda(c.valueLimit) : 'sem teto'}</Dado>
          <Dado rotulo="Consumido na vigência">
            {c.valueLimit != null ? <>{moeda(c.consumed ?? 0)}{percentual != null && <span className="sub"> ({percentual}%)</span>}</> : '—'}
          </Dado>
          <Dado rotulo="Saldo">{c.balance != null ? <strong>{moeda(c.balance)}</strong> : '—'}</Dado>
          <Dado rotulo="Custo evitado em reajustes">{moeda(ficha.costAvoidanceTotal)}</Dado>
        </div>
        {(f.email || f.phone) && <p className="sub mt-3">Contato: {[f.email, f.phone].filter(Boolean).join(' · ')}</p>}
        {c.notes && <p className="sub mt-1">Observação do contrato: {c.notes}</p>}
      </Painel>

      <Documentos ficha={ficha} podeAnexar={podeComprar(usuario)} aoAbrir={abrir} aoAnexar={recarregar} />

      <Painel titulo={`Produtos do contrato (${c.items.length})`}>
        {c.items.length ? (
          <div className="overflow-x-auto">
            <table data-testid="produtos-do-contrato">
              <thead><tr><th>Produto</th><th>Unidade</th><th>Preço</th><th>Pagamento</th><th>Entrega</th></tr></thead>
              <tbody>
                {c.items.map((i) => (
                  <tr key={i.id ?? i.description}>
                    <td>{i.catalogCode && <span className="sub">[{i.catalogCode}] </span>}{i.description}{i.notes && <div className="sub">{i.notes}</div>}</td>
                    <td>{i.unitOfMeasure ?? '—'}</td>
                    <td className="whitespace-nowrap">{moeda(i.unitPrice)}</td>
                    <td>{i.paymentTerms ?? (i.paymentDays != null ? `${i.paymentDays} dias` : '—')}</td>
                    <td>{i.deliveryDays != null ? `${i.deliveryDays} dias` : '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : <Vazio>Nenhum produto contratado: o fornecedor é cotado normalmente.</Vazio>}
      </Painel>

      <Painel titulo={`Compras com o fornecedor (${ficha.purchaseOrders.length})`}>
        <p className="sub mb-2">Todas as compras, e não só as da vigência: a de antes do contrato mostra quanto se pagava sem ele. Cada linha diz se abate o saldo.</p>
        {ficha.purchaseOrders.length ? (
          <div className="overflow-x-auto">
            <table data-testid="compras-do-contrato">
              <thead><tr><th>Pedido</th><th>Emitido</th><th>O.C. do ERP</th><th>Origem</th><th>Total</th><th>Situação</th><th>No contrato</th></tr></thead>
              <tbody>
                {ficha.purchaseOrders.map((o) => (
                  <tr key={o.id} data-conta={o.countsInContract ? 'sim' : 'nao'}>
                    <td><Link className="text-marca hover:underline" to={`/pedidos/${o.id}`}>{o.number}</Link></td>
                    <td className="whitespace-nowrap">{data(o.createdAt)}</td>
                    <td>{o.erpNumber ?? '—'}</td>
                    <td className="sub">{[o.sourcePrNumber, o.quotationNumber].filter(Boolean).join(' · ') || '—'}</td>
                    <td className="whitespace-nowrap">{moeda(o.total)}</td>
                    <td>{o.status}</td>
                    <td className={o.countsInContract ? 'text-ok' : 'sub'}>{motivoDaCompra(o, temContrato)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : <Vazio>Nenhuma compra com este fornecedor ainda.</Vazio>}
      </Painel>

      <Painel titulo="Histórico do contrato">
        {!ficha.historyComplete && temContrato && (
          <p className="mb-3 rounded-lg bg-aviso-fundo px-3 py-2 text-[13px] text-aviso" data-testid="historico-incompleto">
            Este contrato foi cadastrado antes de o sistema registrar a história dele: o que aconteceu antes da primeira linha abaixo não está aqui.
          </p>
        )}
        {ficha.timeline.length ? (
          <ol className="space-y-3" data-testid="linha-do-tempo-contrato">
            {ficha.timeline.map((e, i) => (
              <li key={`${e.at}-${i}`} className="flex gap-3" data-tipo={e.kind}>
                <span className={`mt-1.5 h-2.5 w-2.5 shrink-0 rounded-full ${COR_DO_ACONTECIMENTO[e.kind] ?? 'bg-slate-400'}`} />
                <div>
                  <div className="text-[13.5px]"><strong>{ROTULO_ACONTECIMENTO[e.kind] ?? e.kind}</strong> — {e.text}</div>
                  <div className="sub">{dataHora(e.at)} · {e.by}</div>
                </div>
              </li>
            ))}
          </ol>
        ) : <Vazio>Nada registrado ainda.</Vazio>}
      </Painel>
    </div>
  );
}

function Documentos({ ficha, podeAnexar, aoAbrir, aoAnexar }: {
  ficha: Ficha; podeAnexar: boolean; aoAbrir: (documentId: string) => void; aoAnexar: () => void;
}) {
  const { avisar } = useToast();
  const [anexando, setAnexando] = useState(false);
  const [tipo, setTipo] = useState<TipoDocumento>('CONTRATO');
  const [rotulo, setRotulo] = useState('');
  const [arquivo, setArquivo] = useState<File | null>(null);
  const [ocupado, setOcupado] = useState(false);
  const docs = documentosEmOrdem(ficha.supplier.documents);

  async function anexar() {
    if (!arquivo) { avisar('Escolha o arquivo.', 'erro'); return; }
    setOcupado(true);
    try {
      // o papel do contrato não leva validade: a dele é a vigência, que já está no contrato
      await anexarDocumento(ficha.supplier.id, arquivo, tipo, '', rotulo);
      avisar('Documento anexado ao contrato.');
      setAnexando(false); setArquivo(null); setRotulo('');
      aoAnexar();
    } catch (e) { avisar(mensagem(e, 'Falha ao anexar o documento.'), 'erro'); }
    finally { setOcupado(false); }
  }

  return (
    <Painel titulo={`Documentos (${docs.length})`} acoes={podeAnexar && !anexando
      ? <button type="button" className="botao-secundario" onClick={() => setAnexando(true)}>Anexar documento do contrato</button>
      : undefined}>
      {docs.length ? (
        <div className="overflow-x-auto">
          <table data-testid="documentos-do-contrato">
            <thead><tr><th>Documento</th><th>Arquivo</th><th>Válido até</th><th>Situação</th><th>Enviado por</th></tr></thead>
            <tbody>
              {docs.map((d) => {
                const doContrato = ehDocumentoDoContrato(d.type);
                const marca = doContrato ? null : situacaoDocumento(d);
                return (
                  <tr key={d.id} data-documento={d.type}>
                    <td>
                      {ROTULO_DOCUMENTO[d.type] ?? d.type}
                      {d.label && <div className="sub">{d.label}</div>}
                    </td>
                    <td><button type="button" className="text-marca underline" onClick={() => aoAbrir(d.documentId)}>{d.fileName}</button></td>
                    <td>{d.validUntil ? data(d.validUntil) : <span className="sub">{doContrato ? 'a vigência do contrato' : 'sem validade'}</span>}</td>
                    <td>{marca ? <Badge classe={marca.classe}>{marca.rotulo}</Badge> : <span className="sub">{doContrato ? 'do contrato' : '—'}</span>}</td>
                    <td className="sub">{d.uploadedByLabel ?? '—'}</td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      ) : <Vazio>Nenhum documento anexado. O contrato assinado e os aditivos podem ser anexados aqui; as certidões, na homologação do fornecedor.</Vazio>}

      {anexando && (
        <div className="mt-4 rounded-lg border border-borda p-3" data-testid="anexar-documento-contrato">
          <Grade2>
            <Campo id="ficha-doc-tipo" rotulo="Documento">
              <select id="ficha-doc-tipo" value={tipo} onChange={(ev) => setTipo(ev.target.value as TipoDocumento)}>
                {[...DOCUMENTOS_DO_CONTRATO, 'OUTRO' as TipoDocumento].map((t) => <option key={t} value={t}>{ROTULO_DOCUMENTO[t]}</option>)}
              </select>
            </Campo>
            <Campo id="ficha-doc-arquivo" rotulo="Arquivo">
              <input id="ficha-doc-arquivo" type="file" accept=".pdf,.png,.jpg,.jpeg,.docx"
                onChange={(ev: ChangeEvent<HTMLInputElement>) => setArquivo(ev.target.files?.[0] ?? null)} />
            </Campo>
          </Grade2>
          <Campo id="ficha-doc-rotulo" rotulo="Descrição" dica="(opcional — ex.: 1º aditivo, renovação 2027)" className="mt-3">
            <input id="ficha-doc-rotulo" value={rotulo} onChange={(ev) => setRotulo(ev.target.value)} />
          </Campo>
          <div className="mt-3 flex gap-2">
            <button type="button" className="botao" disabled={ocupado} onClick={anexar}>{ocupado ? 'Anexando…' : 'Anexar'}</button>
            <button type="button" className="botao-secundario" onClick={() => setAnexando(false)}>Cancelar</button>
          </div>
        </div>
      )}
    </Painel>
  );
}
