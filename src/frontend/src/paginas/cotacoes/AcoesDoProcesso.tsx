import { useState } from 'react';
import {
  CRITERIOS, escolherVencedor, impedimentoDaOferta, mapaDeFamilias, MINIMO_MOTIVO_SEM_OC,
  ofertaDaProposta, propostasVigentes, registrarOc,
  type LoteDaFamilia, type Processo, type Proposta,
} from '@/api/cotacoes';
import { anexarOc } from '@/api/pedidos';
import { Nota } from '@/componentes/formulario';
import { Campo, Grade2 } from '@/componentes/formulario';
import { moeda } from '@/util/formato';
import { useCarregar } from '@/util/useCarregar';

const mensagem = (e: unknown, padrao: string) => (e instanceof Error ? e.message : padrao);

/**
 * O mapa por família — é ele que sabe quem pode mesmo levar cada lote: cotou a
 * família inteira (RFQ-ERR-024), está ativo (RFQ-ERR-040) e está homologado
 * (SUP-ERR-030). Sem isso a tela deixava escolher, o comprador escrevia a
 * justificativa e só então tomava o erro do servidor.
 *
 * Falha na leitura não trava nada: sem o mapa a escolha volta a ser a de antes
 * e quem barra é a API — melhor o botão que falha do que a ação escondida.
 */
export function useLotesDoProcesso(processo: Processo | null): LoteDaFamilia[] | null {
  // a tela do processo carrega o mapa uma vez e reparte entre a grade e os impedimentos:
  // duas buscas do mesmo dado divergiriam no primeiro instante entre uma e outra
  return useLotes(processo);
}

function useLotes(processo: Processo | null): LoteDaFamilia[] | null {
  // A chave inclui as propostas vigentes, e não só o id do processo: registrar uma
  // negociação cria uma versão NOVA da proposta, e o mapa preso ao id continuava
  // devolvendo o da versão anterior. A tela então não achava a proposta atual no
  // lote e concluía que o fornecedor "não cotou nenhum item" — logo o fornecedor
  // com quem se acabou de negociar. Mudou a lista de propostas, o mapa é refeito.
  const chave = processo ? propostasVigentes(processo).map((p) => `${p.id}:${p.version}`).join(',') : '';
  const id = processo?.id ?? '';
  const { dados } = useCarregar(async (signal) => {
    if (!id) return null;
    try { return await mapaDeFamilias(id, signal); } catch { return null; }
  }, [id, chave]);
  return dados ?? null;
}

/**
 * Por que esta proposta não pode vencer — no processo de uma família só,
 * escolher o vencedor é escolher quem leva o lote inteiro. Proposta que não
 * aparece no lote não cotou nada dele. Sem o mapa carregado, nada é barrado.
 */
export function impedimentoDaProposta(lote: LoteDaFamilia | null, p: Proposta): string | null {
  if (!lote) return null;
  const oferta = ofertaDaProposta(lote, p.id);
  if (oferta) return impedimentoDaOferta(oferta);
  // A proposta não está no lote. Isso é "não cotou nada" só quando o lote conhece o
  // fornecedor e mesmo assim não tem esta proposta; se o fornecedor nem aparece no
  // mapa, o que se tem é um mapa mais velho que a proposta — e barrar por isso é
  // esconder a ação por causa de um dado atrasado. Nesse caso quem decide é a API.
  const conheceOFornecedor = lote.offers.some((o) => o.supplierId === p.supplierId);
  return conheceOFornecedor ? 'não cotou nenhum item desta compra' : null;
}

/** Fornecedor que pode ou não levar uma família, na forma que o <select> usa. */
export interface Candidata {
  proposalId: string;
  supplierName: string;
  totalValue: number;
  version: number;
  /** Motivo do impedimento, ou `null` quando pode vencer. */
  impedimento: string | null;
}

/**
 * Quem disputa uma família. Com o mapa carregado, o valor é o da fatia daquela
 * família (com o rateio de frete e desconto) e o impedimento vem da mesma régua
 * do servidor. Sem o mapa, sobra o que dá para saber pelo processo.
 */
export function candidatasDaFamilia(
  processo: Processo, familia: string, lote: LoteDaFamilia | null,
): Candidata[] {
  if (lote) return lote.offers.map((o) => ({
    proposalId: o.proposalId, supplierName: o.supplierName, totalValue: o.totalValue,
    version: o.proposalVersion, impedimento: impedimentoDaOferta(o),
  }));
  return propostasDaFamilia(processo, familia).map((p) => ({
    proposalId: p.id, supplierName: p.supplierName, totalValue: p.totalValue,
    version: p.version, impedimento: null,
  }));
}

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
  const lote = useLotes(processo)?.[0] ?? null;
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
            {vigentes.map((p) => {
              const impedida = impedimentoDaProposta(lote, p);
              return (
              <tr key={p.id} data-proposta={p.supplierName}>
                <td className="w-8">
                  <input type="radio" name="vencedor" className="w-auto" value={p.id} disabled={!!impedida}
                    checked={propostaId === p.id} aria-label={`Escolher ${p.supplierName}`}
                    onChange={() => setPropostaId(p.id)} />
                </td>
                <td>
                  {p.supplierName} <span className="sub">v{p.version} · {p.submittedVia}</span>
                  {impedida && <div className="sub text-perigo" data-impedimento>Não pode vencer: {impedida}.</div>}
                </td>
                <td className="whitespace-nowrap"><strong>{moeda(p.totalValue)}</strong></td>
                <td className="sub whitespace-nowrap">
                  {p.deliveryDays ?? '—'} dias · {p.paymentTerms || '—'}
                </td>
              </tr>
              );
            })}
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
        {salvando ? 'Registrando…' : processo.isBudget && !processo.budgetConvertedAt
          ? 'Confirmar escolha e apresentar o orçamento' : 'Confirmar escolha e enviar à aprovação'}
      </button>
      <Nota>A escolha e os critérios ficam registrados na auditoria do processo.</Nota>
    </div>
  );
}

