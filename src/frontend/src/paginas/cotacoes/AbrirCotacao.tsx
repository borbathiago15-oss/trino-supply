import { useMemo, useState } from 'react';
import {
  abrirProcesso, agruparPorFamilia, filaDeCotacao, ROTULO_TIPO, situacaoDaSelecao,
  type ScNaFila, type TipoCotacao,
} from '@/api/cotacoes';
import { Badge, Carregando, Erro, Painel, Vazio } from '@/componentes/basicos';
import { Confirmacao } from '@/componentes/Dialogo';
import { Nota } from '@/componentes/formulario';
import { useToast } from '@/componentes/Toast';
import { podeConduzirCotacao } from '@/dominio/papeis';
import { useUsuario } from '@/sessao/SessaoProvider';
import { moeda, quantidade } from '@/util/formato';
import { useCarregar } from '@/util/useCarregar';

const mensagem = (e: unknown, padrao: string) => (e instanceof Error ? e.message : padrao);

/**
 * Depois de abrir, o passo seguinte é convidar fornecedores — e essa tela ainda
 * é a do sistema clássico. O deep link já abre o processo recém-criado.
 */
export const linkDoProcesso = (id?: string) => `/#tela=quotations${id ? `&rfq=${id}` : ''}`;

/** Um item marcado carrega o centro e a família: as duas regras da seleção. */
interface Marcado { id: string; centroCusto: string; familia: string }

