import { useEffect, useMemo, useState, type ChangeEvent, type FormEvent } from 'react';
import {
  atualizarProduto, buscarProdutos, criarProduto, enviarFoto, resumoCatalogo, tiposDeProduto,
  type Produto, type ResumoCatalogo, type TipoDeProduto,
} from '@/api/catalogo';
import { listarFamilias, type Familia } from '@/api/familias';
import { listarFornecedores, type Fornecedor } from '@/api/fornecedores';
import { Carregando, Erro, Painel, Vazio } from '@/componentes/basicos';
import { BadgeAtivo, Campo, Grade2, Nota } from '@/componentes/formulario';
import { Miniatura, Visor } from '@/componentes/Miniatura';
import { useToast } from '@/componentes/Toast';
import { podeManterCatalogo, temModulo } from '@/dominio/papeis';
import { useUsuario } from '@/sessao/SessaoProvider';
import { moeda } from '@/util/formato';
import { rolarPara } from '@/util/rolar';
import { useCarregar } from '@/util/useCarregar';
import {
  daFornecedorDoProduto, faltaCa, fornecedoresDoFormulario, FornecedoresDoProduto, novaLinhaFornecedor,
  type LinhaFornecedor,
} from './FornecedoresDoProduto';
import { PainelImportacao } from './PainelImportacao';

/** A tela mostra um bloco por vez: o acervo tem milhares de itens. */
export const POR_PAGINA = 50;

const VAZIO = { codigo: '', familia: '', descricao: '', unidade: '', preco: '', tipo: '' };
type Formulario = typeof VAZIO;
const mensagem = (e: unknown, padrao: string) => (e instanceof Error ? e.message : padrao);

/** Frase do resumo do catálogo, com a pendência de C.A. quando houver. */
export function resumoEmTexto(s: ResumoCatalogo): string {
  const partes = [`${s.active} produto(s) ativo(s)`];
  if (s.inactive) partes.push(`${s.inactive} inativo(s)`);
  if (s.compliancePending) partes.push(`${s.compliancePending} sem C.A. de fornecedor`);
  return partes.join(' · ');
}

