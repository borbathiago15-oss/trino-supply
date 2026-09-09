import { useEffect, useState } from 'react';
import {
  anexarNaProposta, precosDeContrato, propostaVigenteDe, registrarProposta,
  type CoberturaDoContrato, type Processo, type PropostaManual,
} from '@/api/cotacoes';
import {
  listarCondicoesDePagamento, listarFormasDePagamento,
  type CondicaoDePagamento, type FormaDePagamento,
} from '@/api/pagamentos';
import { Campo, Grade2, Nota } from '@/componentes/formulario';
import { data, quantidade } from '@/util/formato';

const numero = (v: string) => {
  const n = Number(v.replace(',', '.'));
  return v.trim() && !Number.isNaN(n) ? n : null;
};

const inteiro = (v: string) => {
  const n = Number.parseInt(v, 10);
  return Number.isNaN(n) ? null : n;
};

/**
 * Monta a proposta a partir do formulário. Item sem preço fica de fora — o
 * fornecedor pode não ter cotado tudo, e o backend aceita proposta parcial.
 */
export function montarProposta(
  supplierId: string,
  campos: Record<string, string>,
  precos: Record<string, string>,
): { proposta: PropostaManual | null; erro: string | null } {
  if (!supplierId) return { proposta: null, erro: 'Escolha o fornecedor da proposta.' };
  const items = Object.entries(precos)
    .map(([quotationItemId, v]) => ({ quotationItemId, unitPrice: numero(v) }))
    .filter((i): i is { quotationItemId: string; unitPrice: number } => i.unitPrice != null && i.unitPrice > 0);
  if (!items.length) return { proposta: null, erro: 'Informe o preço de ao menos um item.' };
  return {
    erro: null,
    proposta: {
      supplierId,
      deliveryDays: inteiro(campos.prazoEntrega ?? ''),
      paymentTerms: campos.condicaoPagamento || null,
      paymentMethodName: campos.formaPagamento || null,
      paymentDays: inteiro(campos.prazoPagamento ?? ''),
      freightValue: numero(campos.frete ?? ''),
      taxValue: numero(campos.impostos ?? ''),
      otherCosts: numero(campos.outros ?? ''),
      discountValue: numero(campos.desconto ?? ''),
      validUntil: campos.validade || null,
      currency: campos.moeda || 'BRL',
      notes: campos.observacao || null,
      items,
    },
  };
}

const VAZIO = {
  prazoEntrega: '', condicaoPagamento: '', formaPagamento: '', prazoPagamento: '', frete: '',
  impostos: '', outros: '', desconto: '', validade: '', moeda: 'BRL', observacao: '',
};

/**
 * A condição escolhida traz junto o prazo da primeira parcela: é o mesmo número toda
 * vez para a mesma condição, e era o que o comprador redigitava a cada cotação. Fica
 * editável — a condição diz o padrão, o fornecedor pode ter combinado outro.
 */
export function daCondicao(c: CondicaoDePagamento): { condicaoPagamento: string; prazoPagamento?: string } {
  return c.firstDueDays == null
    ? { condicaoPagamento: c.name }
    : { condicaoPagamento: c.name, prazoPagamento: String(c.firstDueDays) };
}

/**
 * O que o contrato de parceria preenche no formulário. Devolve só o que ele de fato
 * define, e <b>nunca por cima do que já foi digitado</b>: o comprador pode ter fechado
 * um preço melhor que o do contrato, e apagá-lo seria o formulário desfazer a negociação.
 *
 * Quando o contrato não está vigente não vem nada — preço vencido entrando calado na
 * proposta é pior do que campo vazio, porque se fecharia por um valor que não vale mais.
 */
