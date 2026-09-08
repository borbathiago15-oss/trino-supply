import { useState } from 'react';
import { convidarFornecedor, type Processo } from '@/api/cotacoes';
import { criarFornecedor, listarFornecedores } from '@/api/fornecedores';
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
  const [novo, setNovo] = useState({ aberto: false, razaoSocial: '', cnpj: '' });
  const [criando, setCriando] = useState(false);
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

  /**
   * Pré-cadastro para cotar: razão social e CNPJ, que é tudo o que a API exige.
   * Existe porque a cotação vem antes do cadastro — o comprador chama muita gente
   * para o BID e só cadastra de verdade quem ganha. O fornecedor nasce PROSPECT:
   * concorre em pé de igualdade, e a homologação é cobrada na hora de vencer.
   */
  async function criarEConvidar() {
    const razaoSocial = novo.razaoSocial.trim();
    const cnpj = novo.cnpj.replace(/\D/g, '');
    if (razaoSocial.length < 3 || (cnpj.length !== 14 && cnpj.length !== 11)) return;
    setCriando(true);
    try {
      const f = await criarFornecedor({ legalName: razaoSocial, tradeName: null, taxId: cnpj, email: null, phone: null });
      await convidarFornecedor(q.id, [f.id]);
      aoAvisar(`${razaoSocial} entrou na cotação como pré-cadastro.`);
      setNovo({ aberto: false, razaoSocial: '', cnpj: '' });
      catalogo.recarregar();
      aoConvidar();
    } catch (e) {
      aoAvisar(mensagem(e, 'Falha ao incluir o fornecedor na cotação.'), 'erro');
    } finally { setCriando(false); }
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
            {!novo.aberto && (
              <button type="button" className="botao-secundario"
                onClick={() => setNovo((n) => ({ ...n, aberto: true }))}>
                Fornecedor fora do cadastro
              </button>
            )}
          </div>

          {novo.aberto && (
            <div data-testid="pre-cadastro-cotacao" className="mt-3 rounded-lg border border-borda bg-superficie-suave p-3">
              <div className="flex flex-wrap items-end gap-2">
                <Campo id="rfq-novo-nome" rotulo="Razão social" className="min-w-[260px] flex-1">
                  <input id="rfq-novo-nome" value={novo.razaoSocial} placeholder="nome da empresa"
                    onChange={(e) => setNovo((n) => ({ ...n, razaoSocial: e.target.value }))} />
                </Campo>
                <Campo id="rfq-novo-cnpj" rotulo="CNPJ" className="min-w-[180px]">
                  <input id="rfq-novo-cnpj" value={novo.cnpj} placeholder="somente números"
                    onChange={(e) => setNovo((n) => ({ ...n, cnpj: e.target.value }))} />
                </Campo>
                <button type="button" className="botao" disabled={criando} onClick={criarEConvidar}>
                  {criando ? 'Incluindo…' : 'Incluir na cotação'}
                </button>
                <button type="button" className="botao-secundario" disabled={criando}
                  onClick={() => setNovo({ aberto: false, razaoSocial: '', cnpj: '' })}>
                  Cancelar
                </button>
              </div>
              <Nota>
                Pré-cadastro para cotar: razão social e CNPJ bastam. O fornecedor entra como
                <strong> PROSPECT</strong> e concorre normalmente — <strong>só não pode vencer</strong> antes
                de o gestor de suprimentos homologá-lo. É o que permite chamar todo mundo para o BID e
                cadastrar de verdade apenas quem ganhar.
              </Nota>
            </div>
          )}

          <Nota>
            O fornecedor responde pelo Portal com CNPJ + chave de acesso — gere a chave em
            Cadastros → Fornecedores. O convite fica registrado na auditoria do processo.
          </Nota>
        </>
      )}
    </Painel>
  );
}
