import { useState } from 'react';
import {
  anexarNaProposta, propostaVigenteDe, registrarProposta,
  type Processo, type PropostaManual,
} from '@/api/cotacoes';
import { Campo, Grade2, Nota } from '@/componentes/formulario';
import { quantidade } from '@/util/formato';

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
  prazoEntrega: '', condicaoPagamento: '', prazoPagamento: '', frete: '',
  impostos: '', outros: '', desconto: '', validade: '', moeda: 'BRL', observacao: '',
};

/** Proposta que chegou por e-mail: o comprador lança o que o fornecedor respondeu. */
export function PainelPropostaManual({ processo, aoRegistrar, aoAvisar }: {
  processo: Processo;
  aoRegistrar: () => void;
  aoAvisar: (texto: string, tipo?: 'ok' | 'erro') => void;
}) {
  const [fornecedor, setFornecedor] = useState(processo.suppliers[0]?.supplierId ?? '');
  const [campos, setCampos] = useState<Record<string, string>>(VAZIO);
  const [precos, setPrecos] = useState<Record<string, string>>({});
  const [arquivo, setArquivo] = useState<File | null>(null);
  const [salvando, setSalvando] = useState(false);

  const campo = (k: keyof typeof VAZIO) => ({
    value: campos[k] ?? '',
    onChange: (e: { target: { value: string } }) => setCampos((c) => ({ ...c, [k]: e.target.value })),
  });

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
            <input id="ip-pagamento" placeholder="ex.: 30/60 dias" {...campo('condicaoPagamento')} />
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
          <Campo id="ip-prazo-pag" rotulo="Prazo p/ pagamento (dias)">
            <input id="ip-prazo-pag" type="number" min="0" placeholder="ex.: 28" {...campo('prazoPagamento')} />
          </Campo>
          <Campo id="ip-validade" rotulo="Validade da proposta">
            <input id="ip-validade" type="date" {...campo('validade')} />
          </Campo>
        </Grade2>
        <Campo id="ip-moeda" rotulo="Moeda">
          <select id="ip-moeda" {...campo('moeda')}>
            <option value="BRL">Real (BRL)</option>
            <option value="USD">Dólar (USD)</option>
            <option value="EUR">Euro (EUR)</option>
          </select>
        </Campo>
      </Grade2>

      <Campo id="ip-obs" rotulo="Observação da proposta" className="mt-3">
        <input id="ip-obs" placeholder="ex.: condição especial, prazo de embalagem…" {...campo('observacao')} />
      </Campo>

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