export function doContrato(
  cobertura: CoberturaDoContrato | null,
  precosAtuais: Record<string, string>,
  camposAtuais: Record<string, string>,
): { precos: Record<string, string>; campos: Record<string, string> } {
  if (!cobertura?.current || cobertura.items.length === 0)
    return { precos: precosAtuais, campos: camposAtuais };

  const precos = { ...precosAtuais };
  for (const i of cobertura.items)
    if (!precos[i.quotationItemId]) precos[i.quotationItemId] = String(i.unitPrice);

  // prazo e condição do contrato valem para a proposta inteira: pegamos os do primeiro
  // item que os define, porque um contrato negocia isso uma vez, não linha a linha
  const campos = { ...camposAtuais };
  const comEntrega = cobertura.items.find((i) => i.deliveryDays != null);
  if (comEntrega && !campos.prazoEntrega) campos.prazoEntrega = String(comEntrega.deliveryDays);
  const comCondicao = cobertura.items.find((i) => i.paymentTerms);
  if (comCondicao && !campos.condicaoPagamento) campos.condicaoPagamento = comCondicao.paymentTerms!;
  const comPrazoPag = cobertura.items.find((i) => i.paymentDays != null);
  if (comPrazoPag && !campos.prazoPagamento) campos.prazoPagamento = String(comPrazoPag.paymentDays);

  return { precos, campos };
}

