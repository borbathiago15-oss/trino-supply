import { useMemo, useState, type FormEvent } from 'react';
import { atualizarFamilia, criarFamilia, excluirFamilia, listarFamilias, type DadosFamilia, type Familia } from '@/api/familias';
import { contarProdutosPorFamilia } from '@/api/catalogo';
import { BadgeAtivo, Campo, Grade2, Nota } from '@/componentes/formulario';
import { Carregando, Erro, Painel, Vazio } from '@/componentes/basicos';
import { Confirmacao } from '@/componentes/Dialogo';
import { useToast } from '@/componentes/Toast';
import { podeManterCatalogo, temModulo } from '@/dominio/papeis';
import { useUsuario } from '@/sessao/SessaoProvider';
import { rolarPara } from '@/util/rolar';
import { useCarregar } from '@/util/useCarregar';

const VAZIO = { nome: '', observacao: '', categoria: '', l1: '', l2: '', l3: '', l4: '' };
type Formulario = typeof VAZIO;

const doFormulario = (f: Formulario): DadosFamilia => {
  const dias = (v: string) => (v === '' ? null : parseInt(v, 10));
  return {
    name: f.nome,
    notes: f.observacao || null,
    category: f.categoria || null,
    clearCategory: f.categoria === '',
    leadRequestToQuote: dias(f.l1),
    leadQuoteToApproval: dias(f.l2),
    leadApprovalToPo: dias(f.l3),
    leadPoToDelivery: dias(f.l4),
    applyLeadTimes: true,
  };
};

/** Prazos-meta somados, no formato "2 + 3 + 1 + 15 = 21". */
export function resumoPrazos(f: Familia): string {
  if (f.leadTotal == null) return 'sem meta definida';
  const partes = [f.leadRequestToQuote ?? 0, f.leadQuoteToApproval ?? 0, f.leadApprovalToPo ?? 0, f.leadPoToDelivery ?? 0];
  return `${partes.join(' + ')} = ${f.leadTotal}`;
}

