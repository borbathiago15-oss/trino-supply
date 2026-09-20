import { useState } from 'react';
import { Link } from 'react-router-dom';
import { painelDeAtendimentos, type GrupoPainel, type LinhaPainel } from '@/api/material';
import { Carregando, Erro, Painel, Vazio } from '@/componentes/basicos';
import { useCarregar } from '@/util/useCarregar';
import { data, quantidade } from '@/util/formato';
import { rolarPara } from '@/util/rolar';

type Bloco = 'aguardando' | 'andamento' | 'parcial' | 'concluido';

/**
 * A coluna que muda de bloco para bloco: no parcial é o que falta e a SC que o
 * cobre; nos outros, quem atendeu ou desde quando a solicitação espera.
 */
export function situacaoDaLinha(r: LinhaPainel): string {
  if (r.purchaseRequisitionNumber) return `falta ${quantidade(r.pending)} · SC ${r.purchaseRequisitionNumber}`;
  if (r.fulfilledByLabel) return r.fulfilledByLabel;
  if (r.approvedAt) return `aprovada em ${data(r.approvedAt)}`;
  return 'aguardando o Nível 1 do centro';
}

function Cartao({ rotulo, valor, detalhe, aberto, aoEscolher }: {
  rotulo: string; valor: number; detalhe: string; aberto: boolean; aoEscolher: () => void;
}) {
  return (
    <button type="button" onClick={aoEscolher} aria-pressed={aberto}
      className={'rounded-painel border px-4 py-3 text-left transition-colors '
        + (aberto ? 'border-marca bg-marca/5' : 'border-borda bg-superficie hover:bg-superficie-suave')}>
      <div className="rotulo">{rotulo}</div>
      <div className="mt-1 text-[22px] font-bold leading-tight">{valor}</div>
      <div className="sub mt-0.5">{detalhe}</div>
      <div className="mt-1 text-[12px] font-semibold text-marca">{aberto ? 'mostrando só este →' : 'ver a lista →'}</div>
    </button>
  );
}

function Lista({ linhas, coluna, marca }: { linhas: LinhaPainel[]; coluna: string; marca: string }) {
  if (!linhas.length) return <Vazio>Nada por aqui. ✔</Vazio>;
  return (
    <div className="overflow-x-auto">
      <table data-testid={marca} className="min-w-[760px]">
        <thead>
          <tr><th>Solicitação</th><th>Solicitante</th><th>Centro</th><th>Itens</th><th>{coluna}</th></tr>
        </thead>
        <tbody>
          {linhas.map((r) => (
            <tr key={r.id} data-material={r.number}>
              <td className="whitespace-nowrap">
                <span className="font-semibold">{r.number}</span>
                <div className="sub">{data(r.createdAt)}</div>
              </td>
              <td>{r.requesterLabel}</td>
              <td>{r.costCenter}</td>
              <td className="min-w-[240px]">{r.summary || '—'}</td>
              <td>{situacaoDaLinha(r)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function Agrupado({ linhas, titulo, campo, marca }:
  { linhas: GrupoPainel[]; titulo: string; campo: 'costCenter' | 'requesterLabel'; marca: string }) {
  if (!linhas.length) return <Vazio>Sem dados ainda.</Vazio>;
  return (
    <div className="overflow-x-auto">
      <table data-testid={marca}>
        <thead>
          <tr><th>{titulo}</th><th>Total</th><th>Em andamento</th><th>Concluídos</th><th>Parciais</th></tr>
        </thead>
        <tbody>
          {linhas.map((g) => (
            <tr key={g[campo] ?? '—'}>
              <td>{g[campo] || '—'}</td>
              <td>{g.total}</td><td>{g.emAndamento}</td><td>{g.concluidos}</td><td>{g.parciais}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export function PainelDeAtendimentos() {
  const [aberto, setAberto] = useState<Bloco | null>(null);
  const { dados, erro, carregando } = useCarregar(painelDeAtendimentos, []);

  // clicar de novo no mesmo cartão volta a mostrar todos os blocos
  const escolher = (b: Bloco) => {
    const proximo = aberto === b ? null : b;
    setAberto(proximo);
    if (proximo) rolarPara('bloco-' + proximo);
  };
  const visivel = (b: Bloco) => aberto === null || aberto === b;

  if (erro) return <Painel><Erro>{erro}</Erro></Painel>;
  if (!dados) return <Painel>{carregando && <Carregando />}</Painel>;

  const t = dados.totals;
  return (
    <>
      <div className="mb-2 grid grid-cols-2 gap-3 lg:grid-cols-4">
        <Cartao rotulo="Aguardando aprovação" valor={t.aguardandoAprovacao} detalhe="do responsável do centro"
          aberto={aberto === 'aguardando'} aoEscolher={() => escolher('aguardando')} />
        <Cartao rotulo="Em andamento" valor={t.emAndamento} detalhe="aprovadas, na fila do estoque"
          aberto={aberto === 'andamento'} aoEscolher={() => escolher('andamento')} />
        <Cartao rotulo="Concluídos" valor={t.concluidos} detalhe="atendidos integralmente"
          aberto={aberto === 'concluido'} aoEscolher={() => escolher('concluido')} />
        <Cartao rotulo="Parciais aguardando compra" valor={t.parciais} detalhe="o faltante virou solicitação"
          aberto={aberto === 'parcial'} aoEscolher={() => escolher('parcial')} />
      </div>
      <p className="sub mb-4">
        {aberto ? 'Mostrando só este cartão — clique nele de novo para ver todos.'
          : 'Clique em um cartão para ver a lista por trás do número.'}
      </p>

      {visivel('aguardando') && (
        <Painel id="bloco-aguardando" titulo="Aguardando aprovação do centro"
          acoes={<Link className="botao-secundario" to="/aprovacoes">Ir para a Central de Aprovação</Link>}>
          <Lista linhas={dados.aguardandoAprovacao} coluna="Situação" marca="painel-aguardando" />
        </Painel>
      )}
      {visivel('andamento') && (
        <Painel id="bloco-andamento" titulo="Atendimentos em andamento"
          acoes={<Link className="botao-secundario" to="/estoque/fila">Ir para a Fila de Atendimento</Link>}>
          <Lista linhas={dados.emAndamento} coluna="Situação" marca="painel-andamento" />
        </Painel>
      )}
      {visivel('parcial') && (
        <Painel id="bloco-parcial" titulo="Concluídos parcialmente — aguardando a compra do faltante"
          acoes={<Link className="botao-secundario" to="/cotacoes">Ver os processos de compra</Link>}>
          <Lista linhas={dados.parciais} coluna="Faltante" marca="painel-parcial" />
        </Painel>
      )}
      {visivel('concluido') && (
        <Painel id="bloco-concluido" titulo="Atendimentos concluídos">
          <Lista linhas={dados.concluidos} coluna="Atendido por" marca="painel-concluido" />
        </Painel>
      )}

      <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
        <Painel titulo="Por centro de custo">
          <Agrupado linhas={dados.porCentro} titulo="Centro de custo" campo="costCenter" marca="painel-por-centro" />
        </Painel>
        <Painel titulo="Por solicitante">
          <Agrupado linhas={dados.porSolicitante} titulo="Solicitante" campo="requesterLabel" marca="painel-por-solicitante" />
        </Painel>
      </div>
    </>
  );
}
