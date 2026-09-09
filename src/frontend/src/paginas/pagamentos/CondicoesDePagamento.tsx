import { useState, type FormEvent } from 'react';
import {
  atualizarCondicaoDePagamento, criarCondicaoDePagamento, listarCondicoesDePagamento,
  type CondicaoDePagamento,
} from '@/api/pagamentos';
import { BadgeAtivo, Campo, Grade2, Nota } from '@/componentes/formulario';
import { Carregando, Erro, Painel, Vazio } from '@/componentes/basicos';
import { useToast } from '@/componentes/Toast';
import { podeComprar, temModulo } from '@/dominio/papeis';
import { useUsuario } from '@/sessao/SessaoProvider';
import { rolarPara } from '@/util/rolar';
import { useCarregar } from '@/util/useCarregar';

const VAZIO = { nome: '', parcelas: '1', primeiroVencimento: '', padrao: false };
type Formulario = typeof VAZIO;

/** Resumo legível da condição: "3 parcelas · 1ª em 30 dias". */
export function resumoCondicao(c: CondicaoDePagamento): string {
  const parcelas = c.installments === 1 ? '1 parcela' : `${c.installments} parcelas`;
  if (c.firstDueDays == null) return parcelas;
  return `${parcelas} · 1ª ${c.firstDueDays === 0 ? 'à vista' : `em ${c.firstDueDays} dias`}`;
}

/**
 * Cadastro das condições de pagamento — o "quando" (à vista, 30/60/90, 14/28).
 * O prazo da primeira parcela é o que preenche sozinho o campo "prazo p/ pagamento"
 * da proposta: é o mesmo número toda vez para a mesma condição, e hoje o comprador
 * o redigita a cada cotação.
 */
