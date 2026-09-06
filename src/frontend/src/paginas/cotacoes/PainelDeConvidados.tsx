import { useState } from 'react';
import { convidarFornecedor, type Processo } from '@/api/cotacoes';
import { listarFornecedores } from '@/api/fornecedores';
import { Badge, Painel, Vazio } from '@/componentes/basicos';
import { Campo, Nota } from '@/componentes/formulario';
import { data } from '@/util/formato';
import { useCarregar } from '@/util/useCarregar';

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

/**
 * Quem foi convidado, quem já respondeu e o convite de quem falta.
 *
 * Saiu de `ProcessoDetalhe.tsx` no INT-B, e trouxe junto o que só ele usava: a
 * lista de fornecedores do cadastro, o campo do convite e o texto copiado.
 */
export function PainelDeConvidados({ processo: q, podeConvidar, aoConvidar, aoAvisar }: {
  processo: Processo;
  podeConvidar: boolean;
  aoConvidar: () => void;
  aoAvisar: (t: string, tipo?: 'ok' | 'erro') => void;
}) {
  const [convidado, setConvidado] = useState('');
  const catalogo = useCarregar(
    async (signal) => listarFornecedores(false, signal).catch(() => []), []);

  const naoConvidados = (catalogo.dados ?? [])
    .filter((f) => !q.suppliers.some((s) => s.supplierId === f.id));

  async function convidar() {
    if (!convidado) return;
    try {
      await convidarFornecedor(q.id, [convidado]);
      aoAvisar('Fornecedor convidado.');
      setConvidado('');
      aoConvidar();
    } catch (e) { aoAvisar(mensagem(e, 'Falha ao convidar o fornecedor.'), 'erro'); }
  }

  async function copiarConvite(nome: string) {
    const texto = textoDoConvite(q, globalThis.location.origin);
    try {
      await navigator.clipboard.writeText(texto);
      aoAvisar(`Convite de ${nome} copiado.`);
    } catch { aoAvisar('Não foi possível copiar. Selecione o texto do convite manualmente.', 'erro'); }
  }

  return (
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

      {podeConvidar && (
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
  );
}