/**
 * Propostas que cotaram a família **inteira** — só elas podem levar o lote
 * (RFQ-ERR-024). É a régua do servidor: cotar parte da família não habilita a
 * disputa. Serve de reserva para quando o mapa por família não pôde ser lido.
 */
export function propostasDaFamilia(processo: Processo, familia: string): Proposta[] {
  const itens = processo.items.filter((i) => i.family === familia).map((i) => i.id);
  return propostasVigentes(processo).filter((p) =>
    itens.every((id) => p.items.some((x) => x.quotationItemId === id && x.unitPrice > 0)));
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
  const lotes = useLotes(processo);
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
              const candidatas = candidatasDaFamilia(processo, f, lotes?.find((l) => l.family === f) ?? null);
              const podem = candidatas.filter((c) => !c.impedimento);
              const impedidas = candidatas.filter((c) => c.impedimento);
              return (
                <tr key={f} data-familia={f}>
                  <td><strong>{f}</strong></td>
                  <td className="min-w-[280px]">
                    {podem.length === 0
                      ? <span className="sub">Nenhuma proposta pode levar esta família.</span>
                      : <select aria-label={`Fornecedor da família ${f}`} value={escolhas[f] ?? ''}
                          onChange={(e) => setEscolhas((x) => ({ ...x, [f]: e.target.value }))}>
                          <option value="">Escolha o fornecedor…</option>
                          {podem.map((c) => (
                            <option key={c.proposalId} value={c.proposalId}>
                              {c.supplierName} — {moeda(c.totalValue)} (v{c.version})
                            </option>
                          ))}
                        </select>}
                    {impedidas.map((c) => (
                      <div key={c.proposalId} className="sub text-perigo" data-impedimento>
                        {c.supplierName} fora: {c.impedimento}.
                      </div>
                    ))}
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
  const [motivoSemOc, setMotivoSemOc] = useState('');
  const [arquivo, setArquivo] = useState<File | null>(null);
  const [salvando, setSalvando] = useState(false);

  // a O.C. é gerada no ERP: sem ela o processo não fecha, a não ser com a observação
  const semOc = numero.trim().length === 0;
  const motivoCurto = motivoSemOc.trim().length < MINIMO_MOTIVO_SEM_OC;
  const pronto = semOc ? !motivoCurto : true;

  async function confirmar() {
    setSalvando(true);
    // guarda o que já existia para achar a O.C. nova depois de registrar
    const antes = new Set(processo.purchaseOrders.map((o) => o.id));
    try {
      const atualizado = await registrarOc(processo.id, {
        erpNumber: numero.trim(), issuedOn: emissao || null, notes: observacao || null,
        supplierId: pendentes.length > 1 ? fornecedor : null,
        noErpReason: semOc ? motivoSemOc.trim() : null,
      });
      // o anexo é um passo à parte: falhar nele não desfaz a O.C. registrada
      if (arquivo) {
        const nova = atualizado.purchaseOrders.find((o) => !antes.has(o.id));
        try {
          if (!nova) throw new Error('pedido não localizado no processo');
          await anexarOc(nova.id, arquivo);
        } catch (e) {
          aoAvisar(`O.C. registrada, mas o anexo falhou: ${mensagem(e, 'erro no upload')}. Anexe pela tela do pedido.`, 'erro');
          aoConcluir();
          return;
        }
      }
      aoAvisar(semOc
        ? 'Fechado sem O.C. do ERP, com a observação na auditoria. Segue para faturamento e entrega.'
        : 'O.C. registrada. O processo segue para faturamento e entrega.');
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
      {semOc && (
        <Campo id="oc-motivo" rotulo="Observação: por que a O.C. não foi gerada no ERP?"
          dica="(obrigatória para fechar sem O.C.)" className="mt-3">
          <input id="oc-motivo" placeholder="ex.: compra emergencial de balcão, sem tempo de abrir O.C."
            value={motivoSemOc} onChange={(e) => setMotivoSemOc(e.target.value)} />
        </Campo>
      )}

      <Campo id="oc-arquivo" rotulo="Anexo da O.C." dica="(opcional) PDF, planilha ou imagem" className="mt-3">
        <input id="oc-arquivo" type="file" accept=".pdf,.png,.jpg,.jpeg,.xlsx,.xls,.csv,.docx"
          onChange={(e) => setArquivo(e.target.files?.[0] ?? null)} />
      </Campo>

      <button type="button" className="botao mt-3" disabled={!pronto || salvando} onClick={confirmar}>
        {salvando
          ? 'Registrando…'
          : semOc ? 'Fechar sem O.C., com a observação' : 'Registrar O.C. e seguir para faturamento'}
      </button>
      {semOc && (
        <Nota>
          A O.C. é gerada no ERP SENIOR, e sem ela o processo <strong>não fecha</strong>. A
          observação acima é a única exceção: com ela o pedido segue com a própria numeração, e
          a justificativa fica registrada na auditoria.
        </Nota>
      )}
      <Nota>O faturamento e a confirmação de entrega ficam na tela do pedido.</Nota>
    </div>
  );
}
