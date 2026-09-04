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

const mensagem = (e: unknown, padrao: string) => (e instanceof Error ? e.message : padrao);

/** O que vale para o atendimento: o aprovado quando o Nível 1 cortou, senão o pedido. */
export const aprovado = (i: ItemMaterial) => i.effectiveQuantity ?? i.approvedQuantity ?? i.quantity;

/**
 * Nada entregue é atendimento nenhum — e o backend recusa. A tela diz isso antes
 * de gastar a chamada; para não atender, o caminho é fechar o painel.
 */
export function validarAtendimento(entregas: Record<string, string>, itens: ItemMaterial[]) {
  const linhas = itens.map((i) => ({
    itemId: i.itemId,
    quantity: Number((entregas[i.itemId] ?? '').replace(',', '.')) || 0,
    limite: aprovado(i),
  }));
  const acima = linhas.find((l) => l.quantity > l.limite);
  if (acima) return { items: [], erro: 'Não dá para entregar mais do que foi aprovado.' };
  if (!linhas.some((l) => l.quantity > 0))
    return { items: [], erro: 'Informe ao menos uma quantidade entregue — para não atender agora, feche o painel.' };
  return { items: linhas.map(({ itemId, quantity }) => ({ itemId, quantity })), erro: null };
}

export function FilaDeAtendimento() {
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
    setEntregas(Object.fromEntries(r.items.map((i) => [i.itemId, String(aprovado(i))])));
    rolarPara('painel-atendimento');
  }

  const atenderTudo = () => atendendo &&
    setEntregas(Object.fromEntries(atendendo.items.map((i) => [i.itemId, String(aprovado(i))])));

  async function confirmar() {
    if (!atendendo) return;
    const { items, erro: problema } = validarAtendimento(entregas, atendendo.items);
    if (problema) { avisar(problema, 'erro'); return; }
    setSalvando(true);
    try {
      const mr = await atenderMaterial(atendendo.id, items);
      avisar(mr.purchaseRequisitionNumber
        ? `Atendimento registrado. O faltante virou a solicitação ${mr.purchaseRequisitionNumber}.`
        : 'Atendimento registrado.');
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
                      <div className="sub">CC: {r.costCenter}</div>
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
                        : <span className="sub">sem responsável (Gestão de Solicitações)</span>}
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
                    <td className="whitespace-nowrap">{quantidade(aprovado(i))} {i.unitOfMeasure}</td>
                    <td>
                      <input type="number" min="0" step="0.01" max={aprovado(i)}
                        aria-label={`Entregue de ${i.description}`}
                        value={entregas[i.itemId] ?? ''}
                        onChange={(e) => setEntregas((q) => ({ ...q, [i.itemId]: e.target.value }))} />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <div className="mt-4 flex flex-wrap gap-2">
            <button type="button" className="botao" disabled={salvando} onClick={confirmar}>
              {salvando ? 'Registrando…' : 'Confirmar atendimento'}
            </button>
            <button type="button" className="botao-secundario" onClick={atenderTudo}>Atender tudo</button>
          </div>
          <Nota>
            Deixe zero no que não tinha em estoque: o faltante vira uma solicitação de compra no nome
            de {atendendo.requesterLabel}, no centro {atendendo.costCenter}.
          </Nota>
        </Painel>
      )}
    </>
  );
}
