import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { abrirBlob } from '@/api/cliente';
import {
  acoesDisponiveis, cancelarProcesso, convidarFornecedor, decidir, encerrarParaAnalise,
  lerProcesso, propostasVigentes, registrarNegociacao, ROTULO_RFQ,
  type Alcada, type Decisao, type Processo,
} from '@/api/cotacoes';
import { baixarDocumento } from '@/api/documentos';
import { listarFornecedores } from '@/api/fornecedores';
import { Aviso, Badge, Carregando, Dado, Erro, Painel, Vazio } from '@/componentes/basicos';
import { Confirmacao } from '@/componentes/Dialogo';
import { DialogoMotivo } from '@/componentes/DialogoMotivo';
import { Campo, Grade2, Nota } from '@/componentes/formulario';
import { useToast } from '@/componentes/Toast';
import { podeAprovarDiretor, podeAprovarGerente, podeConduzirCotacao } from '@/dominio/papeis';
import { useUsuario } from '@/sessao/SessaoProvider';
import { data, dataHora, moeda, quantidade } from '@/util/formato';
import { useCarregar } from '@/util/useCarregar';
import { FormAdjudicacao, FormRegistroOc, FormVencedor } from './AcoesDoProcesso';
import { MapaDeCotacao } from './MapaDeCotacao';
import { PainelPropostaManual } from './PainelPropostaManual';
import { ProximoPasso } from './ProximoPasso';

const mensagem = (e: unknown, padrao: string) => (e instanceof Error ? e.message : padrao);

/** Texto do convite que o comprador manda ao fornecedor. */
export function textoDoConvite(q: Processo, origem: string) {
  return `Prezado fornecedor,\n\n`
    + `Convidamos sua empresa a participar da cotação ${q.number} (${q.kind}).\n`
    + `Prazo para resposta: ${q.deadline ?? 'a combinar'}.\n`
    + `Acesse o Portal do Fornecedor em ${origem}/portal, informe seu CNPJ e a chave de acesso `
    + `fornecida pelo nosso time de Suprimentos e localize a cotação pelo número ${q.number}.\n\n`
    + `Atenciosamente,\nSuprimentos — Trino Supply`;
}

type Pendente =
  | { tipo: 'cancelar' }
  | { tipo: 'encerrar' }
  | { tipo: 'decidir'; alcada: Alcada; decisao: Decisao };

