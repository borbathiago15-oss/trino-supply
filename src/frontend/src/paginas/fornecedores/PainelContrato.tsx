import { useEffect, useState } from 'react';
import { listarProdutos, type ProdutoResumo } from '@/api/catalogo';
import { salvarContrato, type Fornecedor, type ItemContrato } from '@/api/fornecedores';
import { Painel } from '@/componentes/basicos';
import { Campo, Grade2, Nota } from '@/componentes/formulario';
import { useToast } from '@/componentes/Toast';
import { moeda } from '@/util/formato';
import { rolarPara } from '@/util/rolar';
import { useCarregar } from '@/util/useCarregar';

interface LinhaForm {
  chave: string;
  catalogItemId: string;
  /** Produto fora do catálogo, vindo de um contrato salvo antes. */
  descricaoLivre: string | null;
  preco: string;
  condicao: string;
  diasPagamento: string;
  diasEntrega: string;
  observacao: string;
}

let sequencia = 0;
const novaLinha = (): LinhaForm => ({
  chave: 'l' + ++sequencia, catalogItemId: '', descricaoLivre: null,
  preco: '', condicao: '', diasPagamento: '', diasEntrega: '', observacao: '',
});

const daLinha = (i: ItemContrato): LinhaForm => ({
  chave: 'l' + ++sequencia,
  catalogItemId: i.catalogItemId ?? '',
  descricaoLivre: i.catalogItemId ? null : i.description,
  preco: i.unitPrice != null ? String(i.unitPrice) : '',
  condicao: i.paymentTerms ?? '',
  diasPagamento: i.paymentDays?.toString() ?? '',
  diasEntrega: i.deliveryDays?.toString() ?? '',
  observacao: i.notes ?? '',
});

/** Linhas do formulário viram itens da API; linha sem produto e sem descrição é descartada. */
export function itensDoFormulario(linhas: LinhaForm[], catalogo: ProdutoResumo[]): ItemContrato[] {
  const inteiro = (v: string) => (v === '' ? null : parseInt(v, 10));
  return linhas
    .map((l) => {
      const produto = catalogo.find((c) => c.id === l.catalogItemId);
      return {
        catalogItemId: produto?.id ?? null,
        description: produto?.description ?? l.descricaoLivre,
        catalogCode: produto?.code ?? null,
        unitOfMeasure: produto?.unitOfMeasure ?? null,
        unitPrice: l.preco ? parseFloat(l.preco) : 0,
        paymentTerms: l.condicao || null,
        paymentDays: inteiro(l.diasPagamento),
        deliveryDays: inteiro(l.diasEntrega),
        notes: l.observacao || null,
      };
    })
    .filter((i) => i.catalogItemId || i.description);
}