/** Proposta que chegou por e-mail: o comprador lança o que o fornecedor respondeu. */
export function PainelPropostaManual({ processo, aoRegistrar, aoAvisar }: {
  processo: Processo;
  aoRegistrar: () => void;
  aoAvisar: (texto: string, tipo?: 'ok' | 'erro') => void;
}) {
  const [fornecedor, setFornecedor] = useState(processo.suppliers[0]?.supplierId ?? '');
  const [campos, setCampos] = useState<Record<string, string>>(VAZIO);
  const [formas, setFormas] = useState<FormaDePagamento[]>([]);
  const [condicoes, setCondicoes] = useState<CondicaoDePagamento[]>([]);
  const [contrato, setContrato] = useState<CoberturaDoContrato | null>(null);
  const [precos, setPrecos] = useState<Record<string, string>>({});
  const [arquivo, setArquivo] = useState<File | null>(null);
  const [salvando, setSalvando] = useState(false);

  const campo = (k: keyof typeof VAZIO) => ({
    value: campos[k] ?? '',
    onChange: (e: { target: { value: string } }) => setCampos((c) => ({ ...c, [k]: e.target.value })),
  });

  // Os cadastros alimentam as duas listas. Falhar aqui não pode travar o registro da
  // proposta: sem lista, os campos continuam sendo texto livre, que é como era antes.
  useEffect(() => {
    const controle = new AbortController();
    listarFormasDePagamento(false, controle.signal).then(setFormas).catch(() => {});
    listarCondicoesDePagamento(false, controle.signal).then((lista) => {
      setCondicoes(lista);
      const sugerida = lista.find((c) => c.isDefault);
      if (sugerida) setCampos((c) => (c.condicaoPagamento ? c : { ...c, ...daCondicao(sugerida) }));
    }).catch(() => {});
    return () => controle.abort();
  }, []);

  // Preço de contrato de parceria: escolher o fornecedor traz o que já foi combinado
  // com ele, em vez de o comprador redigitar. Falhar aqui não trava nada — o formulário
  // volta a ser o de antes, com os campos em branco.
  useEffect(() => {
    if (!fornecedor) { setContrato(null); return; }
    const controle = new AbortController();
    precosDeContrato(processo.id, fornecedor, controle.signal)
      .then((c) => {
        setContrato(c);
        setPrecos((p) => doContrato(c, p, campos).precos);
        setCampos((cs) => doContrato(c, precos, cs).campos);
      })
      .catch(() => setContrato(null));
    return () => controle.abort();
    // de propósito só o fornecedor: recarregar a cada tecla digitada refaria a chamada
    // e ainda tentaria preencher por cima do que está sendo escrito
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [processo.id, fornecedor]);

  function escolherCondicao(nome: string) {
    const escolhida = condicoes.find((c) => c.name === nome);
    setCampos((c) => (escolhida ? { ...c, ...daCondicao(escolhida) } : { ...c, condicaoPagamento: nome }));
  }

  async function salvar() {
    const { proposta, erro } = montarProposta(fornecedor, campos, precos);
    if (erro || !proposta) { aoAvisar(erro ?? 'Proposta incompleta.', 'erro'); return; }
    setSalvando(true);
    try {
      const atualizado = await registrarProposta(processo.id, proposta);
      // o anexo é um passo à parte: falhar nele não desfaz a proposta registrada
      if (arquivo) {
        const registrada = propostaVigenteDe(atualizado, fornecedor);
        try {
          if (!registrada) throw new Error('proposta não localizada no processo');
          await anexarNaProposta(processo.id, registrada.id, arquivo);
        } catch (e) {
          aoAvisar(`Proposta registrada, mas o anexo falhou: ${e instanceof Error ? e.message : 'erro no upload'}.`, 'erro');
          setCampos(VAZIO);
          setPrecos({});
          setArquivo(null);
          aoRegistrar();
          return;
        }
      }
      aoAvisar('Proposta registrada.');
      setCampos(VAZIO);
      setPrecos({});
      setArquivo(null);
      aoRegistrar();
    } catch (e) { aoAvisar(e instanceof Error ? e.message : 'Falha ao registrar a proposta.', 'erro'); }
    finally { setSalvando(false); }
  }

  return (
    <div className="mt-4 border-t border-borda pt-4" data-testid="form-proposta">
      <h3 className="mb-2 text-[14px] font-bold">Registrar proposta recebida fora do portal</h3>
      <Grade2>
        <Campo id="ip-fornecedor" rotulo="Fornecedor">
          <select id="ip-fornecedor" value={fornecedor} onChange={(e) => setFornecedor(e.target.value)}>
            {processo.suppliers.map((s) => (
              <option key={s.supplierId} value={s.supplierId}>{s.supplierName}</option>
            ))}
          </select>
        </Campo>
        <Grade2>
          <Campo id="ip-entrega" rotulo="Prazo entrega (dias)">
            <input id="ip-entrega" type="number" min="0" {...campo('prazoEntrega')} />
          </Campo>
          <Campo id="ip-pagamento" rotulo="Cond. pagamento">
            {condicoes.length ? (
              <select id="ip-pagamento" value={campos.condicaoPagamento ?? ''}
                onChange={(e) => escolherCondicao(e.target.value)}>
                <option value="">—</option>
                {condicoes.map((c) => <option key={c.id} value={c.name}>{c.name}</option>)}
              </select>
            ) : (
              // sem cadastro (ou com a lista fora do ar) o campo volta a ser o que era
              <input id="ip-pagamento" placeholder="ex.: 30/60 dias" {...campo('condicaoPagamento')} />
            )}
          </Campo>
        </Grade2>
      </Grade2>

      <Grade2 className="mt-3">
        <Grade2>
          <Campo id="ip-frete" rotulo="Frete (R$)">
            <input id="ip-frete" type="number" min="0" step="0.01" {...campo('frete')} />
          </Campo>
          <Campo id="ip-desconto" rotulo="Desconto (R$)">
            <input id="ip-desconto" type="number" min="0" step="0.01" {...campo('desconto')} />
          </Campo>
        </Grade2>
        <Grade2>
          <Campo id="ip-impostos" rotulo="Impostos (R$)">
            <input id="ip-impostos" type="number" min="0" step="0.01" placeholder="destacados na proposta" {...campo('impostos')} />
          </Campo>
          <Campo id="ip-outros" rotulo="Outros custos (R$)">
            <input id="ip-outros" type="number" min="0" step="0.01" placeholder="embalagem, taxas…" {...campo('outros')} />
          </Campo>
        </Grade2>
      </Grade2>

      <Grade2 className="mt-3">
        <Grade2>
          <Campo id="ip-forma-pag" rotulo="Forma de pagamento">
            {formas.length ? (
              <select id="ip-forma-pag" {...campo('formaPagamento')}>
                <option value="">—</option>
                {formas.map((f) => <option key={f.id} value={f.name}>{f.name}</option>)}
              </select>
            ) : (
              <input id="ip-forma-pag" placeholder="ex.: Boleto Bancário" {...campo('formaPagamento')} />
            )}
          </Campo>
          <Campo id="ip-prazo-pag" rotulo="Prazo p/ pagamento (dias)"
            dica="vem da condição escolhida; ajuste se o fornecedor combinou outro">
            <input id="ip-prazo-pag" type="number" min="0" placeholder="ex.: 28" {...campo('prazoPagamento')} />
          </Campo>
        </Grade2>
        <Grade2>
          <Campo id="ip-validade" rotulo="Validade da proposta">
            <input id="ip-validade" type="date" {...campo('validade')} />
          </Campo>
          <Campo id="ip-moeda" rotulo="Moeda">
            <select id="ip-moeda" {...campo('moeda')}>
              <option value="BRL">Real (BRL)</option>
              <option value="USD">Dólar (USD)</option>
              <option value="EUR">Euro (EUR)</option>
            </select>
          </Campo>
        </Grade2>
      </Grade2>

      <Campo id="ip-obs" rotulo="Observação da proposta" className="mt-3">
        <input id="ip-obs" placeholder="ex.: condição especial, prazo de embalagem…" {...campo('observacao')} />
      </Campo>

      {contrato?.current && contrato.items.length > 0 && (
        // o comprador precisa saber de onde veio o número: preço que aparece sozinho,
        // sem dizer por quê, é mais difícil de conferir do que campo em branco
        <p className="mt-3 rounded-lg bg-ok-fundo px-3 py-2 text-[12.5px] text-ok" data-testid="aviso-contrato">
          Preços preenchidos pelo <strong>contrato de parceria
          {contrato.contractNumber ? ` ${contrato.contractNumber}` : ''}</strong>
          {contrato.validUntil ? ` (vigente até ${data(contrato.validUntil)})` : ''} —
          {' '}{contrato.items.length} {contrato.items.length === 1 ? 'item coberto' : 'itens cobertos'}.
          Pode alterar: o contrato é o ponto de partida, não uma trava.
        </p>
      )}

      <div className="mt-3">
        <p className="mb-1 text-[12.5px] font-semibold text-texto-suave">Preços unitários</p>
        <div className="overflow-x-auto">
          <table>
            <tbody>
              {processo.items.map((i) => (
                <tr key={i.id}>
                  <td>
                    {i.description} <span className="sub">({quantidade(i.quantity)} {i.unitOfMeasure})</span>
                  </td>
                  <td className="w-36">
                    <input type="number" min="0" step="0.01" placeholder="R$ unit."
                      aria-label={`Preço unitário de ${i.description}`}
                      value={precos[i.id] ?? ''}
                      onChange={(e) => setPrecos((p) => ({ ...p, [i.id]: e.target.value }))} />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      <Campo id="ip-arquivo" rotulo="Cotação recebida" dica="PDF, planilha ou imagem, até 10 MB" className="mt-3">
        <input id="ip-arquivo" type="file" accept=".pdf,.xlsx,.xls,.csv,.docx,.png,.jpg,.jpeg"
          onChange={(e) => setArquivo(e.target.files?.[0] ?? null)} />
      </Campo>

      <div className="mt-3">
        <button type="button" className="botao" disabled={salvando} onClick={salvar}>
          {salvando ? 'Registrando…' : 'Registrar proposta'}
        </button>
      </div>
      <Nota>
        Item sem preço fica de fora: o fornecedor pode não ter cotado tudo. O orçamento que ele
        enviou por e-mail fica arquivado no processo, disponível na comparação e na aprovação.
      </Nota>
    </div>
  );
}
