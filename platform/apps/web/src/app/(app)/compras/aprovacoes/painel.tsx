'use client';

import { useState, useTransition } from 'react';
import { useRouter } from 'next/navigation';
import type { InstanciaAprovacao, Requisicao } from '@trino/contratos';
import { Modal } from '@/componentes/modal';
import { dinheiro, EtiquetaPrioridade, EtiquetaStatus } from '@/componentes/etiquetas';
import { Stepper } from './stepper';
import { decidirEtapa, type ResultadoDecisao } from './acoes';

export interface ItemAprovacao {
  instancia: InstanciaAprovacao;
  requisicao: Requisicao | null;
  /** A etapa que está esperando decisão agora. */
  etapaAtualId: string;
  nivelAtual: number;
  ehNivelFinal: boolean;
}

type Acao = 'APROVAR' | 'REJEITAR' | 'DEVOLVER';

const TITULOS: Record<Acao, string> = {
  APROVAR: 'Confirmar aprovação',
  REJEITAR: 'Rejeitar requisição',
  DEVOLVER: 'Devolver para ajuste',
};

/**
 * Portal do aprovador. Toda ação passa por confirmação explícita — aprovar por
 * engano custa dinheiro, e rejeitar sem motivo trava o solicitante sem
 * explicação. Rejeição exige motivo (o banco também exige).
 */
