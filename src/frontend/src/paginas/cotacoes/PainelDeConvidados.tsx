import { useState } from 'react';
import {
  convidarFornecedor, dispensarConvite, prorrogarConvite, situacaoDoConvite,
  type FornecedorConvidado, type Processo,
} from '@/api/cotacoes';
import { acharFornecedor, criarFornecedor, listarFornecedores } from '@/api/fornecedores';
import { Aviso, Badge, Painel, Vazio } from '@/componentes/basicos';
import { DialogoMotivo } from '@/componentes/DialogoMotivo';
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
  const [prazo, setPrazo] = useState('');
  // o fornecedor de quem se espera resposta e o que se vai fazer com ele
  const [novoPrazo, setNovoPrazo] = useState<{ s: FornecedorConvidado; data: string } | null>(null);
  const [dispensando, setDispensando] = useState<FornecedorConvidado | null>(null);
  const [novo, setNovo] = useState({ aberto: false, razaoSocial: '', telefone: '', cnpj: '' });
  const [criando, setCriando] = useState(false);
  const catalogo = useCarregar(
    async (signal) => listarFornecedores(false, signal).catch(() => []), []);

  const naoConvidados = (catalogo.dados ?? [])
    .filter((f) => !q.suppliers.some((s) => s.supplierId === f.id));

  async function convidar() {
    if (!convidado) return;
    try {
      await convidarFornecedor(q.id, [convidado], prazo || null);
      aoAvisar(prazo
        ? `Fornecedor convidado, com prazo até ${data(prazo)}.`
        : 'Fornecedor convidado.');
      setConvidado('');
      aoConvidar();
    } catch (e) { aoAvisar(mensagem(e, 'Falha ao convidar o fornecedor.'), 'erro'); }
  }

  /**
   * Pré-cadastro para cotar: razão social e telefone, que é tudo o que a API exige (§7).
   * Existe porque a cotação vem antes do cadastro — o comprador pede preço por telefone
   * ou WhatsApp e, nessa hora, o CNPJ ele não tem. O fornecedor nasce PROSPECT: concorre
   * em pé de igualdade, e o cadastro completo é cobrado de quem ganhar o BID.
   *
   * **Quem já está no cadastro é reaproveitado, não recriado.** O pedido aqui é "coloque
   * este fornecedor na cotação", e não "crie um registro": cadastrar de novo deixava a
   * cotação presa a um PROSPECT sem CNPJ enquanto o homologado era outra linha com o
   * mesmo nome — e o comprador via "não pode vencer" num fornecedor que ele mesmo tinha
   * homologado. Inativo não se convida calado: reativar é decisão do cadastro.
   */
  async function criarEConvidar() {
    const razaoSocial = novo.razaoSocial.trim();
    const telefone = novo.telefone.replace(/\D/g, '');
    const cnpj = novo.cnpj.replace(/\D/g, '');
    if (razaoSocial.length < 3 || telefone.length < 10) return;
    // CNPJ é opcional, mas informado errado não vale a viagem até o servidor
    if (cnpj.length > 0 && cnpj.length !== 14 && cnpj.length !== 11) {
      aoAvisar('CPF/CNPJ inválido: informe 11 ou 14 dígitos, ou deixe em branco.', 'erro');
      return;
    }
    setCriando(true);
    try {
      const existente = await acharFornecedor(razaoSocial, cnpj);
      if (existente && !existente.active) {
        aoAvisar(`${existente.legalName} já está no cadastro, mas inativo. `
          + 'Reative-o em Fornecedores para convidá-lo.', 'erro');
        return;
      }
      const id = existente?.id ?? (await criarFornecedor({
        legalName: razaoSocial, tradeName: null, taxId: cnpj || null,
        email: null, phone: novo.telefone.trim(),
      })).id;
      await convidarFornecedor(q.id, [id], prazo || null);
      aoAvisar(existente
        ? `${existente.legalName} já estava no cadastro e entrou na cotação.`
        : `${razaoSocial} entrou na cotação como pré-cadastro.`);
      setNovo({ aberto: false, razaoSocial: '', telefone: '', cnpj: '' });
      catalogo.recarregar();
      aoConvidar();
    } catch (e) {
      aoAvisar(mensagem(e, 'Falha ao incluir o fornecedor na cotação.'), 'erro');
    } finally { setCriando(false); }
  }

  async function prorrogar() {
    if (!novoPrazo?.data) return;
    const { s, data: ate } = novoPrazo;
    setNovoPrazo(null);
    try {
      await prorrogarConvite(q.id, s.supplierId, ate);
      aoAvisar(`${s.supplierName} tem até ${data(ate)} para responder.`);
      aoConvidar();
    } catch (e) { aoAvisar(mensagem(e, 'Falha ao prorrogar o prazo.'), 'erro'); }
  }

  async function dispensar(motivo: string) {
    const s = dispensando;
    setDispensando(null);
    if (!s) return;
    try {
      await dispensarConvite(q.id, s.supplierId, motivo);
      aoAvisar(`O processo segue sem ${s.supplierName}.`);
      aoConvidar();
    } catch (e) { aoAvisar(mensagem(e, 'Falha ao seguir sem o fornecedor.'), 'erro'); }
  }

  async function copiarConvite(nome: string) {
    const texto = textoDoConvite(q, globalThis.location.origin);
    try {
      await navigator.clipboard.writeText(texto);
      aoAvisar(`Convite de ${nome} copiado.`);
    } catch { aoAvisar('Não foi possível copiar. Selecione o texto do convite manualmente.', 'erro'); }
  }

  const atrasados = q.suppliers.filter((s) => s.late);

  return (
    <Painel titulo="Fornecedores convidados">
      {atrasados.length > 0 && (
        <Aviso testid="convites-atrasados">
          {atrasados.length === 1
            ? `${atrasados[0].supplierName} passou do prazo e não enviou proposta.`
            : `${atrasados.length} fornecedores passaram do prazo sem enviar proposta.`}
          {' '}Dê um novo prazo ou siga sem {atrasados.length === 1 ? 'ele' : 'eles'} — o processo
          não anda esperando quem já não respondeu.
        </Aviso>
      )}

      {!q.suppliers.length && <Vazio>Nenhum fornecedor convidado ainda.</Vazio>}
      {q.suppliers.length > 0 && (
        <div className="overflow-x-auto">
          <table data-testid="fornecedores-convidados">
            <thead>
              <tr>
                <th>Fornecedor</th><th>CNPJ</th><th>Convite</th>
                <th>Prazo de resposta</th><th>Proposta</th><th></th>
              </tr>
            </thead>
            <tbody>
              {q.suppliers.map((s) => (
                <tr key={s.supplierId} data-fornecedor={s.supplierName}>
                  <td>{s.supplierName}</td>
                  <td>{s.taxId}</td>
                  <td className="sub">{data(s.invitedAt)} por {s.invitedByLabel ?? '—'}</td>
                  <td className="whitespace-nowrap">
                    {s.responseDeadline ? data(s.responseDeadline) : <span className="sub">a combinar</span>}
                    {s.extensions > 0 && (
                      <div className="sub">
                        prorrogado {s.extensions}×
                      </div>
                    )}
                  </td>
                  <td>
                    <Badge classe={situacaoDoConvite(s).classe}>{situacaoDoConvite(s).rotulo}</Badge>
                    {s.waived && s.waivedReason && <div className="sub">{s.waivedReason}</div>}
                  </td>
                  <td className="whitespace-nowrap">
                    <button type="button" className="botao-secundario" onClick={() => copiarConvite(s.supplierName)}>
                      Copiar convite
                    </button>
                    {/* as duas saídas só aparecem para quem de fato ainda se espera */}
                    {podeConvidar && !s.hasProposal && !s.waived && (
                      <>
                        {' '}
                        <button type="button" className="botao-secundario"
                          data-testid={`novo-prazo-${s.supplierId}`}
                          onClick={() => setNovoPrazo({ s, data: '' })}>
                          Novo prazo
                        </button>
                        {' '}
                        <button type="button" className="botao-secundario"
                          data-testid={`seguir-sem-${s.supplierId}`}
                          onClick={() => setDispensando(s)}>
                          Seguir sem ele
                        </button>
                      </>
                    )}
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
            <Campo id="rfq-prazo" rotulo="Prazo para responder" dica="opcional" className="min-w-[170px]">
              <input id="rfq-prazo" type="date" value={prazo} onChange={(e) => setPrazo(e.target.value)} />
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
                <Campo id="rfq-novo-telefone" rotulo="Telefone" className="min-w-[180px]">
                  <input id="rfq-novo-telefone" value={novo.telefone} placeholder="(81) 99999-0000"
                    onChange={(e) => setNovo((n) => ({ ...n, telefone: e.target.value }))} />
                </Campo>
                <Campo id="rfq-novo-cnpj" rotulo="CNPJ (opcional)" className="min-w-[180px]">
                  <input id="rfq-novo-cnpj" value={novo.cnpj} placeholder="informe depois, se ganhar"
                    onChange={(e) => setNovo((n) => ({ ...n, cnpj: e.target.value }))} />
                </Campo>
                <button type="button" className="botao" disabled={criando} onClick={criarEConvidar}>
                  {criando ? 'Incluindo…' : 'Incluir na cotação'}
                </button>
                <button type="button" className="botao-secundario" disabled={criando}
                  onClick={() => setNovo({ aberto: false, razaoSocial: '', telefone: '', cnpj: '' })}>
                  Cancelar
                </button>
              </div>
              <Nota>
                Pré-cadastro para cotar: <strong>razão social e telefone bastam</strong> — o CNPJ pode
                ficar para depois. O fornecedor entra como <strong>PROSPECT</strong> e concorre
                normalmente; <strong>só não pode vencer</strong> antes do cadastro completo e da
                homologação pelo gestor de suprimentos. É o que permite chamar todo mundo para o BID e
                cadastrar de verdade apenas quem ganhar.
              </Nota>
            </div>
          )}

          <Nota>
            O fornecedor responde pelo Portal com CNPJ + chave de acesso — gere a chave em
            Cadastros → Fornecedores. O convite fica registrado na auditoria do processo.{' '}
            <strong>O prazo é deste convite</strong>, não do processo: convidar em dias
            diferentes com um prazo só cobraria do último a folga dada ao primeiro. Em branco,
            vale o prazo do processo{q.deadline ? ` (${data(q.deadline)})` : ''}.
          </Nota>
        </>
      )}
      {novoPrazo && (
        <div data-testid="dialogo-novo-prazo"
          className="mt-3 rounded-lg border border-borda bg-superficie-suave p-3">
          <div className="flex flex-wrap items-end gap-2">
            <Campo id="rfq-novo-prazo" rotulo={`Novo prazo para ${novoPrazo.s.supplierName}`}
              className="min-w-[200px]">
              <input id="rfq-novo-prazo" type="date" value={novoPrazo.data}
                onChange={(e) => setNovoPrazo((n) => (n ? { ...n, data: e.target.value } : n))} />
            </Campo>
            <button type="button" className="botao" disabled={!novoPrazo.data} onClick={prorrogar}>
              Dar novo prazo
            </button>
            <button type="button" className="botao-secundario" onClick={() => setNovoPrazo(null)}>
              Cancelar
            </button>
          </div>
          <Nota>
            O prazo vencido barra a proposta no Portal — é esta data que reabre a porta para
            ele, e não só o aviso da tela.
          </Nota>
        </div>
      )}

      {dispensando && (
        <DialogoMotivo
          titulo={`Seguir sem ${dispensando.supplierName}`}
          rotulo="Por que o processo segue sem este fornecedor?"
          dica="mínimo 10 caracteres — é o que explica depois um BID com menos proponentes"
          rotuloConfirmar={`Seguir sem ${dispensando.supplierName}`}
          obrigatorio
          aoConfirmar={dispensar}
          aoFechar={() => setDispensando(null)}
        />
      )}
    </Painel>
  );
}