export function CondicoesDePagamento() {
  const usuario = useUsuario();
  const mantem = podeComprar(usuario) && temModulo(usuario, 'COMPRAS');
  const { avisar } = useToast();
  const [editando, setEditando] = useState<CondicaoDePagamento | null>(null);
  const [form, setForm] = useState<Formulario>(VAZIO);
  const [salvando, setSalvando] = useState(false);

  const { dados, erro, carregando, recarregar } = useCarregar(
    (signal) => listarCondicoesDePagamento(mantem, signal),
    [mantem],
  );

  const campo = (k: 'nome' | 'parcelas' | 'primeiroVencimento') => ({
    value: form[k],
    onChange: (ev: { target: { value: string } }) => setForm((f) => ({ ...f, [k]: ev.target.value })),
  });

  function editar(c: CondicaoDePagamento) {
    setEditando(c);
    setForm({
      nome: c.name, parcelas: String(c.installments),
      primeiroVencimento: c.firstDueDays?.toString() ?? '', padrao: c.isDefault,
    });
    rolarPara('form-condicao-pagamento');
  }

  function cancelar() {
    setEditando(null);
    setForm(VAZIO);
  }

  async function alternarSituacao(c: CondicaoDePagamento) {
    try {
      await atualizarCondicaoDePagamento(c.id, { active: !c.active });
      avisar(c.active ? 'Condição inativada.' : 'Condição reativada.');
      recarregar();
    } catch (e) { avisar(e instanceof Error ? e.message : 'Falha ao alterar a condição.', 'erro'); }
  }

  async function enviar(ev: FormEvent) {
    ev.preventDefault();
    setSalvando(true);
    const dadosDoForm = {
      name: form.nome,
      installments: parseInt(form.parcelas, 10) || 1,
      firstDueDays: form.primeiroVencimento === '' ? null : parseInt(form.primeiroVencimento, 10),
      isDefault: form.padrao,
    };
    try {
      if (editando) await atualizarCondicaoDePagamento(editando.id, dadosDoForm);
      else await criarCondicaoDePagamento(dadosDoForm);
      avisar(editando ? 'Condição atualizada.' : 'Condição cadastrada.');
      cancelar();
      recarregar();
    } catch (e) { avisar(e instanceof Error ? e.message : 'Falha ao salvar a condição.', 'erro'); }
    finally { setSalvando(false); }
  }

  return (
    <>
      <Painel titulo="Condições de Pagamento">
        <Nota>
          A condição é <b>quando se paga</b>. O nome carrega o cronograma como o comprador o diz
          (“Parcelado 30/60/90”), e o prazo da primeira parcela preenche sozinho o
          “prazo p/ pagamento” no registro da proposta.
        </Nota>
        {erro && <Erro>{erro}</Erro>}
        {carregando && !dados && <Carregando />}
        {dados && !dados.length && <Vazio>Nenhuma condição de pagamento cadastrada ainda.</Vazio>}
        {dados && dados.length > 0 && (
          <div className="mt-3 overflow-x-auto">
            <table data-testid="tabela-condicoes-pagamento" className="min-w-[720px]">
              <thead>
                <tr>
                  <th>Condição</th><th>Nº de parcelas</th><th>Prazo da 1ª</th>
                  <th>Situação</th>{mantem && <th>Ações</th>}
                </tr>
              </thead>
              <tbody>
                {dados.map((c) => (
                  <tr key={c.id} data-condicao={c.name}>
                    <td className="font-semibold">
                      {c.name}
                      {c.isDefault && <span className="ml-2 text-[11px] font-semibold text-texto-suave">(sugerida)</span>}
                    </td>
                    <td>{c.installments}</td>
                    <td className="sub">{resumoCondicao(c)}</td>
                    <td><BadgeAtivo ativo={c.active} rotuloAtivo="ATIVA" rotuloInativo="INATIVA" /></td>
                    {mantem && (
                      <td className="whitespace-nowrap">
                        <div className="flex gap-1.5">
                          <button type="button" className="botao-secundario !py-1.5" onClick={() => editar(c)}>Editar</button>
                          <button type="button" className={(c.active ? 'botao-perigo' : 'botao-secundario') + ' !py-1.5'}
                            onClick={() => alternarSituacao(c)}>{c.active ? 'Inativar' : 'Reativar'}</button>
                        </div>
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Painel>

      {mantem && (
        <Painel id="form-condicao-pagamento" titulo={editando ? `Editar condição — ${editando.name}` : 'Nova condição de pagamento'}>
          <form onSubmit={enviar}>
            <Campo id="cp-nome" rotulo="Nome">
              <input id="cp-nome" required minLength={2} placeholder="ex.: Parcelado 30/60/90" {...campo('nome')} />
            </Campo>
            <Grade2 className="mt-3">
              <Campo id="cp-parcelas" rotulo="Nº de parcelas">
                <input id="cp-parcelas" type="number" min={1} max={36} required {...campo('parcelas')} />
              </Campo>
              <Campo id="cp-primeiro" rotulo="Prazo da 1ª parcela (dias)"
                dica="0 é à vista; em branco quando a condição não define">
                <input id="cp-primeiro" type="number" min={0} max={365} placeholder="ex.: 30" {...campo('primeiroVencimento')} />
              </Campo>
            </Grade2>
            <label className="mt-3 flex items-center gap-2 text-[13px]">
              <input type="checkbox" checked={form.padrao}
                onChange={(e) => setForm((f) => ({ ...f, padrao: e.target.checked }))} />
              Sugerir esta condição por padrão no registro da proposta
            </label>

            <div className="mt-4 flex flex-wrap gap-2">
              <button type="submit" className="botao" disabled={salvando}>
                {salvando ? 'Salvando…' : editando ? 'Salvar alterações' : 'Cadastrar condição'}
              </button>
              {editando && <button type="button" className="botao-secundario" onClick={cancelar}>Cancelar edição</button>}
            </div>
          </form>
          <Nota>
            Só uma condição é a sugerida: marcar esta desmarca a anterior. E a condição inativada
            deixa de sugerir sozinha — a tela não pode oferecer o que ela mesma escondeu da lista.
          </Nota>
        </Painel>
      )}
    </>
  );
}