export function ProcessoDetalhe() {
  const { id = '' } = useParams();
  const usuario = useUsuario();
  const { avisar } = useToast();
  const [pendente, setPendente] = useState<Pendente | null>(null);
  const [convidado, setConvidado] = useState('');
  const [negociacao, setNegociacao] = useState({ fornecedor: '', valor: '', percentual: '', notas: '' });

  const { dados, erro, carregando, recarregar } = useCarregar(
    (signal) => lerProcesso(id, signal), [id]);
  const catalogo = useCarregar(
    async (signal) => listarFornecedores(false, signal).catch(() => []), []);

  if (erro) return <Painel><Erro>{erro}</Erro></Painel>;
  if (!dados) return <Painel>{carregando && <Carregando texto="Abrindo o processo…" />}</Painel>;

  const q = dados;
  const marca = ROTULO_RFQ[q.status] ?? { rotulo: q.status, classe: 'bg-slate-100 text-slate-600' };
  const pode = acoesDisponiveis(q, {
    conduz: podeConduzirCotacao(usuario),
    aprovaNivel1: podeAprovarGerente(usuario),
    aprovaNivel2: podeAprovarDiretor(usuario),
    de: usuario?.id,
  });
  const vigentes = propostasVigentes(q);
  const origem = q.sourcePrNumbers.length ? q.sourcePrNumbers : (q.sourcePrNumber ? [q.sourcePrNumber] : []);
  const naoConvidados = (catalogo.dados ?? [])
    .filter((f) => !q.suppliers.some((s) => s.supplierId === f.id));

  async function baixarAnexo(documentId: string) {
    try { abrirBlob(await baixarDocumento(documentId)); }
    catch (e) { avisar(mensagem(e, 'Falha ao baixar o anexo.'), 'erro'); }
  }

  async function convidar() {
    if (!convidado) return;
    try {
      await convidarFornecedor(q.id, [convidado]);
      avisar('Fornecedor convidado.');
      setConvidado('');
      recarregar();
    } catch (e) { avisar(mensagem(e, 'Falha ao convidar o fornecedor.'), 'erro'); }
  }

  async function copiarConvite(nome: string) {
    const texto = textoDoConvite(q, globalThis.location.origin);
    try {
      await navigator.clipboard.writeText(texto);
      avisar(`Convite de ${nome} copiado.`);
    } catch { avisar('Não foi possível copiar. Selecione o texto do convite manualmente.', 'erro'); }
  }

  async function negociar() {
    const valor = Number(negociacao.valor.replace(',', '.'));
    const percentual = Number(negociacao.percentual.replace(',', '.'));
    const temValor = negociacao.valor.trim() && !Number.isNaN(valor);
    const temPercentual = negociacao.percentual.trim() && !Number.isNaN(percentual);
    if (!negociacao.fornecedor) { avisar('Escolha o fornecedor negociado.', 'erro'); return; }
    if (!temValor && !temPercentual) {
      avisar('Informe o valor fechado ou o desconto negociado.', 'erro');
      return;
    }
    try {
      await registrarNegociacao(q.id, {
        supplierId: negociacao.fornecedor,
        closedValue: temValor ? valor : null,
        discountPercent: !temValor && temPercentual ? percentual : null,
        notes: negociacao.notas || null,
      });
      avisar('Negociação registrada. O mapa passa a comparar pelo valor fechado.');
      setNegociacao({ fornecedor: '', valor: '', percentual: '', notas: '' });
      recarregar();
    } catch (e) { avisar(mensagem(e, 'Falha ao registrar a negociação.'), 'erro'); }
  }

  async function concluirPendente(motivo: string) {
    const acao = pendente;
    setPendente(null);
    if (!acao) return;
    try {
      if (acao.tipo === 'cancelar') {
        await cancelarProcesso(q.id, motivo);
        avisar('Processo cancelado.');
      } else if (acao.tipo === 'encerrar') {
        await encerrarParaAnalise(q.id);
        avisar('Cotação encerrada. As propostas estão em análise.');
      } else {
        await decidir(q.id, acao.alcada, acao.decisao, motivo || null);
        avisar(acao.decisao === 'APROVAR' ? 'Processo aprovado.'
          : acao.decisao === 'AJUSTES' ? 'Ajustes solicitados ao comprador.' : 'Processo rejeitado.');
      }
      recarregar();
    } catch (e) { avisar(mensagem(e, 'Falha ao concluir a ação.'), 'erro'); }
  }

  const campoNegociacao = (k: keyof typeof negociacao) => ({
    value: negociacao[k],
    onChange: (e: { target: { value: string } }) => setNegociacao((n) => ({ ...n, [k]: e.target.value })),
  });

  const alcadaPendente: Alcada | null = pode.decidirNivel1 ? 'manager' : pode.decidirNivel2 ? 'director' : null;

  return (
    <>
      <Painel titulo={
        <span className="flex flex-wrap items-center gap-2">
          {q.number} <Badge classe={marca.classe}>{marca.rotulo}</Badge>
        </span>
      } acoes={<Link className="botao-secundario" to="/cotacoes">← Voltar</Link>}>
        <div className="grid grid-cols-2 gap-4 md:grid-cols-4">
          <Dado rotulo="Tipo">{q.kind}</Dado>
          <Dado rotulo="Origem">{origem.join(', ') || '—'}</Dado>
          <Dado rotulo="Centro de custo">{q.costCenter}</Dado>
          <Dado rotulo="Prazo">{data(q.deadline)}</Dado>
        </div>
        <Nota>Aberta por {q.createdByLabel ?? '—'} em {dataHora(q.createdAt)}.</Nota>
        <ProximoPasso processo={q} />
        {q.decisionReason && (
          <p className="mt-2 rounded-lg bg-aviso-fundo px-3 py-2 text-[13px] text-aviso">
            Último motivo registrado: <strong>{q.decisionReason}</strong>
          </p>
        )}

        <h3 className="mb-2 mt-4 text-[14px] font-bold">Itens da cotação</h3>
        <div className="overflow-x-auto">
          <table data-testid="itens-cotacao">
            <thead>
              <tr>
                <th>#</th><th>Descrição</th>
                {q.families.length > 1 && <th>Família</th>}
                <th>Qtd</th><th>Unid.</th>
                {origem.length > 1 && <th>SC de origem</th>}
              </tr>
            </thead>
            <tbody>
              {q.items.map((i) => {
                const award = q.awards.find((a) => a.family === i.family);
                return (
                  <tr key={i.id}>
                    <td>{i.sequence}</td>
                    <td>
                      {i.description}
                      {i.catalogCode && <div className="sub">{i.catalogCode}</div>}
                    </td>
                    {q.families.length > 1 && (
                      <td>
                        {i.family || '—'}
                        {award && <div className="sub">{award.supplierName}</div>}
                      </td>
                    )}
                    <td className="whitespace-nowrap">{quantidade(i.quantity)}</td>
                    <td>{i.unitOfMeasure}</td>
                    {origem.length > 1 && <td className="sub">{i.sourcePrNumber || '—'}</td>}
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      </Painel>

      <Painel titulo="Fornecedores convidados">
        {!q.suppliers.length && <Vazio>Nenhum fornecedor convidado ainda.</Vazio>}
        {q.suppliers.length > 0 && (
          <div className="overflow-x-auto">
            <table data-testid="fornecedores-convidados">
              <thead>
                <tr><th>Fornecedor</th><th>CNPJ</th><th>Convite</th><th>Proposta</th><th></th></tr>
              </thead>
              <tbody>
                {q.suppliers.map((s) => (
                  <tr key={s.supplierId} data-fornecedor={s.supplierName}>
                    <td>{s.supplierName}</td>
                    <td>{s.taxId}</td>
                    <td className="sub">{data(s.invitedAt)} por {s.invitedByLabel ?? '—'}</td>
                    <td>
                      {s.hasProposal
                        ? <Badge classe="bg-ok-fundo text-ok">RECEBIDA</Badge>
                        : <Badge classe="bg-slate-100 text-slate-600">AGUARDANDO</Badge>}
                    </td>
                    <td>
                      <button type="button" className="botao-secundario" onClick={() => copiarConvite(s.supplierName)}>
                        Copiar convite
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {pode.convidar && (
          <>
            <div className="mt-3 flex flex-wrap items-end gap-2">
              <Campo id="rfq-convidar" rotulo="Convidar fornecedor" className="min-w-[260px]">
                <select id="rfq-convidar" value={convidado} onChange={(e) => setConvidado(e.target.value)}>
                  <option value="">Escolha o fornecedor…</option>
                  {naoConvidados.map((f) => (
                    <option key={f.id} value={f.id}>{f.tradeName || f.legalName}</option>
                  ))}
                </select>
              </Campo>
              <button type="button" className="botao" disabled={!convidado} onClick={convidar}>Convidar</button>
            </div>
            <Nota>
              O fornecedor responde pelo Portal com CNPJ + chave de acesso — gere a chave em
              Cadastros → Fornecedores. O convite fica registrado na auditoria do processo.
            </Nota>
          </>
        )}
      </Painel>

      <Painel titulo="Mapa de cotação">
        <MapaDeCotacao processo={q} aoBaixarAnexo={(docId) => baixarAnexo(docId)} />
        {pode.registrarProposta && (
          <PainelPropostaManual processo={q} aoRegistrar={recarregar}
            aoAvisar={(t, tipo) => avisar(t, tipo === 'erro' ? 'erro' : 'ok')} />
        )}
      </Painel>

      <Painel titulo="Negociação e ganho">
        {q.saving
          ? <div className={'rounded-lg border px-4 py-3 ' + (q.saving.value > 0 ? 'border-ok/30 bg-ok-fundo' : 'border-borda bg-superficie-suave')}>
              <strong>
                Ganho de negociação: {moeda(q.saving.value)}
                {q.saving.percent != null && ` (${q.saving.percent}%)`}
              </strong>
              <div className="sub">
                Primeira proposta {moeda(q.saving.baselineValue)} → fechado {moeda(q.saving.closedValue)}
                {q.saving.byLabel && ` · negociado por ${q.saving.byLabel}`}
                {q.saving.notes && ` · “${q.saving.notes}”`}
              </div>
            </div>
          : <Vazio>Nenhuma negociação registrada. O ganho é medido contra a primeira proposta do fornecedor.</Vazio>}

        {pode.negociar && (
          <div className="mt-4 border-t border-borda pt-4" data-testid="form-negociacao">
            <Grade2>
              <Campo id="ng-fornecedor" rotulo="Fornecedor negociado">
                <select id="ng-fornecedor" {...campoNegociacao('fornecedor')}>
                  <option value="">Escolha o fornecedor…</option>
                  {vigentes.map((p) => (
                    <option key={p.supplierId} value={p.supplierId}>
                      {p.supplierName} — proposta atual {moeda(p.totalValue)}
                    </option>
                  ))}
                </select>
              </Campo>
              <Grade2>
                <Campo id="ng-valor" rotulo="Valor fechado (R$)">
                  <input id="ng-valor" type="number" min="0" step="0.01" placeholder="ex.: 95000" {...campoNegociacao('valor')} />
                </Campo>
                <Campo id="ng-percentual" rotulo="ou desconto (%)">
                  <input id="ng-percentual" type="number" min="0" max="99.99" step="0.01" placeholder="ex.: 5" {...campoNegociacao('percentual')} />
                </Campo>
              </Grade2>
            </Grade2>
            <Campo id="ng-notas" rotulo="O que foi negociado" className="mt-3">
              <input id="ng-notas" placeholder="ex.: 5% de desconto após negociação de prazo" {...campoNegociacao('notas')} />
            </Campo>
            <button type="button" className="botao mt-3" onClick={negociar}>Registrar negociação</button>
            <Nota>O valor fechado entra como nova versão da proposta e o mapa passa a comparar por ele.</Nota>
          </div>
        )}
      </Painel>

      <Painel titulo="Ações da etapa atual">
        {q.selection && (
          <p className="sub mb-3">
            Fornecedor selecionado por <strong>{q.selection.byLabel ?? '—'}</strong> — critérios:{' '}
            {q.selection.criteria || '—'} · justificativa: “{q.selection.justification}”
          </p>
        )}
        {q.awards.length > 0 && (q.splitAward || q.families.length > 1) && (
          <>
            <h3 className="mb-2 text-[14px] font-bold">
              Adjudicação por família
              {q.splitAward && <> <Badge classe="bg-aviso-fundo text-aviso">compra dividida</Badge></>}
            </h3>
            <div className="overflow-x-auto">
              <table data-testid="tabela-adjudicacao">
                <thead>
                  <tr><th>Família</th><th>Fornecedor</th><th>Valor</th><th>O.C.</th><th>Justificativa</th></tr>
                </thead>
                <tbody>
                  {q.awards.map((a) => (
                    <tr key={a.id}>
                      <td><strong>{a.family}</strong></td>
                      <td>{a.supplierName} <span className="sub">v{a.proposalVersion}</span></td>
                      <td className="whitespace-nowrap">{moeda(a.totalValue)}</td>
                      <td>
                        {a.purchaseOrderNumber
                          ? <Badge classe="bg-ok-fundo text-ok">{a.purchaseOrderNumber}</Badge>
                          : <Badge classe="bg-slate-100 text-slate-600">pendente</Badge>}
                      </td>
                      <td className="sub">{a.justification || '—'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <Nota>
              Total da compra: <strong>{moeda(q.awards.reduce((t, a) => t + a.totalValue, 0))}</strong>
              {q.splitAward && ` em ${new Set(q.awards.map((a) => a.supplierId)).size} fornecedores`}.
            </Nota>
          </>
        )}
        {q.managerApproval && <p className="sub mt-2">Aprovador 01 (Nível 1): <strong>{q.managerApproval.byLabel ?? '—'}</strong></p>}
        {q.directorApproval && <p className="sub">Aprovador 02 (Nível 2): <strong>{q.directorApproval.byLabel ?? '—'}</strong></p>}

        <div className="mt-3 flex flex-col gap-4">
          {pode.encerrar && (
            <div>
              <button type="button" className="botao" onClick={() => setPendente({ tipo: 'encerrar' })}>
                Encerrar para análise
              </button>
              <Nota>Depois de encerrar, o processo para de aceitar propostas novas pelo portal.</Nota>
            </div>
          )}

          {pode.escolherVencedor && (pode.porFamilia
            ? <FormAdjudicacao processo={q} aoConcluir={recarregar} aoAvisar={(t, tipo) => avisar(t, tipo === 'erro' ? 'erro' : 'ok')} />
            : <FormVencedor processo={q} aoConcluir={recarregar} aoAvisar={(t, tipo) => avisar(t, tipo === 'erro' ? 'erro' : 'ok')} />)}

          {pode.conflitoSegregacao && (
            <Aviso testid="conflito-segregacao">{pode.conflitoSegregacao}</Aviso>
          )}

          {alcadaPendente && (
            <div className="flex flex-wrap gap-2" data-testid="acoes-aprovacao">
              <button type="button" className="botao"
                onClick={() => setPendente({ tipo: 'decidir', alcada: alcadaPendente, decisao: 'APROVAR' })}>
                Aprovar
              </button>
              <button type="button" className="botao-secundario"
                onClick={() => setPendente({ tipo: 'decidir', alcada: alcadaPendente, decisao: 'AJUSTES' })}>
                Solicitar ajustes
              </button>
              <button type="button" className="botao-perigo"
                onClick={() => setPendente({ tipo: 'decidir', alcada: alcadaPendente, decisao: 'REJEITAR' })}>
                Rejeitar
              </button>
            </div>
          )}

          {pode.registrarOc && (
            <FormRegistroOc processo={q} aoConcluir={recarregar} aoAvisar={(t, tipo) => avisar(t, tipo === 'erro' ? 'erro' : 'ok')} />
          )}

          {pode.cancelar && (
            <div>
              <button type="button" className="botao-perigo" onClick={() => setPendente({ tipo: 'cancelar' })}>
                Cancelar processo
              </button>
            </div>
          )}

          {!pode.encerrar && !pode.escolherVencedor && !alcadaPendente && !pode.registrarOc && !pode.cancelar
            && !pode.conflitoSegregacao && (
            <Vazio>Nenhuma ação disponível para o seu papel nesta etapa.</Vazio>
          )}
        </div>
      </Painel>

      {q.purchaseOrders.length > 0 && (
        <Painel titulo="O.C. registrada">
          <div className="overflow-x-auto">
            <table data-testid="ocs-do-processo">
              <thead><tr><th>O.C.</th><th>Fornecedor</th><th>Família(s)</th><th>Valor</th><th></th></tr></thead>
              <tbody>
                {q.purchaseOrders.map((o) => (
                  <tr key={o.id}>
                    <td><strong>{o.number ?? '—'}</strong></td>
                    <td>{o.supplierName}</td>
                    <td className="sub">{o.families.join(', ') || '—'}</td>
                    <td className="whitespace-nowrap">{moeda(o.totalValue)}</td>
                    <td>
                      <Link className="botao-secundario" to={`/pedidos/${o.id}`}>Faturamento e entrega</Link>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <Nota>O faturamento e a confirmação de entrega ficam na tela do pedido.</Nota>
        </Painel>
      )}

      {pendente?.tipo === 'cancelar' && (
        <DialogoMotivo titulo={`Cancelar o processo ${q.number}`} rotulo="Motivo do cancelamento"
          dica="(obrigatório)" rotuloConfirmar="Cancelar processo" obrigatorio perigo
          aoConfirmar={concluirPendente} aoFechar={() => setPendente(null)} />
      )}
      {pendente?.tipo === 'encerrar' && (
        <Confirmacao titulo="Encerrar para análise" rotuloConfirmar="Encerrar"
          mensagem={<>Encerrar a cotação <strong>{q.number}</strong>? O portal deixa de aceitar propostas novas.</>}
          aoConfirmar={() => concluirPendente('')} aoFechar={() => setPendente(null)} />
      )}
      {pendente?.tipo === 'decidir' && (
        <DialogoMotivo
          titulo={pendente.decisao === 'APROVAR' ? `Aprovar ${q.number}`
            : pendente.decisao === 'AJUSTES' ? `Solicitar ajustes em ${q.number}` : `Rejeitar ${q.number}`}
          rotulo={pendente.decisao === 'APROVAR' ? 'Observação da aprovação' : 'Motivo'}
          dica={pendente.decisao === 'APROVAR' ? '(opcional)' : '(obrigatório)'}
          rotuloConfirmar={pendente.decisao === 'APROVAR' ? 'Aprovar'
            : pendente.decisao === 'AJUSTES' ? 'Solicitar ajustes' : 'Rejeitar'}
          obrigatorio={pendente.decisao !== 'APROVAR'}
          perigo={pendente.decisao === 'REJEITAR'}
          aoConfirmar={concluirPendente} aoFechar={() => setPendente(null)} />
      )}
    </>
  );
}
