import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { abrirBlob } from '@/api/cliente';
import {
  acoesDisponiveis, cancelarProcesso, decidir, encerrarParaAnalise, lerProcesso,
  type Alcada, type Decisao,
} from '@/api/cotacoes';
import { baixarDocumento } from '@/api/documentos';
import { Aviso, Badge, Carregando, Erro, Painel, Vazio } from '@/componentes/basicos';
import { Confirmacao } from '@/componentes/Dialogo';
import { DialogoMotivo } from '@/componentes/DialogoMotivo';
import { Nota } from '@/componentes/formulario';
import { useToast } from '@/componentes/Toast';
import { podeAprovarDiretor, podeAprovarGerente, podeConduzirCotacao } from '@/dominio/papeis';
import { useUsuario } from '@/sessao/SessaoProvider';
import { moeda } from '@/util/formato';
import { useCarregar } from '@/util/useCarregar';
import { FormRegistroOc, FormVencedor, useLotesDoProcesso } from './AcoesDoProcesso';
import { GradeDeAdjudicacao } from './GradeDeAdjudicacao';
import { CabecalhoDoProcesso } from './CabecalhoDoProcesso';
import { MapaDeCotacao } from './MapaDeCotacao';
import { PainelDeConvidados } from './PainelDeConvidados';
import { PainelDeNegociacao } from './PainelDeNegociacao';
import { PainelDeScore } from './PainelDeScore';
import { PainelPropostaManual } from './PainelPropostaManual';

const mensagem = (e: unknown, padrao: string) => (e instanceof Error ? e.message : padrao);

type Pendente =
  | { tipo: 'cancelar' }
  | { tipo: 'encerrar' }
  | { tipo: 'decidir'; alcada: Alcada; decisao: Decisao };

/**
 * A tela do processo de cotação. Ela orquestra: carrega o processo, decide o
 * que o papel de quem olha pode fazer nesta etapa e distribui o resto entre os
 * painéis — cabeçalho, convidados, mapa, negociação e ações.
 *
 * No INT-B os painéis que só liam o processo (ou que tinham estado próprio e
 * de mais ninguém) saíram para arquivos vizinhos. O que sobrou aqui é o que de
 * fato pertence à tela: a leitura, a régua de ações e os diálogos de decisão.
 */
export function ProcessoDetalhe() {
  const { id = '' } = useParams();
  const usuario = useUsuario();
  const { avisar } = useToast();
  const [pendente, setPendente] = useState<Pendente | null>(null);

  const { dados, erro, carregando, recarregar } = useCarregar(
    (signal) => lerProcesso(id, signal), [id]);
  // antes dos returns antecipados: hook que só roda às vezes muda a contagem entre
  // renders, e o React aborta a árvore inteira. Por isso ele aceita processo nulo
  const lotes = useLotesDoProcesso(dados);

  if (erro) return <Painel><Erro>{erro}</Erro></Painel>;
  if (!dados) return <Painel>{carregando && <Carregando texto="Abrindo o processo…" />}</Painel>;

  const q = dados;
  const pode = acoesDisponiveis(q, {
    conduz: podeConduzirCotacao(usuario),
    aprovaNivel1: podeAprovarGerente(usuario),
    aprovaNivel2: podeAprovarDiretor(usuario),
    de: usuario?.id,
  });

  /** Aviso vindo de um painel filho, na forma que o Toast entende. */
  const aviso = (t: string, tipo?: 'ok' | 'erro') => avisar(t, tipo === 'erro' ? 'erro' : 'ok');

  async function baixarAnexo(documentId: string) {
    try { abrirBlob(await baixarDocumento(documentId)); }
    catch (e) { avisar(mensagem(e, 'Falha ao baixar o anexo.'), 'erro'); }
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

  const alcadaPendente: Alcada | null = pode.decidirNivel1 ? 'manager' : pode.decidirNivel2 ? 'director' : null;

  return (
    <>
      <CabecalhoDoProcesso processo={q} usuarioId={usuario?.id} />

      <PainelDeConvidados processo={q} podeConvidar={pode.convidar}
        aoConvidar={recarregar} aoAvisar={aviso} />

      <Painel titulo="Mapa de cotação">
        <MapaDeCotacao processo={q} aoBaixarAnexo={(docId) => baixarAnexo(docId)} />
        {pode.registrarProposta && (
          <PainelPropostaManual processo={q} aoRegistrar={recarregar} aoAvisar={aviso} />
        )}
      </Painel>

      {/* o score vem logo depois do mapa: é a mesma comparação, com o que o preço não diz */}
      {q.proposals.length > 0 && <PainelDeScore processoId={q.id} />}

      <PainelDeNegociacao processo={q} podeNegociar={pode.negociar}
        aoRegistrar={recarregar} aoAvisar={aviso} />

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

          {pode.escolherVencedor && (pode.porItem
            ? <GradeDeAdjudicacao processo={q} lotes={lotes} aoConcluir={recarregar} aoAvisar={aviso} />
            : <FormVencedor processo={q} aoConcluir={recarregar} aoAvisar={aviso} />)}

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
            <FormRegistroOc processo={q} aoConcluir={recarregar} aoAvisar={aviso} />
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
