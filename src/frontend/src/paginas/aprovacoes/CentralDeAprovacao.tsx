import { useState } from 'react';
import { processosParaMinhaAprovacao, propostaVencedora, ROTULO_RFQ, type ProcessoParaAprovar } from '@/api/cotacoes';
import {
  aprovarMaterial, listarSolicitacoesMaterial, recusarMaterial, type SolicitacaoMaterial,
} from '@/api/material';
import {
  aprovacoesPendentes, aprovarSolicitacao, devolverSolicitacao, rejeitarSolicitacao,
  situacaoDaSc, type SolicitacaoCompra,
} from '@/api/solicitacoes';
import { Badge, Carregando, Erro, Painel, Vazio } from '@/componentes/basicos';
import { DialogoMotivo } from '@/componentes/DialogoMotivo';
import { Nota } from '@/componentes/formulario';
import { useToast } from '@/componentes/Toast';
import { moeda, quantidade } from '@/util/formato';
import { useCarregar } from '@/util/useCarregar';
import { resumoDosItens } from '@/paginas/solicitacoes/MeusPedidos';

const mensagem = (e: unknown, padrao: string) => (e instanceof Error ? e.message : padrao);

/**
 * A tela de Cotações ainda é a do sistema clássico: o botão leva para lá com o
 * processo já aberto. Sai daqui quando Cotações for migrada.
 */
export const linkDoProcesso = (id: string) => `/cotacoes/${id}`;

type Acao =
  | { tipo: 'aprovar-sc'; sc: SolicitacaoCompra }
  | { tipo: 'devolver-sc'; sc: SolicitacaoCompra }
  | { tipo: 'rejeitar-sc'; sc: SolicitacaoCompra }
  | { tipo: 'aprovar-material'; mr: SolicitacaoMaterial }
  | { tipo: 'recusar-material'; mr: SolicitacaoMaterial };

