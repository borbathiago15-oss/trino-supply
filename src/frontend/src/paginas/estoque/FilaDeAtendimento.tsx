import { useEffect, useState } from 'react';
import {
  atenderMaterial, filaDoAlmoxarifado, type ItemMaterial, type SolicitacaoMaterial,
} from '@/api/material';
import { Carregando, Erro, Painel, Vazio } from '@/componentes/basicos';
import { Nota } from '@/componentes/formulario';
import { useToast } from '@/componentes/Toast';
import { useUsuario } from '@/sessao/SessaoProvider';
import { quantidade } from '@/util/formato';
import { rolarPara } from '@/util/rolar';
import { useCarregar } from '@/util/useCarregar';
import { rotuloDoCentro } from '@/dominio/centrosDeCusto';
import { useNomesDosCentros } from '@/util/useNomesDosCentros';

const mensagem = (e: unknown, padrao: string) => (e instanceof Error ? e.message : padrao);

/** O que vale para o atendimento: o aprovado quando o Nível 1 cortou, senão o pedido. */
export const aprovado = (i: ItemMaterial) => i.effectiveQuantity ?? i.approvedQuantity ?? i.quantity;

/**
 * Quanto ainda falta entregar deste item. É o teto de cada entrega — e não o aprovado:
 * a solicitação que ficou pendente já tem entrega registrada, e cobrar contra o aprovado
 * deixaria entregar duas vezes a mesma quantidade.
 */
export const falta = (i: ItemMaterial) => i.pendingQuantity ?? (aprovado(i) - (i.fulfilledQuantity ?? 0));

/**
 * O que a tela cobra antes de gastar a chamada.
 *
 * <p>
 * Nada entregue só faz sentido quando se <b>conclui</b> o atendimento — aí quer dizer "não tinha
 * nada, segue para compra". Deixando pendente, nada entregue não registra coisa alguma, e o
 * caminho para não atender agora é fechar o painel.
 * </p>
 */
export function validarAtendimento(
  entregas: Record<string, string>, itens: ItemMaterial[], concluir = true,
) {
  const linhas = itens.map((i) => ({
    itemId: i.itemId,
    quantity: Number((entregas[i.itemId] ?? '').replace(',', '.')) || 0,
    limite: falta(i),
  }));
  const acima = linhas.find((l) => l.quantity > l.limite);
  if (acima) return { items: [], erro: 'Não dá para entregar mais do que ainda falta.' };
  if (!concluir && !linhas.some((l) => l.quantity > 0))
    return { items: [], erro: 'Informe ao menos uma quantidade entregue — para não atender agora, feche o painel.' };
  return { items: linhas.map(({ itemId, quantity }) => ({ itemId, quantity })), erro: null };
}