export function AbrirCotacao() {
  const usuario = useUsuario();
  const { avisar } = useToast();
  const conduz = podeConduzirCotacao(usuario);

  const [marcados, setMarcados] = useState<Record<string, Marcado>>({});
  const [tipo, setTipo] = useState<TipoCotacao>('COMPRA');
  const [prazo, setPrazo] = useState('');
  const [confirmarSeparacao, setConfirmarSeparacao] = useState(false);
  const [abrindo, setAbrindo] = useState(false);

  const { dados, erro, carregando, recarregar } = useCarregar(filaDeCotacao, []);
  const fila = dados ?? [];
  const lista = useMemo(() => Object.values(marcados), [marcados]);
  const selecao = situacaoDaSelecao(lista);

  function marcar(item: Marcado, ligado: boolean) {
    setMarcados((m) => {
      const proximo = { ...m };
      if (ligado) proximo[item.id] = item; else delete proximo[item.id];
      return proximo;
    });
  }

  /** O cabeçalho da SC marca ou desmarca todos os itens dela de uma vez. */
  function marcarSc(sc: ScNaFila, ligado: boolean) {
    setMarcados((m) => {
      const proximo = { ...m };
      for (const i of sc.items) {
        if (ligado) proximo[i.id] = { id: i.id, centroCusto: sc.costCenter, familia: i.family };
        else delete proximo[i.id];
      }
      return proximo;
    });
  }

  const todosDaSc = (sc: ScNaFila) => sc.items.length > 0 && sc.items.every((i) => marcados[i.id]);

  async function juntarNumProcesso() {
    setAbrindo(true);
    try {
      const q = await abrirProcesso({ prItemIds: lista.map((m) => m.id), kind: tipo, deadline: prazo || null });
      avisar(`Processo ${q.number} aberto com ${lista.length} item(ns). Abrindo para convidar os fornecedores…`);
      globalThis.location.assign(linkDoProcesso(q.id));
    } catch (e) { avisar(mensagem(e, 'Falha ao abrir o processo.'), 'erro'); }
    finally { setAbrindo(false); }
  }

  /**
   * Um processo por família. Cada abertura é independente: se uma falhar, as
   * outras seguem, e a tela diz o que abriu e o que não.
   */
  async function separarPorFamilia() {
    setConfirmarSeparacao(false);
    const grupos = agruparPorFamilia(lista.map((m) => ({ id: m.id, familia: m.familia })));
    setAbrindo(true);
    const abertos: string[] = [];
    const falhas: string[] = [];
    for (const [familia, ids] of grupos) {
      try {
        const q = await abrirProcesso({
          prItemIds: ids, kind: tipo, deadline: prazo || null,
          notes: `Processo separado da família ${familia}.`,
        });
        abertos.push(`${q.number} (${familia})`);
      } catch (e) { falhas.push(`${familia}: ${mensagem(e, 'falhou')}`); }
    }
    setAbrindo(false);
    if (abertos.length) avisar(`Abertos: ${abertos.join(' · ')}.`);
    if (falhas.length) avisar(falhas.join(' · '), 'erro');
    // com falha parcial, fica na fila para o comprador ver o que sobrou
    if (abertos.length && !falhas.length) globalThis.location.assign(linkDoProcesso());
    else recarregar();
  }

  const familiasDaSelecao = [...agruparPorFamilia(lista.map((m) => ({ id: m.id, familia: m.familia }))).keys()];

  return (
    <>
      <Painel titulo="Solicitações aguardando cotação">
        <Nota>
          As aprovadas podem ser cotadas agora; as retidas mostram de quem está a aprovação pendente.
          O comprador responsável vem de Compras → Gestão de Solicitações.
        </Nota>

        {conduz && fila.some((r) => !r.blockReason) && (
          <div className="mt-3 rounded-lg bg-superficie-suave px-3 py-3">
            <p className="sub mb-2">
              Marque os <strong>itens</strong> (do mesmo centro de custo). Junte tudo num processo — a
              compra ainda pode se dividir por família na escolha do fornecedor — ou separe por família
              desde já:
            </p>
            <div className="flex flex-wrap items-center gap-2">
              <select aria-label="Tipo de cotação" className="w-auto" value={tipo}
                onChange={(e) => setTipo(e.target.value as TipoCotacao)}>
                {(Object.keys(ROTULO_TIPO) as TipoCotacao[]).map((k) => (
                  <option key={k} value={k}>{ROTULO_TIPO[k]}</option>
                ))}
              </select>
              <input type="date" aria-label="Prazo para respostas" className="w-auto" value={prazo}
                onChange={(e) => setPrazo(e.target.value)} />
              <button type="button" className="botao" disabled={!selecao.podeJuntar || abrindo}
                onClick={juntarNumProcesso}>
                Um processo com os itens marcados ({selecao.total})
              </button>
              <button type="button" className="botao-secundario" disabled={!selecao.podeSeparar || abrindo}
                onClick={() => setConfirmarSeparacao(true)}>
                Um processo por família ({selecao.familias})
              </button>
            </div>
            {selecao.aviso && <p className="mt-2 text-[12.5px] text-perigo">{selecao.aviso}</p>}
          </div>
        )}

        {erro && <Erro>{erro}</Erro>}
        {carregando && !dados && <Carregando />}
        {dados && !fila.length && <Vazio>Nenhuma solicitação aguardando cotação. ✔</Vazio>}

        {fila.length > 0 && (
          <div className="mt-3 overflow-x-auto">
            <table data-testid="fila-cotacao" className="min-w-[960px]">
              <thead>
                <tr>
                  {conduz && <th className="w-10"></th>}
                  <th>Solicitação / item</th><th>Família</th><th>Qtde.</th>
                  <th>Valor est.</th><th>Comprador responsável</th><th>Abrir cotação</th>
                </tr>
              </thead>
              <tbody>
                {fila.map((sc) => (
                  <FragmentoSc key={sc.id} sc={sc} conduz={conduz} usuarioId={usuario.id}
                    marcados={marcados} todosMarcados={todosDaSc(sc)}
                    aoMarcarSc={(ligado) => marcarSc(sc, ligado)}
                    aoMarcarItem={marcar} />
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Painel>

      {confirmarSeparacao && (
        <Confirmacao titulo="Separar por família"
          rotuloConfirmar={`Abrir ${familiasDaSelecao.length} processos`}
          mensagem={
            <>
              Abrir <strong>{familiasDaSelecao.length}</strong> processos separados, um por família
              ({familiasDaSelecao.join(', ')})?
            </>
          }
          aoConfirmar={separarPorFamilia} aoFechar={() => setConfirmarSeparacao(false)} />
      )}
    </>
  );
}

function FragmentoSc({ sc, conduz, usuarioId, marcados, todosMarcados, aoMarcarSc, aoMarcarItem }: {
  sc: ScNaFila;
  conduz: boolean;
  usuarioId: string;
  marcados: Record<string, Marcado>;
  todosMarcados: boolean;
  aoMarcarSc: (ligado: boolean) => void;
  aoMarcarItem: (item: Marcado, ligado: boolean) => void;
}) {
  const minha = sc.assignedToId === usuarioId;
  return (
    <>
      <tr data-sc={sc.number}>
        {conduz && (
          <td>
            {!sc.blockReason && sc.items.length > 0 && (
              <input type="checkbox" className="w-auto" checked={todosMarcados}
                aria-label={`Marcar todos os itens de ${sc.number}`}
                onChange={(e) => aoMarcarSc(e.target.checked)} />
            )}
          </td>
        )}
        <td className="min-w-[260px]">
          <strong>{sc.number}</strong>
          {sc.partial && <> <Badge classe="bg-aviso-fundo text-aviso">parcialmente cotada</Badge></>}
          <div className="sub">{sc.requesterLabel} · CC: {sc.costCenter}</div>
          {sc.justification && <div className="sub">{sc.justification}</div>}
        </td>
        <td className="sub">{sc.families.join(', ') || '—'}</td>
        <td className="sub whitespace-nowrap">{sc.items.length} item(ns)</td>
        <td className="whitespace-nowrap">{moeda(sc.totalEstimatedValue)}</td>
        <td className="min-w-[170px]">
          {sc.assignedToLabel
            ? <><strong>{sc.assignedToLabel}</strong>{minha && <div className="sub">designada a você</div>}</>
            : <span className="sub">designe em Compras → Gestão de Solicitações</span>}
        </td>
        <td className="min-w-[150px]">
          {sc.blockReason
            ? <>
                <Badge classe="bg-slate-100 text-slate-600">RETIDA NA APROVAÇÃO</Badge>
                <div className="sub">{sc.blockReason}</div>
              </>
            : conduz ? <span className="sub">marque os itens ao lado</span> : '—'}
        </td>
      </tr>
      {!sc.blockReason && sc.items.map((i) => (
        <tr key={i.id} data-item={i.id}>
          {conduz && (
            <td>
              <input type="checkbox" className="w-auto" checked={!!marcados[i.id]}
                aria-label={`Marcar ${i.description} de ${sc.number}`}
                onChange={(e) => aoMarcarItem(
                  { id: i.id, centroCusto: sc.costCenter, familia: i.family }, e.target.checked)} />
            </td>
          )}
          <td className="pl-6">
            {i.catalogCode && <span className="sub">[{i.catalogCode}] </span>}{i.description}
            <div className="sub">{sc.number}/{String(i.sequence).padStart(4, '0')}</div>
          </td>
          <td>{i.family}</td>
          <td className="whitespace-nowrap">{quantidade(i.quantity)} {i.unitOfMeasure ?? ''}</td>
          <td className="sub whitespace-nowrap">
            {i.estimatedUnitPrice != null ? moeda(i.estimatedUnitPrice * i.quantity) : '—'}
          </td>
          <td colSpan={2}></td>
        </tr>
      ))}
    </>
  );
}