export function Familias() {
  const usuario = useUsuario();
  const mantem = podeManterCatalogo(usuario) && temModulo(usuario, 'PRODUTOS');
  const { avisar } = useToast();
  const [editando, setEditando] = useState<Familia | null>(null);
  const [form, setForm] = useState<Formulario>(VAZIO);
  const [salvando, setSalvando] = useState(false);

  const { dados, erro, carregando, recarregar } = useCarregar(
    async (signal) => ({
      familias: await listarFamilias(mantem, signal),
      // a contagem é informativa: se o usuário não enxerga o catálogo, a coluna fica zerada
      usoPorFamilia: await contarProdutosPorFamilia(signal).catch(() => ({} as Record<string, number>)),
    }),
    [mantem],
  );

  const categorias = useMemo(
    () => [...new Set((dados?.familias ?? []).map((f) => f.category).filter(Boolean) as string[])].sort(),
    [dados],
  );

  function editar(f: Familia) {
    setEditando(f);
    setForm({
      nome: f.name, observacao: f.notes ?? '', categoria: f.category ?? '',
      l1: f.leadRequestToQuote?.toString() ?? '', l2: f.leadQuoteToApproval?.toString() ?? '',
      l3: f.leadApprovalToPo?.toString() ?? '', l4: f.leadPoToDelivery?.toString() ?? '',
    });
    rolarPara('form-familia');
  }
  const cancelar = () => { setEditando(null); setForm(VAZIO); };

  async function enviar(ev: FormEvent) {
    ev.preventDefault();
    setSalvando(true);
    try {
      if (editando) {
        await atualizarFamilia(editando.id, doFormulario(form));
        avisar('Família atualizada — os produtos dela acompanham o novo nome.');
      } else {
        await criarFamilia(doFormulario(form));
        avisar('Família cadastrada.');
      }
      cancelar();
      recarregar();
    } catch (e) { avisar(e instanceof Error ? e.message : 'Falha ao salvar a família.', 'erro'); }
    finally { setSalvando(false); }
  }

  const [aExcluir, setAExcluir] = useState<Familia | null>(null);

  async function excluir(f: Familia) {
    setAExcluir(null);
    try {
      await excluirFamilia(f.id);
      avisar(`Família ${f.name} excluída.`);
      recarregar();
    } catch (e) { avisar(e instanceof Error ? e.message : 'Falha ao excluir a família.', 'erro'); }
  }

  async function alternarSituacao(f: Familia) {
    try {
      await atualizarFamilia(f.id, { active: !f.active });
      avisar('Família atualizada.');
      recarregar();
    } catch (e) { avisar(e instanceof Error ? e.message : 'Falha ao atualizar.', 'erro'); }
  }

  const campo = (k: keyof Formulario) => ({
    value: form[k],
    onChange: (ev: { target: { value: string } }) => setForm((f) => ({ ...f, [k]: ev.target.value })),
  });

  return (
    <>
      <Painel titulo="Famílias de Produtos">
        <Nota>
          A família é escolhida em lista no cadastro do produto, então a mesma família não aparece escrita de formas
          diferentes. Renomear aqui renomeia em todos os produtos.
        </Nota>
        {erro && <Erro>{erro}</Erro>}
        {carregando && !dados && <Carregando />}
        {dados && !dados.familias.length && <Vazio>Nenhuma família cadastrada ainda.</Vazio>}
        {dados && dados.familias.length > 0 && (
          <div className="mt-3 overflow-x-auto">
            <table data-testid="tabela-familias" className="min-w-[900px]">
              <thead>
                <tr>
                  <th>Família</th><th>Categoria</th><th>Produtos</th><th>Prazos-meta (dias)</th>
                  <th>Observação</th><th>Situação</th>{mantem && <th>Ações</th>}
                </tr>
              </thead>
              <tbody>
                {dados.familias.map((f) => (
                  <tr key={f.id} data-familia={f.name}>
                    <td className="font-semibold">{f.name}</td>
                    <td>{f.category ?? <span className="sub">—</span>}</td>
                    <td>{dados.usoPorFamilia[f.name] ?? 0}</td>
                    <td className="sub">{resumoPrazos(f)}</td>
                    <td className="sub">{f.notes || '—'}</td>
                    <td><BadgeAtivo ativo={f.active} rotuloAtivo="ATIVA" rotuloInativo="INATIVA" /></td>
                    {mantem && (
                      <td className="whitespace-nowrap">
                        <div className="flex gap-1.5">
                          <button type="button" className="botao-secundario !py-1.5" onClick={() => editar(f)}>Editar</button>
                          <button type="button" className="botao-secundario !py-1.5"
                            onClick={() => alternarSituacao(f)}>{f.active ? 'Inativar' : 'Reativar'}</button>
                          {/* só a família vazia sai de vez (IC-ERR-031): com produto dentro, eles
                              ficariam numa família que o cadastro não conhece. O número da coluna é
                              dos ativos — o servidor confere também os inativos */}
                          <button type="button" className="botao-perigo !py-1.5"
                            disabled={(dados.usoPorFamilia[f.name] ?? 0) > 0}
                            title={(dados.usoPorFamilia[f.name] ?? 0) > 0
                              ? 'Tem produtos: mova-os para outra família ou inative a família' : undefined}
                            onClick={() => setAExcluir(f)}>Excluir</button>
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
        <Painel id="form-familia" titulo={editando ? `Editar família — ${editando.name}` : 'Nova família'}>
          <form onSubmit={enviar}>
            <Campo id="fam-nome" rotulo="Nome">
              <input id="fam-nome" required minLength={3} placeholder="ex.: MATERIAL DE LIMPEZA" {...campo('nome')} />
            </Campo>
            <Campo id="fam-observacao" rotulo="Observação" className="mt-3">
              <input id="fam-observacao" placeholder="opcional — o que entra nesta família" {...campo('observacao')} />
            </Campo>
            <Campo id="fam-categoria" className="mt-3" rotulo="Categoria"
              dica="(agrupador de famílias para o spend — ex.: MRO, EPI, EMBALAGENS)">
              <input id="fam-categoria" list="fam-categorias" placeholder="opcional — escolha ou digite uma nova" {...campo('categoria')} />
              <datalist id="fam-categorias">{categorias.map((c) => <option key={c} value={c} />)}</datalist>
            </Campo>

            <p className="mt-4 mb-1 text-[12.5px] font-semibold text-texto-suave">
              Prazos-meta do processo <span className="font-normal">(dias corridos; o dashboard compara com o realizado)</span>
            </p>
            <Grade2>
              <Campo id="fam-l1" rotulo="Solicitação → cotação">
                <input id="fam-l1" type="number" min={0} max={365} placeholder="ex.: 2" {...campo('l1')} />
              </Campo>
              <Campo id="fam-l2" rotulo="Cotação → aprovação">
                <input id="fam-l2" type="number" min={0} max={365} placeholder="ex.: 3" {...campo('l2')} />
              </Campo>
            </Grade2>
            <Grade2 className="mt-3">
              <Campo id="fam-l3" rotulo="Aprovação → O.C.">
                <input id="fam-l3" type="number" min={0} max={365} placeholder="ex.: 1" {...campo('l3')} />
              </Campo>
              <Campo id="fam-l4" rotulo="O.C. → entrega">
                <input id="fam-l4" type="number" min={0} max={365} placeholder="ex.: 15" {...campo('l4')} />
              </Campo>
            </Grade2>

            <div className="mt-4 flex flex-wrap gap-2">
              <button type="submit" className="botao" disabled={salvando}>
                {salvando ? 'Salvando…' : editando ? 'Salvar alterações' : 'Cadastrar família'}
              </button>
              {editando && <button type="button" className="botao-secundario" onClick={cancelar}>Cancelar edição</button>}
            </div>
          </form>
    </Painel>
      )}

      {aExcluir && (
        <Confirmacao titulo="Excluir família" perigo rotuloConfirmar="Excluir"
          mensagem={<>Excluir a família <strong>{aExcluir.name}</strong> de vez? Isto não se desfaz. Para só tirá-la de uso, prefira <strong>Inativar</strong>.</>}
          aoConfirmar={() => excluir(aExcluir)} aoFechar={() => setAExcluir(null)} />
      )}
    </>
  );
}