export function FilaDeAtendimento() {
  const nomesDosCentros = useNomesDosCentros();
  // as duas decisões de quem atende, independentes de propósito: encerrar ou deixar na fila,
  // e comprar ou não o que faltou
  const [concluir, setConcluir] = useState(true);
  const [gerarCompra, setGerarCompra] = useState(true);
  const usuario = useUsuario();
  const { avisar } = useToast();
  const [somenteMinhas, setSomenteMinhas] = useState(false);
  const [atendendo, setAtendendo] = useState<SolicitacaoMaterial | null>(null);
  const [entregas, setEntregas] = useState<Record<string, string>>({});
  const [salvando, setSalvando] = useState(false);

  const { dados, erro, carregando, recarregar } = useCarregar(
    (signal) => filaDoAlmoxarifado(somenteMinhas, signal), [somenteMinhas]);
  const fila = dados ?? [];

  // a solicitação atendida sai da fila: fecha o painel em vez de deixá-lo órfão
  useEffect(() => {
    if (atendendo && dados && !dados.some((r) => r.id === atendendo.id)) setAtendendo(null);
  }, [dados, atendendo]);

  function abrir(r: SolicitacaoMaterial) {
    setAtendendo(r);
    // começa com tudo o que foi aprovado: o almoxarife zera o que não tinha
    setEntregas(Object.fromEntries(r.items.map((i) => [i.itemId, String(falta(i))])));
    // cada solicitação começa no padrão: a decisão da anterior não se arrasta para esta
    setConcluir(true); setGerarCompra(true);
    rolarPara('painel-atendimento');
  }

  const atenderTudo = () => atendendo &&
    setEntregas(Object.fromEntries(atendendo.items.map((i) => [i.itemId, String(falta(i))])));

  async function confirmar() {
    if (!atendendo) return;
    const { items, erro: problema } = validarAtendimento(entregas, atendendo.items, concluir);
    if (problema) { avisar(problema, 'erro'); return; }
    setSalvando(true);
    try {
      const mr = await atenderMaterial(atendendo.id, items, { concluir, gerarCompra });
      const compra = mr.purchaseRequisitionNumber
        ? ` O faltante virou a solicitação ${mr.purchaseRequisitionNumber}.` : '';
      avisar(concluir
        ? `Atendimento concluído.${compra}`
        : `Entrega registrada. A solicitação continua na fila do estoque.${compra}`);
      setAtendendo(null);
      recarregar();
    } catch (e) { avisar(mensagem(e, 'Falha ao registrar o atendimento.'), 'erro'); }
    finally { setSalvando(false); }
  }

  return (
    <>
      <Painel titulo="Fila de Atendimento do Almoxarifado" acoes={
        <select aria-label="Escopo da fila" className="w-auto" value={somenteMinhas ? 'MINHAS' : 'TODAS'}
          onChange={(e) => setSomenteMinhas(e.target.value === 'MINHAS')}>
          <option value="TODAS">Toda a fila</option>
          <option value="MINHAS">Designadas a mim</option>
        </select>
      }>
        <Nota>
          Só entra aqui o que o responsável do centro aprovou. Informe quanto você entregou de cada
          item — o que faltar vira solicitação de compra no nome de quem pediu.
        </Nota>

        {erro && <Erro>{erro}</Erro>}
        {carregando && !dados && <Carregando />}
        {dados && !fila.length && (
          <Vazio>
            {somenteMinhas
              ? 'Nenhuma solicitação designada a você. Troque o filtro para ver toda a fila.'
              : 'Nenhuma solicitação aprovada aguardando atendimento.'}
          </Vazio>
        )}

        {fila.length > 0 && (
          <div className="mt-3 overflow-x-auto">
            <table data-testid="fila-almoxarifado" className="min-w-[900px]">
              <thead>
                <tr><th>Solicitação</th><th>Solicitante</th><th>Itens aprovados</th><th>Responsável</th><th></th></tr>
              </thead>
              <tbody>
                {fila.map((r) => (
                  <tr key={r.id} data-material={r.number}>
                    <td className="whitespace-nowrap">
                      <span className="font-semibold">{r.number}</span>
                      <div className="sub" title={r.costCenter}>CC: {rotuloDoCentro(nomesDosCentros, r.costCenter)}</div>
                    </td>
                    <td>
                      {r.requesterLabel}
                      {r.notes && <div className="sub">{r.notes}</div>}
                    </td>
                    <td className="min-w-[280px]">
                      {r.items.map((i) => (
                        <div key={i.itemId}>{quantidade(aprovado(i))}× {i.description}</div>
                      ))}
                      {r.approvedByLabel && <div className="sub">aprovado por {r.approvedByLabel}</div>}
                    </td>
                    <td className="min-w-[160px]">
                      {r.assignedToLabel
                        ? <>
                            <strong>{r.assignedToLabel}</strong>
                            {r.assignedToId === usuario.id && <div className="sub">designada a você</div>}
                          </>
                        : <span className="sub">sem responsável (Triagem de Demandas)</span>}
                    </td>
                    <td>
                      <button type="button" className="botao" onClick={() => abrir(r)}>Atender</button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Painel>

      {atendendo && (
        <Painel id="painel-atendimento" titulo={`Atendimento da ${atendendo.number} — ${atendendo.requesterLabel}`}
          acoes={<button type="button" className="botao-secundario" onClick={() => setAtendendo(null)}>Fechar</button>}>
          <div className="overflow-x-auto">
            <table data-testid="itens-atendimento" className="min-w-[620px]">
              <thead>
                <tr><th>Produto</th><th>Pedido</th><th>Aprovado</th><th className="w-40">Entregue agora</th></tr>
              </thead>
              <tbody>
                {atendendo.items.map((i) => (
                  <tr key={i.itemId} data-item={i.itemId}>
                    <td>{i.catalogCode ? `[${i.catalogCode}] ` : ''}{i.description}</td>
                    <td className="sub whitespace-nowrap">{quantidade(i.quantity)} {i.unitOfMeasure}</td>
                    <td className="whitespace-nowrap">
                      {quantidade(falta(i))} {i.unitOfMeasure}
                      {/* já entregue: a solicitação que ficou pendente volta com parte do saldo */}
                      {(i.fulfilledQuantity ?? 0) > 0 && (
                        <div className="sub">já entregue {quantidade(i.fulfilledQuantity ?? 0)}</div>
                      )}
                    </td>
                    <td>
                      <input type="number" min="0" step="0.01" max={falta(i)}
                        aria-label={`Entregue de ${i.description}`}
                        value={entregas[i.itemId] ?? ''}
                        onChange={(e) => setEntregas((q) => ({ ...q, [i.itemId]: e.target.value }))} />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {/* as duas decisões de quem atende. Antes nem existiam: todo atendimento encerrava a
              solicitação e comprava o faltante, então ou se esperava a carga sem registrar o que
              já saiu, ou se comprava o que já estava a caminho */}
          <div className="mt-4 rounded-lg border border-borda bg-superficie-suave p-3">
            <label className="flex items-start gap-2 text-[13px]">
              <input type="checkbox" className="mt-0.5 w-auto" checked={concluir}
                data-testid="atend-concluir" onChange={(e) => setConcluir(e.target.checked)} />
              <span>
                <strong>Concluir o atendimento</strong> — encerra a solicitação mesmo entregando em
                parte. Desmarque quando o resto chega depois: a solicitação fica na fila do estoque
                com o que já saiu registrado.
              </span>
            </label>
            <label className="mt-2 flex items-start gap-2 text-[13px]">
              <input type="checkbox" className="mt-0.5 w-auto" checked={gerarCompra}
                data-testid="atend-compra" onChange={(e) => setGerarCompra(e.target.checked)} />
              <span>
                <strong>Comprar o que faltou</strong> — abre a solicitação de compra no nome de
                {' '}{atendendo.requesterLabel}, no centro
                {' '}{rotuloDoCentro(nomesDosCentros, atendendo.costCenter)}. Desmarque quando o
                material já está a caminho.
              </span>
            </label>
          </div>

          <div className="mt-3 flex flex-wrap gap-2">
            <button type="button" className="botao" disabled={salvando} onClick={confirmar}>
              {salvando ? 'Registrando…' : concluir ? 'Confirmar atendimento' : 'Registrar entrega parcial'}
            </button>
            <button type="button" className="botao-secundario" onClick={atenderTudo}>Atender tudo</button>
          </div>
        </Painel>
      )}
    </>
  );
}