export function PainelAprovacoes({ itens }: { itens: ItemAprovacao[] }) {
  const router = useRouter();
  const [aberto, setAberto] = useState<{ item: ItemAprovacao; acao: Acao } | null>(null);
  const [comentario, setComentario] = useState('');
  const [autorizarEstouro, setAutorizarEstouro] = useState(false);
  const [resultado, setResultado] = useState<ResultadoDecisao | null>(null);
  const [pendente, iniciar] = useTransition();

  const fechar = () => {
    setAberto(null);
    setComentario('');
    setAutorizarEstouro(false);
    setResultado(null);
  };

  const confirmar = () => {
    if (!aberto) return;
    iniciar(async () => {
      const resposta = await decidirEtapa(aberto.item.etapaAtualId, {
        decisao: aberto.acao === 'APROVAR' ? 'APROVADO' : 'REJEITADO',
        comentario: comentario.trim() || undefined,
        autorizarEstouro: aberto.acao === 'APROVAR' && autorizarEstouro ? true : undefined,
      });
      setResultado(resposta);
      if (resposta.ok) {
        fechar();
        router.refresh();
      }
    });
  };

  const motivoObrigatorio = aberto?.acao !== 'APROVAR';
  const podeConfirmar = !pendente && (!motivoObrigatorio || comentario.trim().length >= 3);

  return (
    <>
      {itens.length === 0 ? (
        <div className="card p-10 text-center text-sm text-slate-500">
          Nenhuma requisição aguardando a sua decisão.
        </div>
      ) : null}

      <div className="space-y-4">
        {itens.map((item) => (
          <article key={item.instancia.id} className="card p-5">
            <div className="flex flex-wrap items-start justify-between gap-4">
              <div>
                <h2 className="text-lg font-semibold text-slate-800">
                  {item.requisicao?.numero ?? 'Requisição'}
                  {item.requisicao?.orcamentoEstourado ? (
                    <span className="etiqueta ml-2 bg-red-100 text-red-800">orçamento estourado</span>
                  ) : null}
                </h2>
                <p className="mt-1 text-sm text-slate-500">
                  Valor base {dinheiro(item.instancia.valorBase)} · seu nível: {item.nivelAtual}
                  {item.ehNivelFinal ? ' (aprovador final)' : ''}
                </p>
              </div>
              <div className="flex items-center gap-2">
                {item.requisicao ? <EtiquetaStatus status={item.requisicao.status} /> : null}
                {item.requisicao ? <EtiquetaPrioridade prioridade={item.requisicao.prioridade} /> : null}
              </div>
            </div>

            {item.requisicao?.justificativa ? (
              <p className="mt-3 text-sm text-slate-700">{item.requisicao.justificativa}</p>
            ) : null}

            {item.requisicao?.orcamentoEstourado && item.requisicao.orcamentoSnapshot ? (
              <p className="mt-3 rounded-md bg-red-50 px-3 py-2 text-sm text-red-800">
                {item.requisicao.orcamentoSnapshot.motivo === 'SEM_ORCAMENTO'
                  ? `Centro de custo sem orçamento definido para ${item.requisicao.orcamentoSnapshot.exercicio}.`
                  : `Excede o saldo em ${dinheiro(item.requisicao.orcamentoSnapshot.excedente ?? 0)}.`}
                {item.ehNivelFinal
                  ? ' Aprovar exige autorizar o estouro explicitamente.'
                  : ' O aprovador final decidirá sobre o estouro.'}
              </p>
            ) : null}

            <div className="mt-4">
              <Stepper etapas={item.instancia.etapas} nivelFinal={item.instancia.regraSnapshot.nivelFinal} />
            </div>

            <div className="mt-5 flex flex-wrap gap-2">
              <button type="button" className="botao-primario" onClick={() => setAberto({ item, acao: 'APROVAR' })}>
                Aprovar
              </button>
              <button type="button" className="botao-perigo" onClick={() => setAberto({ item, acao: 'REJEITAR' })}>
                Rejeitar
              </button>
              <button type="button" className="botao-secundario" onClick={() => setAberto({ item, acao: 'DEVOLVER' })}>
                Devolver
              </button>
            </div>
          </article>
        ))}
      </div>

      <Modal
        aberto={aberto !== null}
        titulo={aberto ? TITULOS[aberto.acao] : ''}
        aoFechar={fechar}
        rodape={
          <div className="flex justify-end gap-2">
            <button type="button" className="botao-secundario" onClick={fechar} disabled={pendente}>
              Cancelar
            </button>
            <button
              type="button"
              className={aberto?.acao === 'APROVAR' ? 'botao-primario' : 'botao-perigo'}
              onClick={confirmar}
              disabled={!podeConfirmar}
            >
              {pendente ? 'Registrando…' : 'Confirmar'}
            </button>
          </div>
        }
      >
        {aberto ? (
          <div className="space-y-4 text-sm">
            <p className="text-slate-700">
              {aberto.acao === 'APROVAR'
                ? `Confirma a aprovação do nível ${aberto.item.nivelAtual} da requisição ${aberto.item.requisicao?.numero ?? ''}?`
                : aberto.acao === 'REJEITAR'
                  ? 'A rejeição encerra a aprovação e volta para o solicitante. Informe o motivo.'
                  : 'A devolução pede ajuste ao solicitante. Informe o que precisa ser corrigido.'}
            </p>

            <div>
              <label className="rotulo" htmlFor="comentario-decisao">
                {motivoObrigatorio ? 'Motivo (obrigatório)' : 'Comentário (opcional)'}
              </label>
              <textarea
                id="comentario-decisao"
                className="campo mt-1"
                rows={3}
                value={comentario}
                onChange={(e) => setComentario(e.target.value)}
              />
            </div>

            {aberto.acao === 'APROVAR' && aberto.item.requisicao?.orcamentoEstourado && aberto.item.ehNivelFinal ? (
              <label className="flex items-start gap-2 rounded-md bg-amber-50 p-3">
                <input
                  type="checkbox"
                  className="mt-0.5"
                  checked={autorizarEstouro}
                  onChange={(e) => setAutorizarEstouro(e.target.checked)}
                />
                <span className="text-amber-900">
                  Autorizo expressamente o estouro do orçamento desta requisição.
                </span>
              </label>
            ) : null}

            {resultado && !resultado.ok ? (
              <p role="alert" className="rounded-md bg-red-50 px-3 py-2 text-red-800">
                {resultado.mensagem}
                {resultado.exigeAutorizacaoEstouro ? ' Marque a autorização do estouro para prosseguir.' : ''}
              </p>
            ) : null}
          </div>
        ) : null}
      </Modal>
    </>
  );
}