export function Produtos() {
  const usuario = useUsuario();
  const mantem = podeManterCatalogo(usuario) && temModulo(usuario, 'PRODUTOS');
  const { avisar } = useToast();

  const [busca, setBusca] = useState('');
  const [familiaFiltro, setFamiliaFiltro] = useState('');
  const [criterio, setCriterio] = useState<{ q: string; familia: string } | null>(null);
  const [editando, setEditando] = useState<Produto | null>(null);
  const [form, setForm] = useState<Formulario>(VAZIO);
  const [fornecedoresDoItem, setFornecedoresDoItem] = useState<LinhaFornecedor[]>([]);
  const [foto, setFoto] = useState<File | null>(null);
  const [formAberto, setFormAberto] = useState(false);
  const [importando, setImportando] = useState(false);
  const [salvando, setSalvando] = useState(false);
  const [ampliada, setAmpliada] = useState<{ url: string; descricao: string } | null>(null);

  const apoio = useCarregar(
    async (signal) => ({
      resumo: await resumoCatalogo(signal),
      familias: await listarFamilias(false, signal).catch(() => [] as Familia[]),
      tipos: await tiposDeProduto(signal).catch(() => [] as TipoDeProduto[]),
      fornecedores: mantem ? await listarFornecedores(false, signal).catch(() => [] as Fornecedor[]) : [],
    }),
    [mantem],
  );

  const resultado = useCarregar(
    async (signal) => (criterio ? buscarProdutos({ ...criterio, incluirInativos: mantem }, signal) : null),
    [criterio, mantem],
  );

  const tipos = apoio.dados?.tipos ?? [];
  const tipoEscolhido = tipos.find((t) => t.key === form.tipo);
  const exigeCa = !!tipoEscolhido?.requiresCa;

  // um tipo que exige C.A. faz o campo aparecer em cada fornecedor já listado
  useEffect(() => {
    if (exigeCa && formAberto && !fornecedoresDoItem.length) setFornecedoresDoItem([novaLinhaFornecedor()]);
  }, [exigeCa, formAberto, fornecedoresDoItem.length]);

  const familiasDisponiveis = useMemo(() => {
    const doCadastro = (apoio.dados?.familias ?? []).map((f) => f.name);
    const doAcervo = (apoio.dados?.resumo.families ?? []).map((f) => f.family);
    return [...new Set([...doCadastro, ...doAcervo])].sort();
  }, [apoio.dados]);

  function buscar() {
    const q = busca.trim();
    if (!q && !familiaFiltro) {
      avisar('Digite um código ou parte da descrição, ou escolha uma família.', 'erro');
      return;
    }
    setCriterio({ q, familia: familiaFiltro });
  }
  function limpar() {
    setBusca(''); setFamiliaFiltro(''); setCriterio(null);
  }

  function novo() {
    setEditando(null); setForm(VAZIO); setFornecedoresDoItem([]); setFoto(null);
    setImportando(false); setFormAberto(true);
    rolarPara('form-produto');
  }
  function editar(p: Produto) {
    setEditando(p);
    setForm({
      codigo: p.code, familia: p.family, descricao: p.description, unidade: p.unitOfMeasure ?? '',
      preco: p.referencePrice != null ? String(p.referencePrice) : '', tipo: p.productType ?? '',
    });
    setFornecedoresDoItem(p.suppliers.map(daFornecedorDoProduto));
    setFoto(null); setImportando(false); setFormAberto(true);
    rolarPara('form-produto');
  }
  const fechar = () => { setFormAberto(false); setEditando(null); setForm(VAZIO); setFornecedoresDoItem([]); setFoto(null); };

  function recarregar() {
    apoio.recarregar();
    if (criterio) resultado.recarregar();
  }

  async function enviar(ev: FormEvent) {
    ev.preventDefault();
    if (faltaCa(fornecedoresDoItem, exigeCa)) {
      avisar('EPI e EPC exigem o C.A.: informe o número em pelo menos um fornecedor do produto.', 'erro');
      return;
    }
    const corpo = {
      description: form.descricao,
      family: form.familia,
      unitOfMeasure: form.unidade || null,
      referencePrice: form.preco ? parseFloat(form.preco) : null,
      productType: form.tipo || null,
      suppliers: fornecedoresDoFormulario(fornecedoresDoItem),
      stockControlled: true as const,
      purchasable: true as const,
    };
    setSalvando(true);
    try {
      if (editando) {
        await atualizarProduto(editando.id, corpo);
        if (foto) await enviarFoto(editando.id, foto);
        avisar('Produto atualizado.');
        fechar();
      } else {
        const criado = await criarProduto({ ...corpo, code: form.codigo || null });
        if (foto) await enviarFoto(criado.id, foto);
        avisar(`Produto ${criado.code} adicionado ao catálogo.`);
        // a família fica: cadastrar vários produtos da mesma família é o caso comum
        setForm({ ...VAZIO, familia: form.familia });
        setFornecedoresDoItem([]); setFoto(null);
      }
      recarregar();
    } catch (e) { avisar(mensagem(e, 'Falha ao salvar o produto.'), 'erro'); }
    finally { setSalvando(false); }
  }

  async function alternarSituacao(p: Produto) {
    try {
      await atualizarProduto(p.id, { active: !p.active });
      avisar('Produto atualizado.');
      recarregar();
    } catch (e) { avisar(mensagem(e, 'Falha ao atualizar.'), 'erro'); }
  }

  const campo = (k: keyof Formulario) => ({
    value: form[k],
    onChange: (ev: { target: { value: string } }) => setForm((f) => ({ ...f, [k]: ev.target.value })),
  });

  const encontrados = resultado.dados ?? [];
  const mostrados = encontrados.slice(0, POR_PAGINA);

  return (
    <>
      <Painel titulo="Cadastro de Produtos" acoes={mantem && (
        <>
          <button type="button" className="botao" onClick={novo}>+ Novo produto</button>
          <button type="button" className="botao-secundario" onClick={() => { setFormAberto(false); setImportando((i) => !i); }}>
            Importar planilha
          </button>
        </>
      )}>
        {apoio.erro && <Erro>{apoio.erro}</Erro>}
        <Nota>
          {apoio.dados
            ? <>{resumoEmTexto(apoio.dados.resumo)} — busque pelo código ou pela descrição para ver e editar um produto.</>
            : 'Carregando o resumo do catálogo…'}
        </Nota>

        <Grade2 className="mt-3">
          <Campo id="prod-busca" rotulo="Buscar produto">
            <input id="prod-busca" placeholder="código ou parte da descrição — ex.: 12003, camisa, detergente"
              value={busca} onChange={(e) => setBusca(e.target.value)}
              onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); buscar(); } }} />
          </Campo>
          <Campo id="prod-familia" rotulo="Filtrar por família">
            <select id="prod-familia" value={familiaFiltro} onChange={(e) => setFamiliaFiltro(e.target.value)}>
              <option value="">Todas as famílias</option>
              {(apoio.dados?.resumo.families ?? []).map((f) => (
                <option key={f.family} value={f.family}>{f.family} ({f.count})</option>
              ))}
            </select>
          </Campo>
        </Grade2>
        <div className="mt-3 flex flex-wrap gap-2">
          <button type="button" className="botao" onClick={buscar}>Buscar</button>
          <button type="button" className="botao-secundario" onClick={limpar}>Limpar</button>
        </div>
      </Painel>

      {criterio && (
        <Painel
          titulo={`Resultado da busca — ${encontrados.length} produto(s)`}
          acoes={<button type="button" className="botao-secundario" onClick={limpar}>Fechar resultado</button>}>
          {resultado.erro && <Erro>{resultado.erro}</Erro>}
          {resultado.carregando && !resultado.dados && <Carregando texto="Buscando…" />}
          {resultado.dados && !encontrados.length && <Vazio>Nenhum produto encontrado com esse critério.</Vazio>}
          {mostrados.length > 0 && (
            <div className="overflow-x-auto">
              <table data-testid="tabela-produtos" className="min-w-[1240px]">
                <thead>
                  <tr>
                    <th>Foto</th><th>Código</th><th>Produto</th><th>Família</th><th>Tam.</th>
                    <th>Tipo / conformidade</th><th>Fornecedores</th><th>Preço ref.</th><th>Situação</th>
                    {mantem && <th>Ações</th>}
                  </tr>
                </thead>
                <tbody>
                  {mostrados.map((p) => (
                    <tr key={p.id} data-produto={p.code}>
                      <td>
                        {p.imageDocumentId
                          ? <Miniatura documentId={p.imageDocumentId} descricao={p.description}
                              aoAmpliar={(url) => setAmpliada({ url, descricao: p.description })} />
                          : <span className="sub">—</span>}
                      </td>
                      <td className="whitespace-nowrap font-semibold">{p.code}</td>
                      <td className="min-w-[190px]">{p.description}<div className="sub">{p.unitOfMeasure}</div></td>
                      <td>{p.family}</td>
                      <td>{p.size || '—'}</td>
                      <td className="min-w-[180px]">
                        <div className="sub">{p.productTypeLabel || '—'}</div>
                        {p.suppliers.filter((f) => f.caNumber).map((f) => (
                          <div key={f.id ?? f.supplierName} className="sub">C.A. {f.caNumber} · {f.supplierName}</div>
                        ))}
                        {p.compliancePending && <div className="text-[12px] text-perigo">⚠ sem C.A. em nenhum fornecedor</div>}
                      </td>
                      <td className="sub">{p.suppliers.map((f) => f.supplierName).join(' · ') || '—'}</td>
                      <td className="whitespace-nowrap">{p.referencePrice != null ? moeda(p.referencePrice) : '—'}</td>
                      <td><BadgeAtivo ativo={p.active} /></td>
                      {mantem && (
                        <td className="whitespace-nowrap">
                          <div className="flex gap-1.5">
                            <button type="button" className="botao-secundario" onClick={() => editar(p)}>Editar</button>
                            <button type="button" className={p.active ? 'botao-perigo' : 'botao-secundario'}
                              onClick={() => alternarSituacao(p)}>{p.active ? 'Inativar' : 'Reativar'}</button>
                          </div>
                        </td>
                      )}
                    </tr>
                  ))}
                </tbody>
              </table>
              {encontrados.length > mostrados.length && (
                <p className="sub mt-2">
                  Mostrando os {mostrados.length} primeiros de {encontrados.length}. Refine a busca para achar o produto certo.
                </p>
              )}
            </div>
          )}
        </Painel>
      )}

      {mantem && formAberto && (
        <Painel id="form-produto" titulo={editando ? `Editar produto — ${editando.code}` : 'Novo produto'}
          acoes={<button type="button" className="botao-secundario" onClick={fechar}>Fechar</button>}>
          <form onSubmit={enviar}>
            <Grade2>
              <Campo id="prod-codigo" rotulo="Código" dica="(opcional)">
                <input id="prod-codigo" placeholder="deixe vazio para gerar automático" disabled={!!editando}
                  title={editando ? 'O código é a identidade do produto e não muda.' : undefined} {...campo('codigo')} />
              </Campo>
              <Campo id="prod-form-familia" rotulo="Família">
                <select id="prod-form-familia" required {...campo('familia')}>
                  <option value="">Selecione a família…</option>
                  {familiasDisponiveis.map((f) => <option key={f} value={f}>{f}</option>)}
                </select>
              </Campo>
            </Grade2>
            <Nota>Sem código informado, o sistema gera pela família (ex.: MAT-001). Se você já usa um código próprio, informe-o aqui.</Nota>

            <Campo id="prod-descricao" rotulo="Descrição" className="mt-3">
              <input id="prod-descricao" required minLength={3} placeholder="ex.: Detergente neutro 500ml" {...campo('descricao')} />
            </Campo>
            <Grade2 className="mt-3">
              <Campo id="prod-unidade" rotulo="Unidade">
                <input id="prod-unidade" placeholder="UN" {...campo('unidade')} />
              </Campo>
              <Campo id="prod-preco" rotulo="Preço ref. (R$)">
                <input id="prod-preco" type="number" min={0} step="0.01" placeholder="opcional" {...campo('preco')} />
              </Campo>
            </Grade2>

            <Campo id="prod-foto" className="mt-3" rotulo="Foto do produto"
              dica="(PNG, JPG ou WEBP — aparece como miniatura na lista)">
              <input id="prod-foto" type="file" accept="image/png,image/jpeg,image/webp"
                onChange={(ev: ChangeEvent<HTMLInputElement>) => setFoto(ev.target.files?.[0] ?? null)} />
            </Campo>
            {editando && (
              <p className="sub mt-1 flex items-center gap-2">
                {editando.imageDocumentId ? (
                  <>
                    Foto atual:
                    <Miniatura documentId={editando.imageDocumentId} descricao={editando.description}
                      aoAmpliar={(url) => setAmpliada({ url, descricao: editando.description })} />
                    — envie outra para substituir.
                  </>
                ) : 'Nenhuma foto enviada ainda.'}
              </p>
            )}

            <Campo id="prod-tipo" rotulo="Tipo de produto" className="mt-3">
              <select id="prod-tipo" {...campo('tipo')}>
                <option value="">Selecione o tipo…</option>
                {tipos.map((t) => <option key={t.key} value={t.key}>{t.label}</option>)}
              </select>
            </Campo>
            {exigeCa && (
              <p className="mt-2 rounded-lg bg-aviso-fundo px-3 py-2 text-[13px] text-aviso" data-testid="aviso-ca">
                EPI e EPC exigem o <strong>C.A.</strong>: informe o número em cada fornecedor abaixo — o mesmo produto
                costuma ter um C.A. por fornecedor.
              </p>
            )}

            <p className="mb-2 mt-5 text-[12.5px] font-semibold text-texto-suave">
              Fornecedores deste produto <span className="font-normal">
                (escolha do cadastro de fornecedores; use “Outro” para quem ainda não está lá)</span>
            </p>
            <FornecedoresDoProduto linhas={fornecedoresDoItem} fornecedores={apoio.dados?.fornecedores ?? []}
              exigeCa={exigeCa} aoMudar={setFornecedoresDoItem} />
            <button type="button" className="botao-secundario mt-3"
              onClick={() => setFornecedoresDoItem((l) => [...l, novaLinhaFornecedor()])}>+ Adicionar fornecedor</button>

            <div className="mt-4 flex flex-wrap gap-2">
              <button type="submit" className="botao" disabled={salvando}>
                {salvando ? 'Salvando…' : editando ? 'Salvar alterações' : 'Adicionar produto'}
              </button>
              <button type="button" className="botao-secundario" onClick={fechar}>Cancelar</button>
            </div>
          </form>
        </Painel>
      )}

      {mantem && importando && (
        <PainelImportacao familias={familiasDisponiveis} tipos={tipos}
          aoImportar={recarregar} aoFechar={() => setImportando(false)} />
      )}

      {ampliada && (
        <Visor url={ampliada.url} descricao={ampliada.descricao} aoFechar={() => setAmpliada(null)} />
      )}
    </>
  );
}
