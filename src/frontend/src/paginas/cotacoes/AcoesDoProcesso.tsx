import { useState } from 'react';
import {
  CRITERIOS, escolherVencedor, propostasVigentes, registrarOc,
  type Processo, type Proposta,
} from '@/api/cotacoes';
import { Nota } from '@/componentes/formulario';
import { Campo, Grade2 } from '@/componentes/formulario';
import { moeda } from '@/util/formato';

const mensagem = (e: unknown, padrao: string) => (e instanceof Error ? e.message : padrao);

/**
 * Escolha do vencedor com uma família só. A justificativa é obrigatória — é
 * ela que sustenta a decisão na auditoria do processo.
 */
export function FormVencedor({ processo, aoConcluir, aoAvisar }: {
  processo: Processo;
  aoConcluir: () => void;
  aoAvisar: (t: string, tipo?: 'ok' | 'erro') => void;
}) {
  const vigentes = propostasVigentes(processo);
  const [propostaId, setPropostaId] = useState('');
  const [criterios, setCriterios] = useState<string[]>([]);
  const [justificativa, setJustificativa] = useState('');
  const [salvando, setSalvando] = useState(false);

  const alternar = (c: string) =>
    setCriterios((atual) => atual.includes(c) ? atual.filter((x) => x !== c) : [...atual, c]);

  async function confirmar() {
    setSalvando(true);
    try {
      await escolherVencedor(processo.id, { proposalId: propostaId, criteria: criterios, justification: justificativa.trim() });
      aoAvisar('Fornecedor escolhido. O processo seguiu para a aprovação.');
      aoConcluir();
    } catch (e) { aoAvisar(mensagem(e, 'Falha ao registrar a escolha.'), 'erro'); }
    finally { setSalvando(false); }
  }

  return (
    <div data-testid="form-vencedor">
      <h3 className="mb-2 text-[14px] font-bold">Escolher fornecedor vencedor</h3>
      <div className="overflow-x-auto">
        <table>
          <tbody>
            {vigentes.map((p) => (
              <tr key={p.id}>
                <td className="w-8">
                  <input type="radio" name="vencedor" className="w-auto" value={p.id}
                    checked={propostaId === p.id} aria-label={`Escolher ${p.supplierName}`}
                    onChange={() => setPropostaId(p.id)} />
                </td>
                <td>{p.supplierName} <span className="sub">v{p.version} · {p.submittedVia}</span></td>
                <td className="whitespace-nowrap"><strong>{moeda(p.totalValue)}</strong></td>
                <td className="sub whitespace-nowrap">
                  {p.deliveryDays ?? '—'} dias · {p.paymentTerms || '—'}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <p className="mb-1 mt-3 text-[12.5px] font-semibold text-texto-suave">Critérios utilizados</p>
      <div className="flex flex-wrap gap-x-4 gap-y-1.5">
        {CRITERIOS.map((c) => (
          <label key={c} className="flex items-center gap-1.5 font-normal">
            <input type="checkbox" className="w-auto" checked={criterios.includes(c)} onChange={() => alternar(c)} />
            {c}
          </label>
        ))}
      </div>

      <Campo id="rfq-justificativa" rotulo="Justificativa da escolha" dica="(obrigatória)" className="mt-3">
        <input id="rfq-justificativa" value={justificativa} placeholder="Por que este fornecedor vence?"
          onChange={(e) => setJustificativa(e.target.value)} />
      </Campo>

      <button type="button" className="botao mt-3" disabled={!propostaId || !justificativa.trim() || salvando}
        onClick={confirmar}>
        {salvando ? 'Registrando…' : 'Confirmar escolha e enviar à aprovação'}
      </button>
      <Nota>A escolha e os critérios ficam registrados na auditoria do processo.</Nota>
    </div>
  );
}

/** Propostas que cotaram ao menos um item da família. */
export function propostasDaFamilia(processo: Processo, familia: string): Proposta[] {
  const itens = processo.items.filter((i) => i.family === familia).map((i) => i.id);
  return propostasVigentes(processo)
    .filter((p) => p.items.some((x) => itens.includes(x.quotationItemId) && x.unitPrice > 0));
}

/**
 * Compra dividida: um fornecedor por família. Quem ganhar mais de uma família
 * recebe todas na mesma O.C.
 */
export function FormAdjudicacao({ processo, aoConcluir, aoAvisar }: {
  processo: Processo;
  aoConcluir: () => void;
  aoAvisar: (t: string, tipo?: 'ok' | 'erro') => void;
}) {
  const [escolhas, setEscolhas] = useState<Record<string, string>>({});
  const [justificativa, setJustificativa] = useState('');
  const [salvando, setSalvando] = useState(false);

  const completo = processo.families.every((f) => escolhas[f]);

  async function confirmar() {
    setSalvando(true);
    try {
      const awards = processo.families.map((family) => ({
        family, proposalId: escolhas[family], criteria: [], justification: justificativa.trim() || null,
      }));
      // o vencedor "principal" é o da primeira família; o backend usa a lista de awards
      await escolherVencedor(processo.id, {
        proposalId: awards[0].proposalId, criteria: [], justification: justificativa.trim(), awards,
      });
      aoAvisar('Fornecedores escolhidos por família. O processo seguiu para a aprovação.');
      aoConcluir();
    } catch (e) { aoAvisar(mensagem(e, 'Falha ao registrar a adjudicação.'), 'erro'); }
    finally { setSalvando(false); }
  }

  return (
    <div data-testid="form-adjudicacao">
      <h3 className="mb-2 text-[14px] font-bold">Escolher o fornecedor de cada família</h3>
      <Nota>
        Esta compra tem <strong>{processo.families.length} famílias</strong>. Cada família pode ficar
        com um fornecedor diferente — quem ganhar mais de uma recebe todas na mesma O.C.
      </Nota>
      <div className="mt-3 overflow-x-auto">
        <table>
          <thead><tr><th>Família</th><th>Fornecedor</th></tr></thead>
          <tbody>
            {processo.families.map((f) => {
              const candidatas = propostasDaFamilia(processo, f);
              return (
                <tr key={f}>
                  <td><strong>{f}</strong></td>
                  <td className="min-w-[280px]">
                    {candidatas.length === 0
                      ? <span className="sub">Nenhuma proposta cotou esta família.</span>
                      : <select aria-label={`Fornecedor da família ${f}`} value={escolhas[f] ?? ''}
                          onChange={(e) => setEscolhas((x) => ({ ...x, [f]: e.target.value }))}>
                          <option value="">Escolha o fornecedor…</option>
                          {candidatas.map((p) => (
                            <option key={p.id} value={p.id}>
                              {p.supplierName} — {moeda(p.totalValue)} (v{p.version})
                            </option>
                          ))}
                        </select>}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>

      <Campo id="adj-justificativa" rotulo="Justificativa da escolha" dica="(obrigatória)" className="mt-3">
        <input id="adj-justificativa" value={justificativa}
          onChange={(e) => setJustificativa(e.target.value)} />
      </Campo>

      <button type="button" className="botao mt-3" disabled={!completo || !justificativa.trim() || salvando}
        onClick={confirmar}>
        {salvando ? 'Registrando…' : 'Confirmar adjudicação e enviar à aprovação'}
      </button>
      {!completo && <Nota>Escolha um fornecedor para cada família antes de confirmar.</Nota>}
    </div>
  );
}

/** Registro da O.C. fechada no ERP. Compra dividida: uma O.C. por fornecedor. */
export function FormRegistroOc({ processo, aoConcluir, aoAvisar }: {
  processo: Processo;
  aoConcluir: () => void;
  aoAvisar: (t: string, tipo?: 'ok' | 'erro') => void;
}) {
  const pendentes = processo.pendingPoSuppliers;
  const [fornecedor, setFornecedor] = useState(pendentes[0]?.supplierId ?? '');
  const [numero, setNumero] = useState('');
  const [emissao, setEmissao] = useState('');
  const [observacao, setObservacao] = useState('');
  const [salvando, setSalvando] = useState(false);

  async function confirmar() {
    setSalvando(true);
    try {
      await registrarOc(processo.id, {
        erpNumber: numero.trim(), issuedOn: emissao || null, notes: observacao || null,
        supplierId: pendentes.length > 1 ? fornecedor : null,
      });
      aoAvisar('O.C. registrada. O processo segue para faturamento e entrega.');
      aoConcluir();
    } catch (e) { aoAvisar(mensagem(e, 'Falha ao registrar a O.C.'), 'erro'); }
    finally { setSalvando(false); }
  }

  const unico = pendentes.length === 1 ? pendentes[0] : null;

  return (
    <div data-testid="form-oc">
      <h3 className="mb-2 text-[14px] font-bold">Registrar a O.C. fechada no ERP</h3>
      {pendentes.length > 1 && (
        <>
          <Nota>
            Compra dividida: <strong>uma O.C. por fornecedor</strong>. Registre uma de cada vez — o
            processo só encerra quando todas estiverem lançadas.
          </Nota>
          <Campo id="oc-fornecedor" rotulo="Fornecedor desta O.C." className="mt-2">
            <select id="oc-fornecedor" value={fornecedor} onChange={(e) => setFornecedor(e.target.value)}>
              {pendentes.map((f) => (
                <option key={f.supplierId} value={f.supplierId}>
                  {f.supplierName} — {f.families.join(', ')} · {moeda(f.totalValue)}
                </option>
              ))}
            </select>
          </Campo>
        </>
      )}
      {unico && (
        <Nota>
          A O.C. é fechada no ERP, que conversa com o financeiro. Aqui você registra o número dela —
          fornecedor <strong>{unico.supplierName}</strong>, total {moeda(unico.totalValue)}.
        </Nota>
      )}

      <Grade2 className="mt-3">
        <Campo id="oc-numero" rotulo="Número da O.C. (ERP)">
          <input id="oc-numero" placeholder="ex.: 663" value={numero} onChange={(e) => setNumero(e.target.value)} />
        </Campo>
        <Campo id="oc-data" rotulo="Data da O.C.">
          <input id="oc-data" type="date" value={emissao} onChange={(e) => setEmissao(e.target.value)} />
        </Campo>
      </Grade2>
      <Campo id="oc-obs" rotulo="Observação" dica="(opcional)" className="mt-3">
        <input id="oc-obs" placeholder="ex.: entrega parcelada combinada com o fornecedor"
          value={observacao} onChange={(e) => setObservacao(e.target.value)} />
      </Campo>

      <button type="button" className="botao mt-3" disabled={!numero.trim() || salvando} onClick={confirmar}>
        {salvando ? 'Registrando…' : 'Registrar O.C. e seguir para faturamento'}
      </button>
      <Nota>O anexo da O.C. continua sendo lançado na tela de Pedidos de Compra.</Nota>
    </div>
  );
}
