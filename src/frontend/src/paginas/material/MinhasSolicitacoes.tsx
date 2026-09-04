import { useState } from 'react';
import { Link } from 'react-router-dom';
import {
  cancelarMaterial, listarSolicitacoesMaterial, podeCancelarMaterial,
  ROTULO_ITEM_MATERIAL, ROTULO_MATERIAL, type SolicitacaoMaterial,
} from '@/api/material';
import { Badge, Carregando, Erro, Painel, Vazio } from '@/componentes/basicos';
import { DialogoMotivo } from '@/componentes/DialogoMotivo';
import { useToast } from '@/componentes/Toast';
import { useUsuario } from '@/sessao/SessaoProvider';
import { quantidade } from '@/util/formato';
import { useCarregar } from '@/util/useCarregar';

const mensagem = (e: unknown, padrao: string) => (e instanceof Error ? e.message : padrao);

/** Um item em uma linha: quanto foi pedido, o que é, e onde ele está. */
export const descricaoDoItem = (i: SolicitacaoMaterial['items'][number]) => {
  const situacao = i.status ? ROTULO_ITEM_MATERIAL[i.status] ?? i.status : null;
  const codigo = i.catalogCode ? `[${i.catalogCode}] ` : '';
  return `${quantidade(i.quantity)}× ${codigo}${i.description}${situacao ? ` (${situacao})` : ''}`;
};

export function MinhasSolicitacoes() {
  const usuario = useUsuario();
  const { avisar } = useToast();
  const [aCancelar, setACancelar] = useState<SolicitacaoMaterial | null>(null);
  const { dados, erro, carregando, recarregar } = useCarregar(listarSolicitacoesMaterial, []);
  const lista = dados ?? [];

  async function cancelar(r: SolicitacaoMaterial, motivo: string) {
    setACancelar(null);
    try {
      await cancelarMaterial(r.id, motivo);
      avisar('Solicitação cancelada.');
      recarregar();
    } catch (e) { avisar(mensagem(e, 'Falha ao cancelar a solicitação.'), 'erro'); }
  }

  return (
    <>
      <Painel titulo="Minhas Solicitações de Material"
        acoes={<Link className="botao" to="/material/nova">Solicitar material</Link>}>
        {erro && <Erro>{erro}</Erro>}
        {carregando && !dados && <Carregando />}
        {dados && !lista.length && (
          <Vazio>Nenhuma solicitação de material ainda. Comece pelo botão “Solicitar material”.</Vazio>
        )}
        {lista.length > 0 && (
          <div className="overflow-x-auto">
            <table data-testid="tabela-material" className="min-w-[860px]">
              <thead>
                <tr><th>Número</th><th>Itens</th><th>Situação</th><th>Ações</th></tr>
              </thead>
              <tbody>
                {lista.map((r) => {
                  const marca = ROTULO_MATERIAL[r.status] ?? { rotulo: r.status, classe: 'bg-slate-100 text-slate-600' };
                  return (
                    <tr key={r.id} data-material={r.number}>
                      <td className="whitespace-nowrap">
                        <span className="font-semibold">{r.number}</span>
                        <div className="sub">CC: {r.costCenter}</div>
                      </td>
                      <td className="min-w-[360px]">
                        {r.items.map((i) => <div key={i.itemId}>{descricaoDoItem(i)}</div>)}
                        {r.notes && <div className="sub">Obs.: {r.notes}</div>}
                        {r.fulfilledByLabel && <div className="sub">Atendida por {r.fulfilledByLabel}</div>}
                        {r.purchaseRequisitionNumber && (
                          <div className="sub">O que faltou virou a solicitação de compra {r.purchaseRequisitionNumber}.</div>
                        )}
                        {(r.cancelReason || r.decisionReason) && (
                          <div className="sub">Motivo: {r.cancelReason ?? r.decisionReason}</div>
                        )}
                      </td>
                      <td><Badge classe={marca.classe}>{marca.rotulo}</Badge></td>
                      <td className="whitespace-nowrap">
                        {podeCancelarMaterial(r, usuario.id) && (
                          <button type="button" className="botao-perigo" onClick={() => setACancelar(r)}>Cancelar</button>
                        )}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </Painel>

      {aCancelar && (
        <DialogoMotivo titulo={`Cancelar a solicitação ${aCancelar.number}`}
          rotulo="Motivo do cancelamento" dica="(obrigatório)" rotuloConfirmar="Cancelar solicitação"
          obrigatorio perigo aoConfirmar={(motivo) => cancelar(aCancelar, motivo)}
          aoFechar={() => setACancelar(null)} />
      )}
    </>
  );
}