export function CentralDeAprovacao() {
  const { avisar } = useToast();
  const [acao, setAcao] = useState<Acao | null>(null);
  const [liberado, setLiberado] = useState<Record<string, string>>({});

  const { dados, erro, carregando, recarregar } = useCarregar(async (signal) => ({
    // cada fila é opcional: quem não tem acesso a uma continua vendo as outras
    processos: await processosParaMinhaAprovacao(signal).catch(() => [] as ProcessoParaAprovar[]),
    materiais: (await listarSolicitacoesMaterial(signal).catch(() => [] as SolicitacaoMaterial[]))
      .filter((r) => r.status === 'AGUARDANDO_APROVACAO'),
    solicitacoes: await aprovacoesPendentes(signal).catch(() => [] as SolicitacaoCompra[]),
  }), []);

  const processos = dados?.processos ?? [];
  const materiais = dados?.materiais ?? [];
  const solicitacoes = dados?.solicitacoes ?? [];
  const vazia = dados && !processos.length && !materiais.length && !solicitacoes.length;

  /** Quantidade liberada de um item: o que o aprovador digitou, ou tudo. */
  const quantidadeLiberada = (mr: SolicitacaoMaterial, itemId: string, pedida: number) => {
    const valor = liberado[`${mr.id}:${itemId}`];
    return valor === undefined || valor === '' ? pedida : parseFloat(valor) || 0;
  };

  async function concluir(texto: string) {
    if (!acao) return;
    setAcao(null);
    try {
      if (acao.tipo === 'aprovar-sc') {
        await aprovarSolicitacao(acao.sc.id, texto || null);
        avisar(`Solicitação ${acao.sc.number} aprovada.`);
      } else if (acao.tipo === 'devolver-sc') {
        await devolverSolicitacao(acao.sc.id, texto);
        avisar(`Solicitação ${acao.sc.number} devolvida para ajuste.`);
      } else if (acao.tipo === 'rejeitar-sc') {
        await rejeitarSolicitacao(acao.sc.id, texto);
        avisar(`Solicitação ${acao.sc.number} rejeitada.`);
      } else if (acao.tipo === 'recusar-material') {
        await recusarMaterial(acao.mr.id, texto);
        avisar('Solicitação de material recusada.');
      } else {
        const items = acao.mr.items.map((i) => ({
          itemId: i.itemId, quantity: quantidadeLiberada(acao.mr, i.itemId, i.quantity),
        }));
        await aprovarMaterial(acao.mr.id, items, texto || null);
        avisar('Solicitação liberada para o almoxarifado.');
      }
      recarregar();
    } catch (e) { avisar(mensagem(e, 'Falha ao concluir a ação.'), 'erro'); }
  }

  return (
    <>
      <Painel titulo="Compras aguardando a sua aprovação">
        {erro && <Erro>{erro}</Erro>}
        {carregando && !dados && <Carregando />}
        {dados && !processos.length && <Vazio>Nenhuma compra aguardando a sua aprovação.</Vazio>}
        {processos.length > 0 && (
          <div className="overflow-x-auto">
            <table data-testid="tabela-processos" className="min-w-[900px]">
              <thead>
                <tr><th>Processo</th><th>Origem</th><th>Fornecedor escolhido</th><th>Valor</th><th>Etapa</th><th>Ações</th></tr>
              </thead>
              <tbody>
                {processos.map((q) => {
                  const marca = ROTULO_RFQ[q.status] ?? { rotulo: q.status, classe: '' };
                  const vencedora = propostaVencedora(q);
                  return (
                    <tr key={q.id} data-processo={q.number}>
                      <td className="whitespace-nowrap">
                        <span className="font-semibold">{q.number}</span>
                        <div className="sub">CC: {q.costCenter}</div>
                      </td>
                      <td>{q.sourcePrNumber ?? '—'}<div className="sub">{q.justification ?? ''}</div></td>
                      <td>
                        {vencedora?.supplierName ?? '—'}
                        {q.selection?.justification && <div className="sub">{q.selection.justification}</div>}
                      </td>
                      <td className="whitespace-nowrap">{vencedora?.totalValue != null ? moeda(vencedora.totalValue) : '—'}</td>
                      <td><Badge classe={marca.classe}>{marca.rotulo}</Badge></td>
                      <td className="whitespace-nowrap">
                        <a className="botao" href={linkDoProcesso(q.id)}>Analisar e decidir</a>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </Painel>

      {materiais.length > 0 && (
        <Painel titulo="Material do almoxarifado — sua aprovação">
          <Nota>Aprove como está ou ajuste a quantidade liberada; a quantidade pedida não muda.</Nota>
          <div className="mt-3 overflow-x-auto">
            <table data-testid="tabela-material">
              <thead><tr><th>Solicitação</th><th>Solicitante</th><th>Itens</th><th>Ações</th></tr></thead>
              <tbody>
                {materiais.map((r) => (
                  <tr key={r.id} data-material={r.number}>
                    <td className="whitespace-nowrap">
                      <span className="font-semibold">{r.number}</span>
                      <div className="sub">CC: {r.costCenter}</div>
                    </td>
                    <td>{r.requesterLabel}{r.notes && <div className="sub">{r.notes}</div>}</td>
                    <td className="min-w-[320px]">
                      {r.items.map((i) => (
                        <div key={i.itemId} className="mb-1 flex flex-wrap items-center gap-2">
                          <span>{i.description}</span>
                          <input type="number" min={0} step="0.01" max={i.quantity} className="!w-[96px]"
                            aria-label={`Quantidade liberada de ${i.description}`}
                            value={liberado[`${r.id}:${i.itemId}`] ?? String(i.quantity)}
                            onChange={(e) => setLiberado((l) => ({ ...l, [`${r.id}:${i.itemId}`]: e.target.value }))} />
                          <span className="sub">de {quantidade(i.quantity)} {i.unitOfMeasure}</span>
                        </div>
                      ))}
                    </td>
                    <td className="whitespace-nowrap">
                      <div className="flex gap-1.5">
                        <button type="button" className="botao" onClick={() => setAcao({ tipo: 'aprovar-material', mr: r })}>Aprovar</button>
                        <button type="button" className="botao-perigo" onClick={() => setAcao({ tipo: 'recusar-material', mr: r })}>Recusar</button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Painel>
      )}

      {solicitacoes.length > 0 && (
        <Painel titulo="Solicitações do fluxo anterior">
          <Nota>Autorização sem preço, do fluxo que existia antes da cotação obrigatória.</Nota>
          <div className="mt-3 overflow-x-auto">
            <table data-testid="tabela-scs" className="min-w-[900px]">
              <thead><tr><th>Número</th><th>Resumo</th><th>Valor est.</th><th>Situação</th><th>Ações</th></tr></thead>
              <tbody>
                {solicitacoes.map((r) => {
                  const marca = situacaoDaSc(r);
                  return (
                    <tr key={r.id} data-solicitacao={r.number}>
                      <td className="whitespace-nowrap">
                        <span className="font-semibold">{r.number}</span>
                        <div className="sub">{r.requesterLabel}</div>
                      </td>
                      <td className="min-w-[300px]">
                        {r.justification}
                        <div className="sub">{resumoDosItens(r)}</div>
                        <div className="sub">CC: {r.costCenter}</div>
                      </td>
                      <td className="whitespace-nowrap">{moeda(r.totalEstimatedValue)}</td>
                      <td><Badge classe={marca.classe}>{marca.rotulo}</Badge></td>
                      <td className="whitespace-nowrap">
                        <div className="flex gap-1.5">
                          <button type="button" className="botao" onClick={() => setAcao({ tipo: 'aprovar-sc', sc: r })}>Aprovar</button>
                          <button type="button" className="botao-secundario" onClick={() => setAcao({ tipo: 'devolver-sc', sc: r })}>Devolver</button>
                          <button type="button" className="botao-perigo" onClick={() => setAcao({ tipo: 'rejeitar-sc', sc: r })}>Rejeitar</button>
                        </div>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </Painel>
      )}

      {vazia && <Painel titulo="Nada na sua fila"><Vazio>Nenhuma aprovação pendente para você agora.</Vazio></Painel>}

      {acao?.tipo === 'aprovar-sc' && (
        <DialogoMotivo titulo={`Aprovar ${acao.sc.number}`} rotulo="Comentário da aprovação" dica="(opcional)"
          rotuloConfirmar="Aprovar" aoConfirmar={concluir} aoFechar={() => setAcao(null)} />
      )}
      {acao?.tipo === 'devolver-sc' && (
        <DialogoMotivo titulo={`Devolver ${acao.sc.number}`} rotulo="O que o solicitante deve ajustar?" obrigatorio
          rotuloConfirmar="Devolver" aoConfirmar={concluir} aoFechar={() => setAcao(null)} />
      )}
      {acao?.tipo === 'rejeitar-sc' && (
        <DialogoMotivo titulo={`Rejeitar ${acao.sc.number}`} rotulo="Motivo da rejeição" obrigatorio perigo
          rotuloConfirmar="Rejeitar" aoConfirmar={concluir} aoFechar={() => setAcao(null)} />
      )}
      {acao?.tipo === 'aprovar-material' && (
        <DialogoMotivo titulo={`Liberar ${acao.mr.number}`} rotulo="Observação da aprovação" dica="(opcional)"
          rotuloConfirmar="Liberar" aoConfirmar={concluir} aoFechar={() => setAcao(null)} />
      )}
      {acao?.tipo === 'recusar-material' && (
        <DialogoMotivo titulo={`Recusar ${acao.mr.number}`} rotulo="Justificativa da recusa" obrigatorio perigo
          rotuloConfirmar="Recusar" aoConfirmar={concluir} aoFechar={() => setAcao(null)} />
      )}
    </>
  );
}