export function PainelContrato({ fornecedor, aoSalvar, aoFechar }:
  { fornecedor: Fornecedor; aoSalvar: () => void; aoFechar: () => void }) {
  const { avisar } = useToast();
  const contrato = fornecedor.contract;
  const { dados: catalogo } = useCarregar((signal) => listarProdutos(signal).catch(() => [] as ProdutoResumo[]), []);
  const [numero, setNumero] = useState(contrato.number ?? '');
  const [teto, setTeto] = useState(contrato.valueLimit != null ? String(contrato.valueLimit) : '');
  const [inicio, setInicio] = useState(contrato.validFrom ?? '');
  const [fim, setFim] = useState(contrato.validUntil ?? '');
  const [observacao, setObservacao] = useState(contrato.notes ?? '');
  const [linhas, setLinhas] = useState<LinhaForm[]>(
    contrato.items.length ? contrato.items.map(daLinha) : [novaLinha()],
  );
  const [salvando, setSalvando] = useState(false);

  useEffect(() => {
    rolarPara('contrato');
  }, []);

  const editar = (chave: string, campo: keyof LinhaForm, valor: string) =>
    setLinhas((ls) => ls.map((l) => (l.chave === chave ? { ...l, [campo]: valor } : l)));

  async function salvar() {
    const items = itensDoFormulario(linhas, catalogo ?? []);
    setSalvando(true);
    try {
      await salvarContrato(fornecedor.id, {
        number: numero || null,
        valueLimit: teto ? parseFloat(teto) : null,
        validFrom: inicio || null,
        validUntil: fim || null,
        notes: observacao || null,
        items,
      });
      avisar(items.length ? 'Contrato salvo.' : 'Contrato encerrado — o fornecedor volta a ser cotado normalmente.');
      aoSalvar();
      aoFechar();
    } catch (e) { avisar(e instanceof Error ? e.message : 'Falha ao salvar o contrato.', 'erro'); }
    finally { setSalvando(false); }
  }

  return (
    <Painel id="contrato" titulo={`Contrato de parceria — ${fornecedor.legalName}`}
      acoes={<button type="button" className="botao-secundario" onClick={aoFechar}>Fechar</button>}>
      <Nota>
        Produtos com <strong>preço, prazo de pagamento e prazo de entrega fixos</strong> enquanto o contrato valer.
        O comprador preenche a proposta desse fornecedor com um clique, sem renegociar.
      </Nota>
      {contrato.valueLimit != null && (
        <p className="sub mt-2">
          Teto {moeda(contrato.valueLimit)} · consumido {moeda(contrato.consumed ?? 0)} · saldo {moeda(contrato.balance ?? 0)}
        </p>
      )}

      <Grade2 className="mt-3">
        <Grade2>
          <Campo id="ct-numero" rotulo="Número do contrato">
            <input id="ct-numero" placeholder="ex.: CT-2026-014" value={numero} onChange={(e) => setNumero(e.target.value)} />
          </Campo>
          <Campo id="ct-teto" rotulo="Teto financeiro (R$)" dica="(as O.C.s abatem o saldo)">
            <input id="ct-teto" type="number" min={0} step="0.01" placeholder="opcional" value={teto} onChange={(e) => setTeto(e.target.value)} />
          </Campo>
        </Grade2>
        <Grade2>
          <Campo id="ct-inicio" rotulo="Início da vigência">
            <input id="ct-inicio" type="date" value={inicio} onChange={(e) => setInicio(e.target.value)} />
          </Campo>
          <Campo id="ct-fim" rotulo="Fim da vigência">
            <input id="ct-fim" type="date" value={fim} onChange={(e) => setFim(e.target.value)} />
          </Campo>
        </Grade2>
      </Grade2>
      <Campo id="ct-observacao" rotulo="Observação do contrato" className="mt-3">
        <input id="ct-observacao" placeholder="opcional — condições gerais combinadas" value={observacao} onChange={(e) => setObservacao(e.target.value)} />
      </Campo>

      <p className="mb-2 mt-5 text-[12.5px] font-semibold text-texto-suave">Produtos contratados</p>
      <div className="flex flex-col gap-3">
        {linhas.map((l) => (
          <div key={l.chave} className="rounded-lg border border-borda p-3" data-linha-contrato>
            <Grade2>
              <Campo rotulo="Produto">
                {l.descricaoLivre && !l.catalogItemId ? (
                  <input value={l.descricaoLivre} readOnly title="Produto fora do catálogo, mantido do contrato anterior" />
                ) : (
                  <select aria-label="Produto do contrato" value={l.catalogItemId}
                    onChange={(e) => editar(l.chave, 'catalogItemId', e.target.value)}>
                    <option value="">Selecione o produto…</option>
                    {(catalogo ?? []).map((c) => (
                      <option key={c.id} value={c.id}>[{c.code}] {c.description}</option>
                    ))}
                  </select>
                )}
              </Campo>
              <Grade2>
                <Campo rotulo="Preço fixo (R$)">
                  <input type="number" min={0} step="0.01" aria-label="Preço fixo" value={l.preco}
                    onChange={(e) => editar(l.chave, 'preco', e.target.value)} />
                </Campo>
                <Campo rotulo="Entrega (dias)">
                  <input type="number" min={0} aria-label="Prazo de entrega em dias" value={l.diasEntrega}
                    onChange={(e) => editar(l.chave, 'diasEntrega', e.target.value)} />
                </Campo>
              </Grade2>
            </Grade2>
            <Grade2 className="mt-3">
              <Grade2>
                <Campo rotulo="Condição de pagamento">
                  <input aria-label="Condição de pagamento" placeholder="ex.: 28 DDL" value={l.condicao}
                    onChange={(e) => editar(l.chave, 'condicao', e.target.value)} />
                </Campo>
                <Campo rotulo="Pagamento (dias)">
                  <input type="number" min={0} aria-label="Prazo de pagamento em dias" value={l.diasPagamento}
                    onChange={(e) => editar(l.chave, 'diasPagamento', e.target.value)} />
                </Campo>
              </Grade2>
              <div className="flex items-end gap-2">
                <Campo rotulo="Observação" className="flex-1">
                  <input aria-label="Observação do item" value={l.observacao}
                    onChange={(e) => editar(l.chave, 'observacao', e.target.value)} />
                </Campo>
                <button type="button" className="botao-perigo" title="Remover este produto do contrato"
                  onClick={() => setLinhas((ls) => (ls.length > 1 ? ls.filter((x) => x.chave !== l.chave) : [novaLinha()]))}>
                  Remover
                </button>
              </div>
            </Grade2>
          </div>
        ))}
      </div>

      <div className="mt-3 flex flex-wrap gap-2">
        <button type="button" className="botao-secundario" onClick={() => setLinhas((ls) => [...ls, novaLinha()])}>+ Adicionar produto</button>
        <button type="button" className="botao" disabled={salvando} onClick={salvar}>{salvando ? 'Salvando…' : 'Salvar contrato'}</button>
      </div>
      <Nota>Salvar sem nenhum produto encerra o contrato: o fornecedor volta a ser cotado normalmente.</Nota>
    </Painel>
  );
}
