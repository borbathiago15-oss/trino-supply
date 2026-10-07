import { useState } from 'react';
import { Link } from 'react-router-dom';
import {
  FILTRO_VAZIO, filtrosAtivos, fraseDoValor, painelDeAtendimentos,
  type FiltroDoPainel, type GrupoPainel, type LinhaPainel,
} from '@/api/material';
import { Carregando, Erro, Painel, Vazio } from '@/componentes/basicos';
import { MarcaDoSla } from '@/componentes/MarcaDoSla';
import { Campo } from '@/componentes/formulario';
import { useCarregar } from '@/util/useCarregar';
import { data, quantidade } from '@/util/formato';
import { rolarPara } from '@/util/rolar';
import { rotuloDoCentro } from '@/dominio/centrosDeCusto';
import { useNomesDosCentros } from '@/util/useNomesDosCentros';

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

function Lista({ linhas, coluna, marca, nomesDosCentros }: {
  linhas: LinhaPainel[]; coluna: string; marca: string;
  /** Por prop: a tela monta várias listas, e um hook em cada uma seria uma consulta por bloco. */
  nomesDosCentros: Record<string, string>;
}) {
  if (!linhas.length) return <Vazio>Nada por aqui. ✔</Vazio>;
  return (
    <div className="overflow-x-auto">
      <table data-testid={marca} className="min-w-[760px]">
        <thead>
          <tr><th>Solicitação</th><th>Solicitante</th><th>Centro</th><th>Itens</th><th>Valor</th><th>Prazo</th><th>{coluna}</th></tr>
        </thead>
        <tbody>
          {linhas.map((r) => (
            <tr key={r.id} data-material={r.number}>
              <td className="whitespace-nowrap">
                <span className="font-semibold">{r.number}</span>
                <div className="sub">{data(r.createdAt)}</div>
              </td>
              <td>{r.requesterLabel}</td>
              <td title={r.costCenter}>{rotuloDoCentro(nomesDosCentros, r.costCenter)}</td>
              <td className="min-w-[240px]">{r.summary || '—'}</td>
              {/* o valor liberado, pelo custo congelado no pedido; "sem custo" é dito, não deixado em branco */}
              <td className="whitespace-nowrap">{fraseDoValor(r.value, r.itemsWithoutPrice) ?? '—'}</td>
              {/* o mesmo prazo e a mesma marca da fila do almoxarifado (MarcaDoSla) */}
              <td className="whitespace-nowrap"><MarcaDoSla sla={r.sla} /></td>
              <td>{situacaoDaLinha(r)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

/**
 * Uma quebra do painel. O rótulo da linha **aplica o filtro daquela linha**: é a mesma regra do
 * ranking do Dashboard — o número já promete um recorte, e obrigar a repetir o centro no seletor
 * ao lado seria pedir duas vezes a mesma coisa. Tocar de novo tira.
 *
 * <p>
 * É <b>botão</b>, e não uma `<tr>` com `onClick`, pela mesma razão do `ListaBarras`: o alvo é
 * largo (a célula inteira), chega pelo teclado e o `aria-pressed` diz se aquele recorte está
 * valendo — numa linha de tabela com `onClick` nada disso existe.
 * </p>
 */
function Agrupado({ linhas, titulo, campo, marca, rotulo, aoFiltrar, ativo }:
  {
    linhas: GrupoPainel[]; titulo: string; campo: 'costCenter' | 'requesterLabel'; marca: string;
    rotulo?: (g: GrupoPainel) => string;
    aoFiltrar: (g: GrupoPainel) => void;
    ativo: (g: GrupoPainel) => boolean;
  }) {
  if (!linhas.length) return <Vazio>Sem dados ainda.</Vazio>;
  return (
    <div className="overflow-x-auto">
      <table data-testid={marca}>
        <thead>
          <tr><th>{titulo}</th><th>Total</th><th>Em andamento</th><th>Concluídos</th><th>Parciais</th></tr>
        </thead>
        <tbody>
          {linhas.map((g) => {
            const texto = (rotulo ? rotulo(g) : g[campo]) || '—';
            const marcada = ativo(g);
            return (
              <tr key={g[campo] ?? '—'} className={marcada ? 'bg-marca/5 font-semibold' : undefined}>
                {/* o código fica na dica do centro: é por ele que se confere com o ERP */}
                <td title={campo === 'costCenter' ? g.costCenter : undefined}>
                  <button type="button" aria-pressed={marcada} onClick={() => aoFiltrar(g)}
                    aria-label={marcada ? `Tirar o filtro de ${texto}` : `Filtrar por ${texto}`}
                    className={'-mx-1 w-full rounded-md px-1 py-0.5 text-left '
                      + (marcada ? 'text-marca' : 'hover:bg-superficie-suave')}>
                    {texto}
                  </button>
                </td>
                <td>{g.total}</td><td>{g.emAndamento}</td><td>{g.concluidos}</td><td>{g.parciais}</td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

export function PainelDeAtendimentos() {
  const nomesDosCentros = useNomesDosCentros();
  const [aberto, setAberto] = useState<Bloco | null>(null);
  const [filtro, setFiltro] = useState<FiltroDoPainel>(FILTRO_VAZIO);
  const chave = JSON.stringify(filtro);
  const { dados, erro, carregando } = useCarregar(
    (signal) => painelDeAtendimentos(filtro, signal), [chave]);

  // `undefined` é como se tira um recorte: a chave serializada muda em relação à anterior, e é
  // ela que manda a consulta de novo — o painel inteiro volta sem ninguém precisar limpar tudo
  const trocar = (mudanca: Partial<FiltroDoPainel>) => setFiltro((f) => ({ ...f, ...mudanca }));

  // clicar de novo no mesmo cartão volta a mostrar todos os blocos
  const escolher = (b: Bloco) => {
    const proximo = aberto === b ? null : b;
    setAberto(proximo);
    if (proximo) rolarPara('bloco-' + proximo);
  };
  const visivel = (b: Bloco) => aberto === null || aberto === b;

  const ativos = filtrosAtivos(filtro);
  const barra = (
    // o recorte vai ao servidor: filtrar só as listas deixaria o cartão contando uma coisa
    // e a tabela mostrando outra, que é o oposto do que o painel promete
    <Painel titulo="Filtro" className="mb-3">
      <div className="grid grid-cols-1 gap-3 md:grid-cols-4" data-testid="filtro-painel">
        {/* as opções vêm de `opcoes`, o cadastro inteiro — não das quebras, que contam o
            recorte: tiradas do recorte, trocar de centro exigiria limpar o filtro primeiro */}
        <Campo id="f-centro" rotulo="Centro de custo">
          <select id="f-centro" value={filtro.costCenter ?? ''}
            onChange={(e) => trocar({ costCenter: e.target.value || undefined })}>
            <option value="">Todos</option>
            {(dados?.opcoes?.centros ?? []).map((codigo) => (
              <option key={codigo} value={codigo}>{rotuloDoCentro(nomesDosCentros, codigo)}</option>
            ))}
          </select>
        </Campo>
        <Campo id="f-solicitante" rotulo="Solicitante">
          <select id="f-solicitante" value={filtro.requesterId ?? ''}
            onChange={(e) => trocar({ requesterId: e.target.value || undefined })}>
            <option value="">Todos</option>
            {(dados?.opcoes?.solicitantes ?? []).map((s) => (
              <option key={s.id} value={s.id}>{s.label}</option>
            ))}
          </select>
        </Campo>
        <Campo id="f-de" rotulo="Criadas de">
          <input id="f-de" type="date" value={filtro.from ?? ''}
            onChange={(e) => trocar({ from: e.target.value || undefined })} />
        </Campo>
        <Campo id="f-ate" rotulo="até">
          <input id="f-ate" type="date" value={filtro.to ?? ''}
            onChange={(e) => trocar({ to: e.target.value || undefined })} />
        </Campo>
      </div>
      {ativos > 0 && (
        <p className="sub mt-2">
          {ativos} filtro(s) valendo — os cartões e as listas abaixo já contam só o recorte.{' '}
          <button type="button" className="underline" data-testid="limpar-filtro"
            onClick={() => setFiltro(FILTRO_VAZIO)}>limpar</button>
        </p>
      )}
    </Painel>
  );

  if (erro) return <>{barra}<Painel><Erro>{erro}</Erro></Painel></>;
  if (!dados) return <>{barra}<Painel>{carregando && <Carregando />}</Painel></>;

  const t = dados.totals;
  return (
    <>
      {barra}
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
          <Lista linhas={dados.aguardandoAprovacao} coluna="Situação" marca="painel-aguardando" nomesDosCentros={nomesDosCentros} />
        </Painel>
      )}
      {visivel('andamento') && (
        <Painel id="bloco-andamento" titulo="Atendimentos em andamento"
          acoes={<Link className="botao-secundario" to="/estoque/fila">Ir para a Fila de Atendimento</Link>}>
          <Lista linhas={dados.emAndamento} coluna="Situação" marca="painel-andamento" nomesDosCentros={nomesDosCentros} />
        </Painel>
      )}
      {visivel('parcial') && (
        <Painel id="bloco-parcial" titulo="Concluídos parcialmente — aguardando a compra do faltante"
          acoes={<Link className="botao-secundario" to="/cotacoes">Ver os processos de compra</Link>}>
          <Lista linhas={dados.parciais} coluna="Faltante" marca="painel-parcial" nomesDosCentros={nomesDosCentros} />
        </Painel>
      )}
      {visivel('concluido') && (
        <Painel id="bloco-concluido" titulo="Atendimentos concluídos">
          <Lista linhas={dados.concluidos} coluna="Atendido por" marca="painel-concluido" nomesDosCentros={nomesDosCentros} />
        </Painel>
      )}

      <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
        <Painel titulo="Por centro de custo">
          <Agrupado linhas={dados.porCentro} titulo="Centro de custo" campo="costCenter" marca="painel-por-centro"
            rotulo={(g) => rotuloDoCentro(nomesDosCentros, g.costCenter)}
            ativo={(g) => !!g.costCenter && filtro.costCenter === g.costCenter}
            aoFiltrar={(g) => trocar({ costCenter: filtro.costCenter === g.costCenter ? undefined : g.costCenter })} />
        </Painel>
        <Painel titulo="Por solicitante">
          <Agrupado linhas={dados.porSolicitante} titulo="Solicitante" campo="requesterLabel" marca="painel-por-solicitante"
            ativo={(g) => !!g.requesterId && filtro.requesterId === g.requesterId}
            aoFiltrar={(g) => trocar({ requesterId: filtro.requesterId === g.requesterId ? undefined : g.requesterId })} />
        </Painel>
      </div>
    </>
  );
}
